using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the view-free audio-drop planner and applier: the Add track strip plans a new audio track, an existing track plans a new lane, both at
/// the exact drop song time; unsupported extensions and guarded paths are refused; the applier is one undo step. Lengths come from a fake
/// measurer (the real import uses WaveformCache.LengthOf).
/// Does not own: the drag data, the pointer hit test, the drag affordance or the decoder (MainWindowAudioDrop, AddTrackStrip, WaveformCache).
/// Tests: this class.
/// </summary>
public sealed class AudioDropPlannerTests
{
    private static readonly Func<string, double> ThreeSeconds = _ => 3.0;

    private static AudioClip ExistingClip(string name = "old take") => new()
    {
        File = @"C:\media\old.wav", Name = name, StartSec = 0, SourceLengthSec = 2, FileLengthSec = 2,
    };

    private static TrackModel AudioTrackWithClip(SongDocument song)
    {
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.AudioClips.Add(ExistingClip());
        song.Project.Tracks.Add(track);
        return track;
    }

    [Fact]
    public void AudioFileOnTheAddTrackStripPlansANewAudioTrackAtTheDropTime()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var files = new[] { @"C:\drops\take one.wav" };

        var plan = AudioDropPlanner.Plan(files, AudioDropTarget.NewTrack, 37.125, _ => 3.5);

