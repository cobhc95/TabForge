using System.Linq;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Rendering;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Render dialog's "Selected bars" (RenderSnip copy and range, RenderDialogPresenter label).
// Does not own: the render run.
// Tests: itself.
public sealed class RenderSnipTests
{
    private static SelectionSnapshot Range(int track, int start, int end, SelectionScope scope = SelectionScope.ThisTrack) =>
        new(track, scope, true, start, end, 0, -1, FocusTarget.None, [], []);

    [Fact]
    public void SnipKeepsOnlySelectedBarsOfTheSelectedTrackAndLeavesTheSongAlone()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => t.Measures.Count > 3 && t.Measures.Skip(1).Take(2).Any(m => m.Cells.Any(c => c.Notes.Count > 0)));
        var index = song.Tracks.IndexOf(track);
        var before = track.Measures[1].Cells.Count(c => c.Notes.Count > 0);
        var snip = RenderSnip.Build(song, Range(index, 1, 2));
        var copy = snip.Map[track];
        Assert.Equal(before, track.Measures[1].Cells.Count(c => c.Notes.Count > 0));
        Assert.Equal(before, copy.Measures[1].Cells.Count(c => c.Notes.Count > 0));
        Assert.DoesNotContain(copy.Measures[0].Cells, c => c.Notes.Count > 0);
        Assert.All(song.Tracks.Where(t => t != track), t => Assert.True(snip.Map[t].Mute));
        Assert.Equal(track.Measures.Count, copy.Measures.Count);
    }

    [Fact]
    public void LabelNamesTracksBarsAndClips()
    {
        Assert.Equal("Selected bars (3 tracks, bars 33-40)", RenderDialogPresenter.SelectedBarsLabel(3, 32, 39, 0));
        Assert.Equal("Selected bars (1 track, bar 2, 2 clips)", RenderDialogPresenter.SelectedBarsLabel(1, 1, 1, 2));
    }
}
