using System;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the SelectionModel rules of the selection spec: S6 track switch, S7 Escape order, the focus target and S9 (the selection stored with undo steps).
// Does not own: the views that mirror the model (SelectionLoopLinkTests).
// Tests: SelectionModel.
public sealed class SelectionModelTests
{
    [Fact]
    public void TrackSwitchClearsThisTrackRange()
    {
        // E1: select bars 3-4 in the score on track 1, switch to track 2: the selection is cleared, not moved.
        var model = new SelectionModel();
        model.SetRange(1, 3, 4, SelectionOrigin.Editor);
        Assert.Equal(SelectionScope.ThisTrack, model.Scope);
        model.SetTrack(2);
        Assert.False(model.HasRange);
        Assert.Equal(2, model.TrackIndex);
    }

    [Fact]
    public void TrackSwitchKeepsAllTracksRange()
    {
        var model = new SelectionModel();
        model.SetRange(1, 3, 4, SelectionOrigin.Timeline, 2, 5);
        model.SetTrack(2);
        Assert.Equal((3, 4), model.BarRange);
        Assert.Equal(SelectionScope.AllTracks, model.Scope);
        Assert.Equal((0, -1), (model.StartCell, model.EndCell));
    }

    [Fact]
    public void FocusIsSetAndReportsChange()
    {
        var model = new SelectionModel();
        Assert.Equal(FocusTarget.None, model.Focus);
        Assert.True(model.SetFocus(FocusTarget.TrackRow));
        Assert.False(model.SetFocus(FocusTarget.TrackRow));
        Assert.Equal(FocusTarget.TrackRow, model.Focus);
    }

    [Fact]
    public void EscapeClearsClipsThenRangeThenScoreThenRows()
    {
        var model = new SelectionModel();
        var song = Song();
        model.SetClips(new[] { new TimelineClipSelection(song.Tracks[0], song.Tracks[0].AudioClips[0]) });
        model.SetRange(0, 1, 2, SelectionOrigin.Timeline);
        model.SetRows(new[] { song.Tracks[0].Id });
        model.SetFocus(FocusTarget.Clips);
        Assert.True(model.ClearNext());
        Assert.Equal(0, model.ClipCount);
        Assert.True(model.HasRange);
        Assert.Equal(FocusTarget.None, model.Focus);
        Assert.True(model.ClearNext());
        Assert.False(model.HasRange);
        Assert.Equal(1, model.TrackRowCount);
        // A score selection is cleared by the same step, before the rows.
        model.SetRange(0, 1, 2, SelectionOrigin.Editor);
        Assert.True(model.ClearNext());
        Assert.False(model.HasRange);
        Assert.Equal(1, model.TrackRowCount);
        Assert.True(model.ClearNext());
        Assert.Equal(0, model.TrackRowCount);
        Assert.False(model.ClearNext());
    }

    private static SongProject Song()
    {
        var song = DemoSongFactory.Create();
        var track = song.Tracks[0];
        track.AudioClips.Add(new AudioClip { File = "a.wav", StartSec = 0, SourceLengthSec = 1 });
        return song;
    }

    [Fact]
    public void ClipsRowsAndFocusRaiseChangedOnlyWhenTheyDiffer()
    {
        var song = Song();
        var model = new SelectionModel();
        var clip = new TimelineClipSelection(song.Tracks[0], song.Tracks[0].AudioClips[0]);
        model.SetClips(new[] { clip });
        model.SetRows(new[] { song.Tracks[0].Id });
        model.SetFocus(FocusTarget.Clips);
        var count = model.ChangeCount;
        model.SetClips(new[] { clip });
        model.SetRows(new[] { song.Tracks[0].Id });
        model.SetFocus(FocusTarget.Clips);
        Assert.Equal(count, model.ChangeCount);
        model.SetClips(Array.Empty<TimelineClipSelection>());
        model.SetRows(Array.Empty<Guid>());
        model.SetFocus(FocusTarget.None);
        Assert.Equal(count + 3, model.ChangeCount);
    }

    [Fact]
    public void RestoreOfTheSameRangeWithADifferentClipStillNotifies()
    {
        var song = Song();
        var model = new SelectionModel();
        model.SetRange(0, 2, 4, SelectionOrigin.Timeline);
        var withClip = model.Snapshot();
        model.SetClips(new[] { new TimelineClipSelection(song.Tracks[0], song.Tracks[0].AudioClips[0]) });
        var snapshot = model.Snapshot();
        model.SetClips(Array.Empty<TimelineClipSelection>());
        var count = model.ChangeCount;

        model.Restore(snapshot, song, 8);

        Assert.Single(model.Clips);
        Assert.Equal(count + 1, model.ChangeCount);
        _ = withClip;
    }

    [Fact]
    public void RestoreOfAnIdenticalSelectionDoesNotNotify()
    {
        var song = Song();
        var model = new SelectionModel();
        model.SetRange(0, 2, 4, SelectionOrigin.Timeline);
        var snapshot = model.Snapshot();
        var count = model.ChangeCount;
        model.Restore(snapshot, song, 8);
        Assert.Equal(count, model.ChangeCount);
    }

    [Fact]
    public void RestoreDropsClipsRowsAndBarsTheSongNoLongerHas()
    {
        var song = Song();
        var model = new SelectionModel();
        var clip = new TimelineClipSelection(song.Tracks[0], song.Tracks[0].AudioClips[0]);
        model.SetClips(new[] { clip });
        model.SetRows(new[] { song.Tracks[0].Id, Guid.NewGuid() });
        model.SetRange(0, 2, 40, SelectionOrigin.Timeline);
        var snapshot = model.Snapshot();

        song.Tracks[0].AudioClips.Clear();
        model.Restore(snapshot, song, 8);

        Assert.Empty(model.Clips);
        Assert.Equal(new[] { song.Tracks[0].Id }, model.Rows);
        Assert.Equal((2, 7), model.BarRange);
    }

    [Fact]
    public void RebindFindsTheSelectedClipInTheReplacedSong()
    {
        var song = Song();
        var model = new SelectionModel();
        model.SetClips(new[] { new TimelineClipSelection(song.Tracks[0], song.Tracks[0].AudioClips[0]) });
        var replaced = ProjectService.CloneProject(song);

        model.Rebind(replaced);

        Assert.Same(replaced.Tracks[0].AudioClips[0], Assert.Single(model.Clips).Clip);
    }

    [Fact]
    public void UndoRestoresTheSelectionBeforeTheEditAndRedoTheOneAfterIt()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        document.Selection.SetRange(0, 1, 2, SelectionOrigin.Editor);
        DocumentEdits.Run(document, project => { project.Title = "changed"; return true; });
        document.Selection.SetRange(0, 3, 4, SelectionOrigin.Timeline);

        DocumentEdits.Undo(document);
        Assert.Equal((1, 2), document.Selection.BarRange);
        Assert.Equal(SelectionScope.ThisTrack, document.Selection.Scope);

        DocumentEdits.Redo(document);
        Assert.Equal((3, 4), document.Selection.BarRange);
        Assert.Equal(SelectionScope.AllTracks, document.Selection.Scope);
    }

    [Fact]
    public void EachDocumentHasItsOwnSelection()
    {
        var first = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var second = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        first.Selection.SetRange(0, 1, 2, SelectionOrigin.Timeline);
        Assert.False(second.Selection.HasRange);
        Assert.NotSame(first.Selection, second.Selection);
    }
}