        Assert.True(plan.Accepted);
        Assert.True(plan.NewTrack);
        Assert.Equal(37.125, plan.StartSec, 6);
        Assert.Equal(0, plan.Lane);
        var item = Assert.Single(plan.Items);
        Assert.Equal(37.125, item.StartSec, 6);
        Assert.Equal(3.5, item.LengthSec, 6);
        Assert.Equal("take one", item.Name);
        Assert.Equal("take one", plan.TrackName);
        Assert.Equal(40.625, plan.EndSec, 6);
    }

    [Fact]
    public void AudioFileOnAnExistingTrackPlansANewLaneAtTheDropTime()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = AudioTrackWithClip(song);

        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\new.wav" }, AudioDropTarget.On(track), 12.5, ThreeSeconds);

        Assert.True(plan.Accepted);
        Assert.False(plan.NewTrack);
        Assert.Same(track, plan.Track);
        Assert.Equal(ClipLanes.Count(track), plan.Lane);   // a new lane after the one already used
        Assert.Equal(1, plan.Lane);
        Assert.Equal(12.5, plan.StartSec, 6);
    }

    [Fact]
    public void EmptyTimelineSpaceIsRefusedWithTheDropTargetNotice()
    {
        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\a.wav" }, AudioDropTarget.None, 3, ThreeSeconds);

        Assert.False(plan.Accepted);
        Assert.Equal(AudioDropTargetKind.None, plan.Kind);
        Assert.Equal(AudioDropPlanner.NoTargetText, plan.Refusal);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void UnsupportedExtensionIsRefusedAndSaysWhy()
    {
        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\notes.txt" }, AudioDropTarget.NewTrack, 2, ThreeSeconds);

        Assert.False(plan.Accepted);
        Assert.Empty(plan.Items);
        Assert.Contains(AudioDropPlanner.NotAudioText, plan.Refusal);
        Assert.Contains("notes.txt", plan.Skipped!.Single());
    }

    [Fact]
    public void DeviceAndReparsePathsAreRefused()
    {
        var device = AudioDropPlanner.Plan(new[] { @"\\.\C:\drops\a.wav" }, AudioDropTarget.NewTrack, 0, ThreeSeconds, FilePathBoundary.RefusalReason);
        Assert.False(device.Accepted);
        Assert.Contains("device", device.Refusal);

        static string? Linked(string file) => file.EndsWith("linked.wav", StringComparison.OrdinalIgnoreCase) ? "a linked file is not allowed" : null;
        var linked = AudioDropPlanner.Plan(new[] { @"C:\drops\linked.wav" }, AudioDropTarget.NewTrack, 0, ThreeSeconds, Linked);
        Assert.False(linked.Accepted);
        Assert.Contains("linked", linked.Refusal);
        Assert.Contains("linked.wav", linked.Skipped!.Single());
    }

    [AvaloniaFact]
    public void TheDropTimeIsExactAtAnArbitraryMidSongSecond()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new TimelineView(song.Project);
        var x = view.XOfSeconds(37.125);
        var mapped = view.SecondsOfX(x);

        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\a.wav" }, AudioDropTarget.NewTrack, mapped, ThreeSeconds);

        Assert.Equal(37.125, mapped, 3);
        Assert.Equal(37.125, plan.StartSec, 3);
        Assert.Equal(37.125, plan.Items[0].StartSec, 3);
    }

    [AvaloniaFact]
    public void ApplyingANewTrackDropIsOneUndoStep()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var beforeTracks = song.Project.Tracks.Count;
        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\take.wav" }, AudioDropTarget.NewTrack, 4.5, _ => 2.25);

        var result = AudioDropApplier.Apply(song, plan);

        Assert.NotNull(result);
        Assert.Equal(beforeTracks + 1, song.Project.Tracks.Count);
        Assert.Same(result!.Track, song.Project.Tracks[^1]);
        Assert.Equal("take", result.Track.Name);
        Assert.Single(result.Track.AudioClips);
        Assert.Equal(4.5, result.Track.AudioClips[0].StartSec, 6);
        Assert.Equal(2.25, result.Track.AudioClips[0].SourceLengthSec, 6);
        Assert.Contains("Added", result.Status);

        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(beforeTracks, song.Project.Tracks.Count);
        Assert.DoesNotContain(song.Project.Tracks, track => track.Name == "take");
    }

    [AvaloniaFact]
    public void ApplyingAMultiFileDropIsExactlyOneUndoStep()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var beforeTracks = song.Project.Tracks.Count;
        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\a.wav", @"C:\drops\b.wav" }, AudioDropTarget.NewTrack, 1.0, _ => 2.0);

        Assert.NotNull(AudioDropApplier.Apply(song, plan));
        Assert.True(song.Undo.CanUndo);

        Assert.NotNull(DocumentEdits.Undo(song));

        Assert.False(song.Undo.CanUndo);
        Assert.Equal(beforeTracks, song.Project.Tracks.Count);
    }

    [AvaloniaFact]
    public void ApplyingAnExistingTrackDropLeavesTheTrackAndAddsOneNewLaneClip()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = AudioTrackWithClip(song);
        var lanesBefore = ClipLanes.Count(track);
        var clipsBefore = track.AudioClips.Count;
        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\new.wav" }, AudioDropTarget.On(track), 6.0, _ => 1.5);

        var result = AudioDropApplier.Apply(song, plan);

        Assert.NotNull(result);
        Assert.Same(track, result!.Track);
        Assert.Equal(clipsBefore + 1, track.AudioClips.Count);
        Assert.True(ClipLanes.Count(track) > lanesBefore);
        Assert.Equal(plan.Lane, result.LastClip.Lane);
        Assert.Equal(6.0, result.LastClip.StartSec, 6);

        Assert.NotNull(DocumentEdits.Undo(song));
        var restored = song.Project.Tracks.Single(candidate => candidate.Name == track.Name);
        Assert.Equal(clipsBefore, restored.AudioClips.Count);
        Assert.Equal(lanesBefore, ClipLanes.Count(restored));
    }

    private static AudioClip MidiClip(string name) => new()
    {
        Name = name, Notes = new List<ClipNote> { new(0, 0.5, 60, 100) }, SourceLengthSec = 0.5, FileLengthSec = 0.5,
    };

    [Fact]
    public void MidiFileOverATrackRowLandsAsAMidiClipAtTheDropTimeInOneUndoStep()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = AudioTrackWithClip(song);
        var files = new[] { @"C:\drops\riff.mid" };

        var plan = AudioDropPlanner.Plan(files, AudioDropTarget.On(track), 4.25, _ => 0, midiOf: _ => MidiClip("x"));
        var result = AudioDropApplier.Apply(song, plan);

        Assert.NotNull(result);
        var clip = track.AudioClips.Last();
        Assert.True(clip.IsMidi);
        Assert.Equal((4.25, "riff", 1), (clip.StartSec, clip.Name, clip.Lane));
        Assert.True(song.Undo.CanUndo);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Single(song.Project.Tracks[^1].AudioClips);
    }

    [Fact]
    public void MidiFileOverTheStripMakesANewTrackWithAMidiClip()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var before = song.Project.Tracks.Count;

        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\riff.mid", @"C:\drops\a.wav" }, AudioDropTarget.NewTrack, 2, _ => 3, midiOf: _ => MidiClip("x"));
        var result = AudioDropApplier.Apply(song, plan);

        Assert.NotNull(result);
        Assert.Equal(before + 1, song.Project.Tracks.Count);
        Assert.Equal(new[] { true, false }, result!.Track.AudioClips.Select(c => c.IsMidi).ToArray());
        Assert.Equal(2.5, result.Track.AudioClips[1].StartSec, 6);
    }

    [Fact]
    public void MidiFileOverTheScoreOpensAsASongAndOverTheTimelineItDoesNot()
    {
        var midi = new[] { @"C:\drops\riff.mid" };
        Assert.False(MainWindowAudioDrop.HasSongs(midi));
        Assert.True(MainWindowAudioDrop.Lands(midi[0]));
        Assert.True(MainWindowAudioDrop.YieldsToWindow(midi, onRowsOrStrip: false));
        Assert.False(MainWindowAudioDrop.YieldsToWindow(midi, onRowsOrStrip: true));
        Assert.True(MainWindowAudioDrop.HasSongs(new[] { @"C:\drops\song.gp5" }));
        Assert.False(MainWindowAudioDrop.YieldsToWindow(new[] { @"C:\drops\a.wav" }, onRowsOrStrip: false));
        Assert.Equal(MainWindowFileDrop.WindowDropKind.Songs, MainWindowFileDrop.Decide(MainWindowFileDrop.SongPaths(midi), false));
    }
}
