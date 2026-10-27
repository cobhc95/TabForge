using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view's row minimum height: with many tracks the rows keep their readable height and the view scrolls, with few they fill the view,
/// the "Band row minimum height" setting changes the minimum live, and selecting an off-screen track scrolls its row into view.
/// Tests: BandView, BandCardFit, BandScoreMenu, BandSettings.
/// </summary>
public sealed class BandRowMinHeightTests
{
    private const double Tolerance = 1;

    private static (BandWindow Window, BandView View, BandSettings Band) Open(double height, bool all)
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var window = new BandWindow(song) { Width = 900, Height = height };
        var band = new BandSettings();
        window.View.ScoreMenu = new BandScoreMenu(window.View, () => band, () => { }, () => { }, _ => { });
        window.Show();
        if (all)
            for (var i = 0; i < song.Project.Tracks.Count; i++)
                if (!song.Project.Tracks[i].IsAudio && !window.View.IsShown(i)) window.View.ToggleShown(i);
        Settle(window);
        return (window, window.View, band);
    }

    private static void Settle(BandWindow window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    [AvaloniaFact]
    public void ManyTracksKeepTheirMinimumHeightAndTheViewScrolls()
    {
        var (window, view, _) = Open(500, all: true);
        Assert.True(view.ShownTrackIndices.Count >= 5);
        Assert.True(view.Extent.Height > view.Viewport.Height + Tolerance, $"{view.Extent.Height} vs {view.Viewport.Height}");
        foreach (var panel in view.ShownTrackIndices.Select(i => view.PanelFor(i)!))
        {
            Assert.True(panel.Staff.Zoom >= BandCardFit.TargetStaffZoom - 1e-6, $"zoom {panel.Staff.Zoom}");
            Assert.True(panel.Bounds.Height >= panel.NaturalHeight(1) - Tolerance, $"height {panel.Bounds.Height}");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void FewTracksFillTheViewWithoutScrolling()
    {
        var (window, view, _) = Open(1000, all: false);
        Assert.True(view.Extent.Height <= view.Viewport.Height + Tolerance, $"{view.Extent.Height} vs {view.Viewport.Height}");
        window.Close();
    }

    [AvaloniaFact]
    public void TheRowMinimumSettingChangesTheMinimumLive()
    {
        var (window, view, band) = Open(500, all: true);
        band.RowMinHeight = BandChoices.RowSmall;
        view.ApplyRowMinHeight();
        Settle(window);
        var small = view.PanelFor(view.ShownTrackIndices[0])!.Staff.Zoom;
        band.RowMinHeight = BandChoices.RowLarge;
        view.ApplyRowMinHeight();
        Settle(window);
        var large = view.PanelFor(view.ShownTrackIndices[0])!.Staff.Zoom;
        Assert.True(large > small + 0.05, $"{small} vs {large}");
        window.Close();
    }

    [AvaloniaFact]
    public void SelectingAnOffScreenTrackScrollsItsRowIntoView()
    {
        var (window, view, _) = Open(500, all: true);
        var last = view.ShownTrackIndices[^1];
        var panel = view.PanelFor(last)!;
        Assert.True(panel.Bounds.Bottom > view.Viewport.Height);
        view.SelectTrack(last);
        Settle(window);
        Assert.True(view.Offset.Y > 0);
        Assert.True(panel.Bounds.Bottom <= view.Offset.Y + view.Viewport.Height + Tolerance);
        Assert.True(panel.Bounds.Top >= view.Offset.Y - Tolerance);
        window.Close();
    }
}
