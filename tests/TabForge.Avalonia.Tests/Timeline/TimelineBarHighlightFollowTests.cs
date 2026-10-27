using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the highlight following an edit: a track delete or reorder (tracks kept by Id), a bar insert or delete (through the edit's bar map), and the Paste row reading the real clipboard.
// Does not own: the highlight rules (TimelineBarHighlightModelTests) or the gestures (TimelineBarHighlightInputTests).
// Tests: this class.
public sealed class TimelineBarHighlightFollowTests
{
    private static SongDocument NewSong()
    {
        var project = new SongProject();
        for (var t = 0; t < 3; t++) project.Tracks.Add(new TrackModel { Name = "T" + t, Measures = Enumerable.Range(0, 8).Select(bar => new MeasureModel { Number = bar + 1 }).ToList() });
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        return document;
    }

    [Fact]
    public void DeletingATrackAboveMovesTheHighlightWithItsTrack()
    {
        var song = NewSong();
        song.Highlight.Set(2, 3);
        DocumentEdits.Run(song, p => { p.Tracks.RemoveAt(0); return true; });
        Assert.Equal((1, 3), (song.Highlight.FirstTrack, song.Highlight.FirstBar));
    }

    [Fact]
    public void DeletingTheHighlightedTrackClearsIt()
    {
        var song = NewSong();
        song.Highlight.Set(1, 3);
        DocumentEdits.Run(song, p => { p.Tracks.RemoveAt(1); return true; });
        Assert.False(song.Highlight.Has);
    }

    [Fact]
    public void ReorderingTracksKeepsTheHighlightOnItsTrack()
    {
        var song = NewSong();
        song.Highlight.Set(0, 2);
        DocumentEdits.Run(song, p => { (p.Tracks[0], p.Tracks[2]) = (p.Tracks[2], p.Tracks[0]); return true; });
        Assert.Equal(2, song.Highlight.FirstTrack);
    }

    [Fact]
    public void InsertingBarsBeforeShiftsTheHighlightRight()
    {
        var song = NewSong();
        song.Highlight.SetBlock(1, 3, 1, 4);
        DocumentEdits.Run<int[]>(song, p => BarRangeEditor.Insert(p, 0, BarRangeEditor.Capture(p, 0, 1)));
        Assert.Equal((5, 6), (song.Highlight.FirstBar, song.Highlight.LastBar));
    }

    [Fact]
    public void DeletingSomeHighlightedBarsShrinksItAndDeletingAllClearsIt()
    {
        var song = NewSong();
        song.Highlight.SetBlock(1, 2, 1, 4);
        DocumentEdits.Run<int[]>(song, p => BarRangeEditor.Remove(p, 3, 6));
        Assert.Equal((2, 2), (song.Highlight.FirstBar, song.Highlight.LastBar));
        DocumentEdits.Run<int[]>(song, p => BarRangeEditor.Remove(p, 0, 2));
        Assert.False(song.Highlight.Has);
    }

    [Fact]
    public void PasteIsOfferedOnTheHighlightOnlyWhenCellsWereCopied()
    {
        var routing = new EditRouting();
        Assert.False(routing.Resolve(FocusTarget.BarHighlight, new EditSelectionState(HasHighlight: true, ClipboardHasBars: true), EditVerb.Paste).IsAvailable);
        Assert.Equal("Highlight.Paste", routing.Resolve(FocusTarget.BarHighlight, new EditSelectionState(HasHighlight: true, ClipboardHasRange: true), EditVerb.Paste).CommandId);
    }
}
