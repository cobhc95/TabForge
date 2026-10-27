using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a track deleted while its song plays falls silent at once (no note-on of it after the delete) while the others play on.
/// Does not own: the engine sync of the delete (TrackStructureTests).
/// Tests: this class.
/// </summary>
public class PlaybackDeletedTrackSilenceTests
{
    private const int PlayBeforeDeleteMs = 700;
    private const int ListenMs = 1500;

    private sealed class ChannelRecorder : IMidiOutput
    {
        private readonly List<int> _channels = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) == 0x90 && data2 > 0) lock (_channels) _channels.Add(status & 0x0F);
        }
        public int NoteOnsOn(int channel) { lock (_channels) return _channels.Count(c => c == channel); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    /// <summary>A synth that ignores CC123: tracks the notes still sounding per (channel, note).</summary>
    private sealed class HangingSynth : IMidiOutput
    {
        public readonly List<(int Status, int Note)> Offs = new();
        private readonly HashSet<(int Channel, int Note)> _sounding = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_sounding)
            {
                var kind = status & 0xF0;
                if (kind == 0x90 && data2 > 0) _sounding.Add((status & 0x0F, data1));
                else if (kind == 0x80 || kind == 0x90) { _sounding.Remove((status & 0x0F, data1)); Offs.Add((status, data1)); }
            }
        }
        public int Sounding(int channel) { lock (_sounding) return _sounding.Count(s => s.Channel == channel); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static ScoreEvent Note(int track, int channel, int note, bool on) =>
        new() { TrackIndex = track, Status = (on ? 0x90 : 0x80) | channel, Data1 = note, Data2 = on ? 90 : 0 };

    [Fact]
    public void ADeletedTracksSoundingNotesGetExplicitNoteOffsEvenOnASharedChannel()
    {
        var synth = new HangingSynth();
        using var engine = new PlaybackEngine(synth);
        foreach (var n in new[] { 60, 64, 67 }) engine.NoteSounding(Note(0, 2, n, true));
        engine.NoteSounding(Note(1, 2, 72, true));   // another track's note on the same channel

        engine.ReleaseSoundingNotes(new[] { true, false });

        Assert.Equal(3, synth.Offs.Count);
        Assert.Equal(new[] { 60, 64, 67 }, synth.Offs.Select(o => o.Note).OrderBy(n => n).ToArray());
        engine.ReleaseSoundingNotes(new[] { true, false });
        Assert.Equal(3, synth.Offs.Count);   // nothing is released twice
    }

    [Fact]
    public void ADeletedTrackLeavesNoHangingNoteOnASynthThatIgnoresAllNotesOff()
    {
        var song = FullDemoSongFactory.Create();
        var synth = new HangingSynth();
        using var engine = new PlaybackEngine(synth);
        engine.Start(song, new PlaybackOptions { LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 }, _ => { }, () => { });
        Thread.Sleep(PlayBeforeDeleteMs);
        var timeline = engine.Timeline!;
        var channelsOf = timeline.Events.Where(e => e.TrackIndex >= 0 && !e.IsMetronome).GroupBy(e => e.Status & 0x0F).ToDictionary(g => g.Key, g => g.Select(e => e.TrackIndex).Distinct().ToList());
        var victim = timeline.Events.Where(e => e.IsNoteOn && !e.IsMetronome && e.TrackIndex >= 0).Select(e => e.TrackIndex).First(t => channelsOf.Where(c => c.Value.Contains(t)).All(c => c.Value.Count == 1));
        var channel = channelsOf.First(c => c.Value.Contains(victim)).Key;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (synth.Sounding(channel) == 0 && DateTime.UtcNow < deadline) Thread.Sleep(2);
        Assert.True(synth.Sounding(channel) > 0, "a note of the track sounds");

        engine.SilenceTracks(new[] { song.Tracks[victim].Id });
        Thread.Sleep(200);
        var hanging = synth.Sounding(channel);
        engine.Stop();

        Assert.Equal(0, hanging);
    }

    [Fact]
    public void ADeletedTrackFallsSilentAtOnceWhileTheSongPlaysOn()
    {
        var song = FullDemoSongFactory.Create();
        var output = new ChannelRecorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions { LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 }, _ => { }, () => { });
        Thread.Sleep(PlayBeforeDeleteMs);
        var timeline = engine.Timeline!;
        var from = engine.Playhead().ElapsedMs;
        // A track that plays notes in the coming stretch on a channel no other track uses.
        var notes = timeline.Events.Where(e => e.IsNoteOn && !e.IsMetronome && !e.IsSetup && e.TrackIndex >= 0 && e.TimeMs > from + 100 && e.TimeMs < from + ListenMs).ToList();
        var channelsOf = timeline.Events.Where(e => e.TrackIndex >= 0 && !e.IsMetronome).GroupBy(e => e.Status & 0x0F).ToDictionary(g => g.Key, g => g.Select(e => e.TrackIndex).Distinct().ToList());
        var victim = notes.GroupBy(e => e.TrackIndex).Select(g => g.Key).FirstOrDefault(t => channelsOf.Where(c => c.Value.Contains(t)).All(c => c.Value.Count == 1));
        var other = notes.Select(e => e.TrackIndex).FirstOrDefault(t => t != victim);
        Assert.True(notes.Any(e => e.TrackIndex == victim) && notes.Any(e => e.TrackIndex != victim), "the demo song plays two tracks in the window");
        var victimChannel = channelsOf.First(c => c.Value.Contains(victim)).Key;
        var otherChannel = channelsOf.First(c => c.Value.Contains(other)).Key;

        engine.SilenceTracks(new[] { song.Tracks[victim].Id });
        Thread.Sleep(50);   // a note the scheduler had already started
        var victimBefore = output.NoteOnsOn(victimChannel);
        var otherBefore = output.NoteOnsOn(otherChannel);
        Thread.Sleep(ListenMs);
        var victimAfter = output.NoteOnsOn(victimChannel);
        var otherAfter = output.NoteOnsOn(otherChannel);
        var stillPlaying = engine.IsPlaying;
        engine.Stop();

        Assert.True(stillPlaying);
        Assert.Equal(victimBefore, victimAfter);
        Assert.True(otherAfter > otherBefore, "the other tracks play on");
    }
}
