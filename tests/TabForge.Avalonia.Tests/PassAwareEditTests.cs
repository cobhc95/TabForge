using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: S5/S5b on performed time: split at the playhead's pass, record from that pass, the performed loop span, and the overlap mute.
// Does not own: the pass mapping (RepeatMapTests), the pass-aware seek (PassAwareSeekTests) or the fold (RepeatViewTests).
// Tests: this class.
public sealed class PassAwareEditTests
{
    /// <summary>Bars 0-11 at 120 bpm (2 s each); bars 4-7 repeat twice (written bars 5-8 in the spec).</summary>
    private static SongDocument Song(out TrackModel track)
    {
        var project = new SongProject { Tempo = 120 };
        track = new TrackModel { Name = "Guitar" };
        for (var bar = 0; bar < 12; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        track.Measures[4].RepeatStart = true;
        track.Measures[7].RepeatEnd = true;
        project.Tracks.Add(track);
        return new SongDocument(() => null) { Project = project };
    }

    private static AudioClip Clip(double startSec, double endSec, int lane = 0) => new()
    {
        File = "take.wav", Name = "take", StartSec = startSec, SourceLengthSec = endSec - startSec, FileLengthSec = 60, Lane = lane,
    };

    [AvaloniaFact]
    public void SplitAtPlayheadPass2CutsTheClipAt19Seconds()
    {
        var song = Song(out var track);
        var clip = Clip(2, 30);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project);
        view.SetPlayhead(5, 0.5, 2);   // bar 6 middle, second play
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var target = new MainWindowClipKeys.Target(song, view, view.SelectedClip!);

        MainWindowClipProperties.Split(target, _ => throw new InvalidOperationException("the split should be inside the clip"));

        var parts = track.AudioClips.OrderBy(part => part.StartSec).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(19.0, parts[0].EndSec, 6);
        Assert.Equal(19.0, parts[1].StartSec, 6);
        Assert.Equal(30.0, parts[1].EndSec, 6);
    }

    [AvaloniaFact]
    public void SplitAtPlayheadPass1KeepsTheOldElevenSecondCut()
    {
        var song = Song(out var track);
        var clip = Clip(2, 30);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project);
        view.SetPlayhead(5, 0.5, 1);
        view.SelectedClip = new TimelineClipSelection(track, clip);
        var target = new MainWindowClipKeys.Target(song, view, view.SelectedClip!);

        MainWindowClipProperties.Split(target, _ => throw new InvalidOperationException("the split should be inside the clip"));

