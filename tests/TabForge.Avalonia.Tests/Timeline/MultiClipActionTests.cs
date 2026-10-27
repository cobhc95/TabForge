using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the clip actions with several clips selected: Delete, Cut, Copy+Paste (relative places kept), Duplicate, nudge, mute and the drag that moves them all, each as one undo step.
// Does not own: the single-clip rules (MainWindowClipKeysTests, ClipEditingBatchFourTests) or the selection gestures (TimelineBoxSelectionTests).
// Tests: this class.
public class MultiClipActionTests
{
    // One audio track with three one-second clips at 1 s, 3 s and 5 s.
    private static (SongDocument Song, TrackModel Track, TimelineView View, MainWindowClipKeys.Target Target) SetUp(int selected = 3)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        foreach (var start in new[] { 1.0, 3.0, 5.0 })
            track.AudioClips.Add(new AudioClip { File = "a.wav", Name = "c" + start, StartSec = start, SourceLengthSec = 1, FileLengthSec = 1 });
        song.MarkClean();
        var view = new TimelineView(song.Project) { FocusModel = song.Selection };
        view.SelectClips(track.AudioClips.Take(selected).Select(clip => new TimelineClipSelection(track, clip)));
        return (song, track, view, new MainWindowClipKeys.Target(song, view, view.SelectedClips[0], view.SelectedClips.ToList()));
    }

    private static double[] Starts(TrackModel track) => track.AudioClips.Select(clip => Math.Round(clip.StartSec, 3)).OrderBy(s => s).ToArray();

    [AvaloniaFact]
    public void DeleteRemovesEverySelectedClipAsOneUndoStep()
    {
        var (song, track, _, target) = SetUp();
        MainWindowClipKeys.Delete(target, () => false);
        Assert.Empty(track.AudioClips);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(new[] { 1.0, 3.0, 5.0 }, Starts(song.Project.Tracks.Last()));
    }

    [AvaloniaFact]
    public void CutCopiesAllSelectedClipsAndRemovesThem()
    {
        var (song, track, _, target) = SetUp(2);
        MainWindowClipKeys.Cut(target, () => false);
        Assert.Equal(new[] { 5.0 }, Starts(track));
        Assert.True(ClipClipboard.IsMany);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(3, song.Project.Tracks.Last().AudioClips.Count);
    }

    [AvaloniaFact]
    public void PasteKeepsTheRelativePlacesAndIsOneUndoStep()
    {
        var (song, track, view, target) = SetUp(2);
        MainWindowClipKeys.Copy(target);
        MainWindowClipKeys.Paste(target);   // after the last selected clip end (4 s): the copies keep their 2 s distance
        Assert.Equal(new[] { 1.0, 3.0, 4.0, 5.0, 6.0 }, Starts(track));
        Assert.Equal(2, view.SelectedClips.Count);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(3, song.Project.Tracks.Last().AudioClips.Count);
    }

    [AvaloniaFact]
    public void DuplicateCopiesEverySelectedClipAfterItselfInOneStep()
    {
        var (song, track, view, target) = SetUp();
        MainWindowClipKeys.Duplicate(target);
        Assert.Equal(6, track.AudioClips.Count);
        Assert.Equal(3, view.SelectedClips.Count);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(3, song.Project.Tracks.Last().AudioClips.Count);
    }

    [AvaloniaFact]
    public void NudgeMovesEverySelectedClipByTheSameAmountInOneStep()
    {
        var (song, track, _, target) = SetUp();
        MainWindowClipKeys.Nudge(target, 0.5, () => false);
        Assert.Equal(new[] { 1.5, 3.5, 5.5 }, Starts(track));
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(new[] { 1.0, 3.0, 5.0 }, Starts(song.Project.Tracks.Last()));
    }

    [AvaloniaFact]
    public void MuteMutesAllSelectedClipsThenUnmutesThemAllEachOneStep()
    {
        var (song, track, _, target) = SetUp();
        track.AudioClips[0].Muted = true;   // a mixed selection mutes the rest first
        MainWindowClipKeys.Edit(target, () => MainWindowClipEditKeys.ToggleMute(target));
        Assert.All(track.AudioClips, clip => Assert.True(clip.Muted));
        MainWindowClipKeys.Edit(target, () => MainWindowClipEditKeys.ToggleMute(target));
        Assert.All(track.AudioClips, clip => Assert.False(clip.Muted));
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.All(song.Project.Tracks.Last().AudioClips, clip => Assert.True(clip.Muted));
    }

    [AvaloniaFact]
    public void DraggingOneSelectedClipMovesAllSelectedTogetherAsOneUndoStep()
    {
        var (song, track, view, _) = SetUp();
        var gestures = new TimelineClipGestureController(view);
        var box = TimelineAudioClips.ClipBounds(view, track, track.AudioClips[1]);
        var middle = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        gestures.Press(true, middle);
        Assert.True(gestures.Begin());
        gestures.Update(new Point(middle.X + view.XOfSeconds(1.5) - view.XOfSeconds(0.5), middle.Y), altHeld: true);
        Assert.True(gestures.Commit(song));
        var moved = track.AudioClips.Select(clip => clip.StartSec).Zip(new[] { 1.0, 3.0, 5.0 }, (now, was) => now - was).ToArray();
        Assert.True(moved[0] > 0.1);
        Assert.All(moved, delta => Assert.Equal(moved[0], delta, 3));
        Assert.Equal(3, view.SelectedClips.Count);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(new[] { 1.0, 3.0, 5.0 }, Starts(song.Project.Tracks.Last()));
    }
}
