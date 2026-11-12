using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: what undo and redo give back beside the song (the side state DocumentEdits keeps per undo state): the tuning shift, the skipped areas,
//   the selection shown with each state, the clean mark when undoing back to the saved song, and Restored raised once per step after all of it.
// Does not own: the undo stacks and their grouping (UndoRulesTests) or the window's after-restore pipeline (AfterRestorePipelineTests).
// Tests: DocumentEdits, SongDocument (IUndoMemory).
public sealed class UndoSideStateCharacterizationTests
{
    private static SongDocument NewSong()
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = Enumerable.Range(0, 8).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() });
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        return song;
    }

    [Fact]
    public void TheTuningShiftComesBackOnUndoAndRedo()
    {
        var song = NewSong();
        Assert.True(DocumentEdits.Run(song, p => { song.TuningShift[0] = 2; p.Tempo = 130; return true; }).Changed);
        Assert.Equal(2, song.TuningShift[0]);
        DocumentEdits.Undo(song);
        Assert.Equal(0, song.TuningShift[0]);
        DocumentEdits.Redo(song);
        Assert.Equal(2, song.TuningShift[0]);
    }

    [Fact]
    public void SkippedAreasComeBackOnUndoAndRedo()
    {
        var song = NewSong();
        DocumentEdits.Run(song, p => { song.SkipRanges.Add((2, 3)); p.Tempo = 130; return true; });
        Assert.Equal(new[] { (2, 3) }, song.SkipRanges.ToArray());
        DocumentEdits.Undo(song);
        Assert.Empty(song.SkipRanges);
        DocumentEdits.Redo(song);
        Assert.Equal(new[] { (2, 3) }, song.SkipRanges.ToArray());
    }

    [Fact]
    public void AnEditThatDoesNotMoveTheSkippedAreasLeavesThemOnUndo()
    {
        var song = NewSong();
        song.SkipRanges.Add((4, 5));
        DocumentEdits.Run(song, p => { p.Tempo = 130; return true; });
        DocumentEdits.Undo(song);
        Assert.Equal(new[] { (4, 5) }, song.SkipRanges.ToArray());
    }

    [Fact]
    public void TheSelectionShownIsTheOneKeptWithEachState()
    {
        var song = NewSong();
        song.Selection.SetRange(0, 1, 2, SelectionOrigin.Timeline);
        DocumentEdits.Run(song, p => { p.Tempo = 130; return true; });
        song.Selection.SetRange(0, 5, 6, SelectionOrigin.Timeline);
        DocumentEdits.Undo(song);
        Assert.Equal((1, 2), song.Selection.BarRange);
        DocumentEdits.Redo(song);
        Assert.Equal((5, 6), song.Selection.BarRange);
    }

    [Fact]
    public void UndoingBackToTheSavedSongClearsTheDirtyMark()
    {
        var song = NewSong();
        DocumentEdits.Run(song, p => { p.Tempo = 130; return true; });
        Assert.True(song.Project.IsDirty);
        DocumentEdits.Undo(song);
        Assert.False(song.Project.IsDirty);
        DocumentEdits.Redo(song);
        Assert.True(song.Project.IsDirty);
    }

    [Fact]
    public void RestoredFiresOnceAfterTheSideStateIsBack()
    {
        var song = NewSong();
        DocumentEdits.Run(song, p => { song.TuningShift[1] = -1; song.SkipRanges.Add((1, 1)); p.Tempo = 140; return true; });
        var seen = new List<(int Shift, int Skips, int Tempo)>();
        song.Restored += () => seen.Add((song.TuningShift[1], song.SkipRanges.Count, song.Project.Tempo));
        DocumentEdits.Undo(song);
        Assert.Equal(new[] { (0, 0, 120) }, seen);
        DocumentEdits.Redo(song);
        Assert.Equal(new[] { (0, 0, 120), (-1, 1, 140) }, seen);
    }
}