        var parts = track.AudioClips.OrderBy(part => part.StartSec).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(11.0, parts[0].EndSec, 6);
        Assert.Equal(11.0, parts[1].StartSec, 6);
    }

    [AvaloniaFact]
    public void SplitUsesTheLaneCursorsPassAwarePositionWhenInsideTheClip()
    {
        var song = Song(out var track);
        var clip = Clip(2, 30);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project);
        view.SetPlayhead(5, 0.5, 1);   // the old first-play second, so the cursor must win
        view.SelectedClip = new TimelineClipSelection(track, clip);
        view.LaneCursor = new TimelineLaneCursor(track, clip.Lane, 11, new RepeatPosition(5, 0.5, 2));
        var target = new MainWindowClipKeys.Target(song, view, view.SelectedClip!);

        MainWindowClipProperties.Split(target, _ => throw new InvalidOperationException("the split should be inside the clip"));

        var parts = track.AudioClips.OrderBy(part => part.StartSec).ToList();
        Assert.Equal(19.0, parts[0].EndSec, 6);
        Assert.Equal(19.0, parts[1].StartSec, 6);
        Assert.Null(view.LaneCursor!.Value.Position);
        Assert.Equal(19.0, view.LaneCursor!.Value.Sec, 6);
    }

    [Fact]
    public void RecordFromPass2StartsAtTheSameNineteenSeconds()
    {
        var song = Song(out _);
        var position = new RepeatPosition(5, 0.5, 2);
        var midCell = Math.Max(1, MusicTime.BarSlots(song.Project, 5)) / 2;

        Assert.Equal(19.0, RecordingPresenter.TakeStartSec(song.Project, position, 5, midCell), 6);
        // Pass 1 / no position is the unchanged first occurrence.
        Assert.Equal(11.0, RecordingPresenter.TakeStartSec(song.Project, null, 5, midCell), 6);

        var performed = SongExtent.Timing(song.Project).Performed;
        Assert.Equal(RepeatMap.ToPerformed(performed, position), RecordingPresenter.TakeStartSec(song.Project, position, 5, midCell), 6);
    }

    [AvaloniaFact]
    public void SplitAfterClipMoveWithNoNewPressUsesPlayheadPass()
    {
        var song = Song(out var track);
        var clip = Clip(2, 30);
        track.AudioClips.Add(clip);
        var view = new TimelineView(song.Project);
        view.SetPlayhead(5, 0.5, 1);   // 11 s, first play
        view.SelectedClip = new TimelineClipSelection(track, clip);

        var gestures = new TimelineClipGestureController(view, (_, _, _) => new RepeatPosition(5, 0.5, 2));
        var pressX = view.XOfSeconds(5);
        var pressY = view.LaneBandTopOf(0) + 4;
        gestures.Press(true, new global::Avalonia.Point(pressX, pressY));
        Assert.True(gestures.Begin());
        // A stale pass-2 click with no new press afterwards.
        view.LaneCursor = new TimelineLaneCursor(track, clip.Lane, 11, new RepeatPosition(5, 0.5, 2));
        gestures.Update(new global::Avalonia.Point(pressX + 10, pressY), altHeld: true);
        Assert.True(gestures.Commit(song));

        Assert.Null(view.LaneCursor!.Value.Position);

        view.SelectedClip = new TimelineClipSelection(track, track.AudioClips[0]);
        var target = new MainWindowClipKeys.Target(song, view, view.SelectedClip!);
        MainWindowClipProperties.Split(target, _ => throw new InvalidOperationException("the split should be inside the clip"));

        var parts = track.AudioClips.OrderBy(part => part.StartSec).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(11.0, parts[0].EndSec, 6);
        Assert.Equal(11.0, parts[1].StartSec, 6);
    }

    [Fact]
    public void RepeatViewStateClampAfterNShrinksKeeps2AndClamps5To3()
    {
        var state = new RepeatViewState();
        var region7a = new RepeatRegion(5, 8, 7);
        var region7b = new RepeatRegion(20, 21, 7);
        state.SetPass(region7a, 2);
        state.SetPass(region7b, 5);
        Assert.Equal(2, state.ShownPass(region7a));
        Assert.Equal(5, state.ShownPass(region7b));

        var region3a = new RepeatRegion(5, 8, 3);
        var region3b = new RepeatRegion(20, 21, 3);
        state.Prune(new[] { region3a, region3b });

        Assert.Equal(2, state.ShownPass(region3a));
        Assert.Equal(3, state.ShownPass(region3b));

        state.Prune(new[] { region3a });
        Assert.Equal(1, state.ShownPass(region3b));
    }

    [Fact]
    public void MuteCoveredMutesOnlyAFullyCoveredOlderClip()
    {
        var track = new TrackModel();
        var old = Clip(2, 30);
        var partial = Clip(19, 24);
        track.AudioClips.Add(old);
        track.AudioClips.Add(partial);

        Assert.False(TakeOverlap.MuteCovered(track, partial));
        Assert.False(old.Muted);

        var full = Clip(2, 30, lane: 1);
        track.AudioClips.Add(full);

        Assert.True(TakeOverlap.MuteCovered(track, full));
        Assert.True(old.Muted);
        Assert.False(full.Muted);
    }

    [AvaloniaFact]
    public void LandingAPartiallyCoveringTakeLeavesTheOlderClipUnmuted()
    {
        var song = Song(out var track);
        var old = Clip(2, 30);
        track.AudioClips.Add(old);

        MainWindowTransportRecord.LandTake(new[] { song }, track, "new.wav", 19, 5);

        Assert.False(old.Muted);   // no partial mute exists: the older clip keeps its own mute state
        var landed = Assert.Single(track.AudioClips, clip => !ReferenceEquals(clip, old));
        Assert.Equal(19.0, landed.StartSec, 6);
        Assert.Equal(1, landed.Lane);
    }

    [AvaloniaFact]
    public void LandingATakeOverAnOldClipMutesItInTheSameUndoStep()
    {
        var song = Song(out var track);
        var old = Clip(2, 30);
        track.AudioClips.Add(old);

        MainWindowTransportRecord.LandTake(new[] { song }, track, "new.wav", 2, 28);

        var landed = track.AudioClips.Where(clip => !ReferenceEquals(clip, old)).ToList();
        Assert.Single(landed);
        Assert.True(old.Muted);
        Assert.False(landed[0].Muted);

        Assert.NotNull(DocumentEdits.Undo(song));
        var restored = Assert.Single(song.Project.Tracks.SelectMany(candidate => candidate.AudioClips));
        Assert.Equal(2.0, restored.StartSec, 6);
        Assert.Equal(30.0, restored.EndSec, 6);
        Assert.False(restored.Muted);
    }
}
