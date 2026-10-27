using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the continuous score in the main window fills the score pane at any zoom: the score starts at the viewer's left edge (no empty band), its
/// first system starts at the page margin, and no bar runs past the viewer's right edge (no horizontal bar).
/// Does not own: the layout of the systems (Core ScoreLayoutEngine), the reflow width rule (ScoreHorizontalBarTests) or the one-line centring (ScoreOneLineCentreTests).
/// Tests: TabForge.Avalonia.Tests.ScoreContinuousFillTests.
/// </summary>
public sealed class ScoreContinuousFillTests
{
    /// <summary>Pixels the edges may differ by: the viewer border (ScorePreviewView ViewerBorder) and rounding.</summary>
    private const double EdgeTolerance = 2;

    private static (MainWindow Main, ScorePreviewView Score) OpenMainWindow()
    {
        var main = new MainWindow { Width = 2000, Height = 1100 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        return (main, main.GetVisualDescendants().OfType<ScorePreviewView>().Single());
    }

    private static void Settle(MainWindow main)
    {
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        main.UpdateLayout();
    }

    private static void AssertFillsPane(ScorePreviewView score, string step)
    {
        var viewer = score.FindAncestorOfType<ScrollViewer>()!;
        var viewport = viewer.Viewport.Width;
        var scoreLeft = score.TranslatePoint(default, viewer)!.Value.X;
        var systems = score.Probe.Layout.GetLayout(score.Probe.Track!).Systems;
        var firstSystemLeft = scoreLeft + systems[0].X * score.Zoom;
        var lastBarRight = scoreLeft + systems.Max(system => system.X + system.Width) * score.Zoom;
        var gridRight = scoreLeft + (score.Probe.GridLeft + score.Probe.GridWidth) * score.Zoom;

        Assert.Equal(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
        Assert.True(Math.Abs(scoreLeft) <= EdgeTolerance, $"{step}: the score starts {scoreLeft} from the viewer's left edge (an empty band)");
        Assert.True(Math.Abs(firstSystemLeft - (scoreLeft + score.Probe.GridLeft * score.Zoom)) <= EdgeTolerance,
            $"{step}: the first system starts at {firstSystemLeft}, not at the page margin");
        Assert.True(lastBarRight <= viewport + EdgeTolerance, $"{step}: the last bar ends at {lastBarRight}, past the viewport {viewport}");
        // Every system fills the reflowed page, the final one too: no grey strip after the music on the right.
        Assert.True(systems.All(system => Math.Abs(scoreLeft + (system.X + system.Width) * score.Zoom - gridRight) <= EdgeTolerance),
            $"{step}: a system stops short of the page margin at {gridRight} (the final one at {lastBarRight})");
    }

    [AvaloniaFact]
    public void ContinuousPageWidthIsTheViewerWidthAtTheZoomAndANewViewportRecomputesIt()
    {
        var (main, score) = OpenMainWindow();
        score.Probe.HorizontalScroll = false;
        score.Probe.Appearance.CenterSystems = true;
        score.PageLayoutChanged();
        score.SetZoom(1);
        Assert.Equal(898, score.ReflowPageWidth(900), 6);
        score.SetZoom(2);
        Assert.Equal(449, score.ReflowPageWidth(900), 6);
        Assert.Equal(380, score.ReflowPageWidth(500), 6);   // the narrow-pane floor stays
        Settle(main);
        var viewer = score.FindAncestorOfType<ScrollViewer>()!;
        Assert.Equal((viewer.Viewport.Width - 2) / score.Zoom, score.Probe.CurrentPageWidth, 3);

        var oldViewport = viewer.Viewport.Width;
        main.Width = 1200;
        Settle(main);
        Assert.NotEqual(oldViewport, viewer.Viewport.Width);
        Assert.Equal((viewer.Viewport.Width - 2) / score.Zoom, score.Probe.CurrentPageWidth, 3);
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(0.9)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void ContinuousSystemsFillThePaneFromTheMarginWithNoHorizontalBar(double zoom)
    {
        var (main, score) = OpenMainWindow();
        score.Probe.HorizontalScroll = false;
        score.Probe.Appearance.CenterSystems = true;
        score.PageLayoutChanged();
        score.SetZoom(zoom);
        Settle(main);
        AssertFillsPane(score, $"continuous at {zoom}");
        main.Close();
    }

    [AvaloniaFact]
    public void ContinuousAfterPageFitAtNinetyPercentStillFillsThePane()
    {
        var (main, score) = OpenMainWindow();
        score.Probe.HorizontalScroll = false;
        score.Probe.Appearance.CenterSystems = false;
        score.FitWidth();
        Settle(main);
        score.Probe.Appearance.CenterSystems = true;
        score.PageLayoutChanged();
        Settle(main);
        AssertFillsPane(score, $"continuous after page fit at {score.Zoom}");
        main.Close();
    }

    [AvaloniaFact]
    public void ContinuousZoomStepDownFromOneHundredStillFillsThePane()
    {
        var (main, score) = OpenMainWindow();
        score.Probe.HorizontalScroll = false;
        score.Probe.Appearance.CenterSystems = true;
        score.PageLayoutChanged();
        Settle(main);
        score.ZoomStep(-1);
        Settle(main);
        AssertFillsPane(score, $"continuous after a zoom step to {score.Zoom}");
        main.Close();
    }

    [AvaloniaFact]
    public void ContinuousAtNinetyPercentFillsThePaneAfterTheWindowResizes()
    {
        var (main, score) = OpenMainWindow();
        score.Probe.HorizontalScroll = false;
        score.Probe.Appearance.CenterSystems = true;
        score.Show(new SongDocument(() => null) { Project = DemoSongFactory.Create(), ZoomFactor = 0.9 });
        Settle(main);
        main.Width = 1200;
        Settle(main);
        AssertFillsPane(score, "continuous at 90% after the window narrows");
        main.Width = 2000;
        Settle(main);
        AssertFillsPane(score, "continuous at 90% after the window widens");
        main.Close();
    }

    [AvaloniaFact]
    public void DemoSongInContinuousAtNinetyPercentFillsThePane()
    {
        var (main, score) = OpenMainWindow();
        score.Probe.HorizontalScroll = false;
        score.Probe.Appearance.CenterSystems = true;
        score.Show(new SongDocument(() => null) { Project = DemoSongFactory.Create(), ZoomFactor = 0.9 });
        Settle(main);
        AssertFillsPane(score, $"demo song in continuous at {score.Zoom}");
        main.Close();
    }
}
