using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view after a track is deleted or the delete is undone: every shown card belongs to a real track, each follows its own track,
/// and the card of a deleted track goes (no card keeps an index that now points at another track or past the end).
/// Tests: this class.
/// </summary>
public sealed class BandViewTrackDeleteTests
{
    private static void AssertCardsFollowTracks(BandWindow window, SongDocument document)
    {
        var view = window.View;
        Assert.Equal(view.ShownTrackIndices.Count, view.VisiblePanelCount);
        foreach (var index in view.ShownTrackIndices)
        {
            Assert.InRange(index, 0, document.Project.Tracks.Count - 1);
            Assert.Same(document.Project.Tracks[index], view.PanelFor(index)!.Staff.Probe.Track);
        }
        Assert.True(view.SelectedTrackIndex < document.Project.Tracks.Count);
    }

    [AvaloniaFact]
    public void DeletingTracksKeepsEveryCardOnItsOwnTrackAndUndoBringsThemBack()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        document.MarkClean();
        var window = new BandWindow(document);
        window.Show();
        var view = window.View;
        var firstShownId = document.Project.Tracks[view.ShownTrackIndices[0]].Id;

        MainWindowTrackMenu.RemoveTrack(document, view.ShownTrackIndices[0]);
        AssertCardsFollowTracks(window, document);
        Assert.DoesNotContain(firstShownId, view.ShownTrackIndices.Select(i => document.Project.Tracks[i].Id));

        MainWindowTrackMenu.RemoveTrack(document, document.Project.Tracks.Count - 1);
        AssertCardsFollowTracks(window, document);

        Assert.NotNull(DocumentEdits.Undo(document));
        AssertCardsFollowTracks(window, document);
        Assert.NotNull(DocumentEdits.Undo(document));
        AssertCardsFollowTracks(window, document);
        window.Close();
    }

    [AvaloniaFact]
    public void DeletingEveryShownTrackLeavesTheDefaultCards()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new BandWindow(document);
        window.Show();
        foreach (var id in window.View.ShownTrackIndices.Select(i => document.Project.Tracks[i].Id).ToList())
            MainWindowTrackMenu.RemoveTrack(document, document.Project.Tracks.FindIndex(t => t.Id == id));
        AssertCardsFollowTracks(window, document);
        Assert.NotEmpty(window.View.ShownTrackIndices);
        window.Close();
    }
}
