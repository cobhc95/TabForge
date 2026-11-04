using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Ports;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the playing family-switch probe — a kit tried on a pitched track while the song plays
//   auditions on the shared output's drum channel (9), and Cancel/close must send the song's own
//   program again on BOTH the original and the audition channel (the leak this file guards);
//   commit/undo/redo keep the shared channel on the other track's program with no global panic.
// Does not own: the choice rules (InstrumentChoice), the stopped path (InstrumentChoiceTests) or the picker windows.
// Tests: InstrumentChoice, EngineProgramSink, PlaybackEngine.EndProgramPreview, InstrumentPickerFlow.
[Collection(TimingCollection.Name)]   // runs a wall-clock playback engine: keep it out of the parallel tests
public sealed class InstrumentChoicePlayingFamilySwitchTests
{
    private const int SettleMs = 600, WaitMs = 3000;

    [Fact]
    public void PlayingFamilySwitchCancelRestoresBothTheOriginalAndTheAuditionChannel()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var origChannel = track.MidiChannel;
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit);
        var sink = new RecordingSink { IsPlaying = true };
        var choice = new InstrumentChoice(song, track, sink);

        choice.Preview(kit.Name);

        var auditionChannel = track.MidiChannel;
        Assert.NotEqual(origChannel, auditionChannel);
        Assert.Contains(sink.Auditions, a => a.Channel == auditionChannel);
        Assert.DoesNotContain(sink.Begins, b => b.Channel == auditionChannel);

        choice.Cancel();

        var ended = sink.Ends.Select(e => e.Channel).ToHashSet();
        Assert.Contains(origChannel, ended);
        Assert.Contains(auditionChannel, ended);
        Assert.All(sink.Ends, e => Assert.False(e.Keep));
        // A second close (the close box after Cancel) sends nothing new.
        var ends = sink.Ends.Count;
        choice.Cancel();
        Assert.Equal(ends, sink.Ends.Count);
    }

    [Fact]
    public void PlayingMultiPreviewCancelRestoresEveryTouchedChannel()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var origChannel = track.MidiChannel;
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit);
        var otherKit = InstrumentCatalog.All.First(e => e.IsDrumKit && e.Program != kit.Program);
        var pitched = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program != track.MidiProgram);
        var sink = new RecordingSink { IsPlaying = true };
        var choice = new InstrumentChoice(song, track, sink);

        choice.Preview(kit.Name);
        var auditionChannel = track.MidiChannel;
        choice.Preview(otherKit.Name);
        choice.Preview(pitched.Name);
        Assert.Equal(origChannel, track.MidiChannel);

        choice.Cancel();

        var ended = sink.Ends.Select(e => e.Channel).ToHashSet();
        Assert.Contains(origChannel, ended);
        Assert.Contains(auditionChannel, ended);
        Assert.All(sink.Ends, e => Assert.False(e.Keep));
    }

    [Fact]
    public void PlayingFamilySwitchCommitEndsOnlyTheOriginalChannelWithKeep()
    {
        // The commit path is unchanged: dropping the (non-existent) preview override plus the
        // TracksChanged re-arm reconverges every channel, so no extra restore is sent here.
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var origChannel = track.MidiChannel;
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit);
        var sink = new RecordingSink { IsPlaying = true };
        var choice = new InstrumentChoice(song, track, sink);
        song.IsDirty = false;
        var doc = new SongDocument(() => null) { Project = song };

        choice.Preview(kit.Name);
        Assert.NotEqual(origChannel, track.MidiChannel);

        var outcome = choice.Commit(doc, kit.Name);

        Assert.True(outcome.Changed);
        var end = Assert.Single(sink.Ends);
        Assert.Equal(origChannel, end.Channel);
        Assert.True(end.Keep);
    }

    [Fact]
    public void PlayingFamilySwitchCancelSendsTheOtherTracksProgramBackOnTheSharedChannel()
    {
        using var rig = new PlayingRig();
        var drumProgram = rig.Song.Tracks.First(t => !t.IsAudio && t.MidiChannel == 9).MidiProgram;
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit && e.Program != drumProgram);
        var device = rig.Track.MidiOutputDeviceId;
        var origChannel = rig.Track.MidiChannel;

        rig.Choice.Preview(kit.Name);

        Assert.True(Until(() => rig.Output.LastProgram(device, 9) == kit.Program), "the audition sounds the kit on the shared drum channel");
        Assert.True(Until(() => rig.Output.NoteOns(device, 9) > 0), "the audition note plays on the drum channel");

        rig.Choice.Cancel();

        Assert.True(Until(() => rig.Output.LastProgram(device, 9) == drumProgram), "the drum channel gets the other track's own program again (ch9 history: " + rig.Output.ProgramHistory(device, 9) + ")");
        Assert.True(Until(() => rig.Output.LastProgram(device, origChannel) == rig.OriginalProgram), "the original channel gets its own program again");
        // The audition note's lifecycle is preserved: its note-off arrives, so no note sticks.
        Assert.True(Until(() => rig.Output.NoteOffs(device, 9, 36) >= rig.Output.NoteOns(device, 9, 36) && rig.Output.NoteOffs(device, 9, 36) > 0),
            "the audition note gets its note-off");
        // No global panic stood in for the targeted restore.
        Assert.Equal(0, rig.Output.Panics);
        Assert.Equal(0, rig.Output.Count(device, 0xB0 | 9, 123));
    }

    [Fact]
    public void CommittedFamilySwitchUndoThenRedoKeepsTheSharedChannelCorrectWithoutPanic()
    {
        // Characterization (passes before and after the Cancel fix): commit, undo and redo of a
        // playing family switch keep the other drum track's program on the shared channel, the
        // model round-trips, and no global panic stands in. Close (the picker's X) runs the same
        // choice.Cancel as the Cancel path above (InstrumentPickerFlow.ChooseAsync finally).
        using var rig = new PlayingRig();
        using var watch = new MainWindowLiveMix(rig.Sessions);
        watch.Bind(rig.Doc);
        var drumProgram = rig.Song.Tracks.First(t => !t.IsAudio && t.MidiChannel == 9).MidiProgram;
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit && e.Program != drumProgram);
        var device = rig.Track.MidiOutputDeviceId;
        var origChannel = rig.Track.MidiChannel;

        rig.Choice.Preview(kit.Name);
        var outcome = rig.Choice.Commit(rig.Doc, kit.Name);
        Assert.True(outcome.Changed);
        Assert.Equal(9, rig.Track.MidiChannel);
        Assert.Equal(kit.Program, rig.Track.MidiProgram);
        // The re-arm converges the shared drum channel to the song's own program at once:
        // the other drum track keeps its sound after the commit.
        Assert.True(Until(() => rig.Output.LastProgram(device, 9) == drumProgram), "the other track keeps its program on the shared channel after commit");

        DocumentEdits.Undo(rig.Doc);
        // Undo replaces Doc.Project, so the pre-undo Track reference is stale: re-fetch by index.
        var undone = rig.Doc.Project.Tracks[rig.Song.Tracks.IndexOf(rig.Track)];
        Assert.Equal(origChannel, undone.MidiChannel);
        Assert.Equal(rig.OriginalProgram, undone.MidiProgram);
        Assert.True(Until(() => rig.Output.LastProgram(device, 9) == drumProgram), "undo keeps the drum channel on the other track's program");
        Assert.True(Until(() => rig.Output.LastProgram(device, origChannel) == rig.OriginalProgram), "undo gives the original channel back its program");

        DocumentEdits.Redo(rig.Doc);
        var redone = rig.Doc.Project.Tracks[rig.Song.Tracks.IndexOf(rig.Track)];
        Assert.Equal(9, redone.MidiChannel);
        Assert.Equal(kit.Program, redone.MidiProgram);
        Assert.True(Until(() => rig.Output.LastProgram(device, 9) == drumProgram), "redo keeps the drum channel on the other track's program");

        // No panic guard here: each edit legitimately re-seeks the playing song (a debounced
        // engine reset plus setup resend, pre-existing behaviour); the targeted pushes above are
        // what keep the shared channel right. The no-panic guard lives on the Cancel test,
        // where no seek may occur at all.
        Assert.True(rig.Sessions.For(rig.Doc)!.IsPlaying, "the song keeps playing");
    }

    private sealed class RecordingSink : IProgramSink
    {
        public bool IsPlaying { get; set; }
        public readonly List<(int Device, int Channel, int Program)> Begins = new();
        public readonly List<(int Device, int Channel, bool Keep)> Ends = new();
        public readonly List<(int Device, int Channel, int Program)> Auditions = new();
        public void BeginPreview(int deviceId, int channel, int program) => Begins.Add((deviceId, channel, program));
        public void EndPreview(int deviceId, int channel, bool keep) => Ends.Add((deviceId, channel, keep));
        public void Audition(int deviceId, int channel, int program, int note, int lengthMs) => Auditions.Add((deviceId, channel, program));
    }

    private static bool Until(Func<bool> condition)
    {
        for (var waited = 0; waited < WaitMs; waited += 20)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    /// <summary>A playing song in a document over sessions whose engines write to one recording output, with a choice on a pitched track.</summary>
    private sealed class PlayingRig : IDisposable
    {
        public PlayingRig()
        {
            Song = FullDemoSongFactory.Create();
            Doc = new SongDocument(() => null) { Project = Song };
            Track = Song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
            OriginalProgram = Track.MidiProgram;
            Song.IsDirty = false;
            Sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new PlaybackEngine(Output));
            Sessions.For(Doc)!.PlayOrPause();
            Thread.Sleep(SettleMs);
            Output.Clear();
            Choice = new InstrumentChoice(Song, Track, new EngineProgramSink(Sessions, Doc, () => new PluginSettings()));
        }

        public SongProject Song { get; }
        public SongDocument Doc { get; }
        public TrackModel Track { get; }
        public int OriginalProgram { get; }
        public Recorder Output { get; } = new();
        public AvaloniaPlaybackSessions Sessions { get; }
        public InstrumentChoice Choice { get; }

        public void Dispose() => Sessions.Dispose();
    }

    private sealed class Recorder : IMidiOutput
    {
        private readonly List<(int Device, int Status, int Data1, int Data2)> _sent = new();
        private readonly List<(long Ms, int Device, int Status, int Data1)> _programs = new();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private int _panics;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public int Panics { get { lock (_sent) return _panics; } }
        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_sent)
            {
                _sent.Add((deviceId, status, data1, data2));
                if ((status & 0xF0) == 0xC0) _programs.Add((_clock.ElapsedMilliseconds, deviceId, status, data1));
            }
        }
        public int? LastProgram(int device, int channel)
        {
            lock (_sent)
            {
                var last = _sent.FindLast(m => m.Device == device && m.Status == (0xC0 | channel));
                return last.Status == 0 ? null : last.Data1;
            }
        }
        public int Count(int device, int status, int data1)
        {
            lock (_sent) return _sent.Count(m => m.Device == device && m.Status == status && m.Data1 == data1);
        }
        public string ProgramHistory(int device, int channel)
        {
            lock (_sent) return string.Join(" ", _programs.Where(m => m.Device == device && (m.Status & 0x0F) == channel).Select(m => $"t={m.Ms}:{m.Data1}"));
        }
        public int NoteOns(int device, int channel)
        {
            lock (_sent) return _sent.Count(m => m.Device == device && m.Status == (0x90 | channel) && m.Data2 > 0);
        }
        public int NoteOns(int device, int channel, int pitch)
        {
            lock (_sent) return _sent.Count(m => m.Device == device && m.Status == (0x90 | channel) && m.Data1 == pitch && m.Data2 > 0);
        }
        public int NoteOffs(int device, int channel, int pitch)
        {
            lock (_sent) return _sent.Count(m => m.Device == device && m.Data1 == pitch &&
                ((m.Status == (0x80 | channel)) || (m.Status == (0x90 | channel) && m.Data2 == 0)));
        }
        public void Clear() { lock (_sent) { _sent.Clear(); _programs.Clear(); _panics = 0; } }
        public void ResetAll() { lock (_sent) _panics++; }
        public void Close() { }
        public void Dispose() { }
    }
}
