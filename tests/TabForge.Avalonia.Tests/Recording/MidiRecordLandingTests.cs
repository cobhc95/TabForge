using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: how recorded MIDI takes land (new lane or merged into the clip underneath, optional input quantize incl. triplets), a window closing
/// mid-record keeping its take, and the piano roll's live overlay model.
/// Does not own: the capture itself (MidiTakeCaptureTests) or the drawing.
/// Tests: this class.
/// </summary>
public class MidiRecordLandingTests
{
    private static (SongDocument Song, TrackModel Track) Song()
    {
        var song = new SongDocument(() => null);
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true };
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    private static AudioClip Clip(double start, double length, params ClipNote[] notes) =>
        new() { StartSec = start, SourceLengthSec = length, Notes = notes.ToList() };

    [Fact]
    public void NewLaneIsTheDefaultAndKeepsTheClipUnderneathUntouched()
    {
        var (song, track) = Song();
        track.AudioClips.Add(Clip(0, 4, new ClipNote(0, 0.5, 60, 90)));
        MidiTakeLanding.Land(song.Project, track, new() { Clip(1, 1, new ClipNote(0, 0.2, 64, 100)) }, new MidiRecordOptions());
        Assert.Equal(2, track.AudioClips.Count);
        Assert.Single(track.AudioClips[0].Notes!);
        Assert.Equal(1, track.AudioClips[1].Lane);
    }

    [Fact]
    public void MergeAddsTheNotesIntoTheClipUnderneathAtTheRightOffset()
    {
        var (song, track) = Song();
        track.AudioClips.Add(Clip(2, 4, new ClipNote(0, 0.5, 60, 90)));
        MidiTakeLanding.Land(song.Project, track, new() { Clip(3, 1, new ClipNote(0.5, 0.2, 64, 100)) }, new MidiRecordOptions(Merge: true));
        var host = Assert.Single(track.AudioClips);
        Assert.Equal(new[] { 60, 64 }, host.Notes!.Select(n => n.Pitch));
        Assert.Equal(1.5, host.Notes![1].StartSec, 6);
    }

    [Fact]
    public void MergeWithNothingUnderneathFallsBackToALane()
    {
        var (song, track) = Song();
        MidiTakeLanding.Land(song.Project, track, new() { Clip(1, 1, new ClipNote(0, 0.2, 64, 100)) }, new MidiRecordOptions(Merge: true));
        Assert.Single(track.AudioClips);
    }

    [Fact]
    public void MergeIsOneUndoStep()
    {
        var (song, track) = Song();
        track.AudioClips.Add(Clip(0, 4, new ClipNote(0, 0.5, 60, 90)));
        var before = song.Undo.Snapshot(song.Project, "Record take");
        var result = DocumentEdits.Run(song, _ => { MidiTakeLanding.Land(song.Project, track, new() { Clip(1, 1, new ClipNote(0, 0.2, 64, 100)) }, new MidiRecordOptions(Merge: true)); return true; }, before, label: "Record take");
        Assert.Equal(2, track.AudioClips[0].Notes!.Count);
        Assert.True(result.Changed);
        Assert.True(song.Undo.CanUndo);
    }

    [Fact]
    public void QuantizeSnapsNoteStartsToTheGridAndOffKeepsRawTiming()
    {
        var (song, track) = Song();
        var quarter = AudioClipTimelineRules.SecondsAtBar(song.Project, 0, 0.25);
        var raw = Clip(0, 4, new ClipNote(quarter + 0.04, 0.2, 60, 100));
        MidiTakeLanding.Land(song.Project, track, new() { raw }, new MidiRecordOptions());
        Assert.Equal(quarter + 0.04, track.AudioClips[0].Notes![0].StartSec, 6);
        var (song2, track2) = Song();
        var takes = new System.Collections.Generic.List<AudioClip> { Clip(0, 4, new ClipNote(quarter + 0.04, 0.2, 60, 100)) };
        MidiTakeQuantize.Quantize(song2.Project, takes, "1/4");
        MidiTakeLanding.Land(song2.Project, track2, takes, new MidiRecordOptions(QuantizeGrid: "1/4"));
        Assert.Equal(quarter, track2.AudioClips[0].Notes![0].StartSec, 6);
        Assert.Equal(0.2, track2.AudioClips[0].Notes![0].LengthSec, 6);
    }

    [Fact]
    public void QuantizeUsesTheTripletGrid()
    {
        var (song, track) = Song();
        var third = AudioClipTimelineRules.SecondsAtBar(song.Project, 0, 1.0 / 12);   // 1/8T = 4/3 sixteenths
        var takes = new System.Collections.Generic.List<AudioClip> { Clip(0, 4, new ClipNote(third + 0.01, 0.1, 60, 100)) };
        MidiTakeQuantize.Quantize(song.Project, takes, "1/8T");
        MidiTakeLanding.Land(song.Project, track, takes, new MidiRecordOptions(QuantizeGrid: "1/8T"));
        Assert.Equal(third, track.AudioClips[0].Notes![0].StartSec, 6);
    }

    [Fact]
    public void ClosingTheWindowMidRecordKeepsTheTake()
    {
        var (song, track) = Song();
        var device = new Device();
        var recording = new TransportRecording(new AudioEngineClient(new TabForge.Plugins.PluginTrust()), new MidiInputHub(device).CreateClient());
        recording.Toggle(song.Project.Tracks, "unused");
        device.Play(0x90, 60, 100, Stopwatch.GetTimestamp());
        device.Play(0x80, 60, 0, Stopwatch.GetTimestamp());
        recording.Dispose();   // the window closed
        Assert.False(recording.IsRecording);
        MainWindowTransportRecord.LandMidiTakes(recording, song);
        Assert.Equal(60, Assert.Single(Assert.Single(track.AudioClips).Notes!).Pitch);
    }

    [Fact]
    public void LiveOverlayModelHoldsFinishedAndHeldNotesInBeats()
    {
        var (song, _) = Song();
        var map = PianoRollTimeMap.Performed(song.Project);
        var held = new Dictionary<int, (double Start, int Velocity)> { [64] = (1.0, 80) };
        var list = new List<LiveRollNote> { default };   // refilled, not appended
        PianoRollLiveNotes.Fill(list, map, 0.5, 2.0, new[] { new ClipNote(0.25, 0.25, 60, 100) }, held);
        Assert.Equal(2, list.Count);
        Assert.Equal(map.BeatsAt(0.75), list[0].StartBeat, 6);
        Assert.False(list[0].Held);
        Assert.True(list[1].Held);
        Assert.Equal(map.BeatsAt(2.0), list[1].EndBeat, 6);
        PianoRollLiveNotes.Fill(list, map, 0, 0, Array.Empty<ClipNote>(), new Dictionary<int, (double, int)>());
        Assert.Empty(list);
    }

    private sealed class Device : IMidiInputSource
    {
        public event Action<int, int, int, long>? Message;
        public bool IsOpen { get; private set; }
        public int Open() { IsOpen = true; return 1; }
        public void Close() => IsOpen = false;
        public void Play(int status, int pitch, int velocity, long stamp) => Message?.Invoke(status, pitch, velocity, stamp);
        public void Dispose() => Close();
    }
}
