using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the horizontal scroll bar of the score's viewer: none in the continuous view while the systems wrap to the pane's width, and shown in
/// the page view only when the page is wider than the pane (a zoom past the pane's width).
/// Does not own: the reflow width rule (Core ScoreGeometryProbe, ContinuousMinPageWidth) or the zoom steps (ScorePreviewZoomOnOpenTests).
/// Tests: ScorePreviewView.Show, ScorePreviewView.Refresh, ScorePreviewView.PageLayoutChanged.
/// </summary>
public class ScoreHorizontalBarTests
{
    // Short enough that the score is taller than the pane, so the vertical bar takes width from the pane (the case that used to overflow).
    private const double PaneHeight = 300;

    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(700)]
    [InlineData(900)]
    [InlineData(1150)]
    public void ContinuousViewWrapsToThePaneWithNoHorizontalBar(double paneWidth)
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = paneWidth, Height = PaneHeight, Content = viewer };
        window.Show();
        score.Probe.Appearance.CenterSystems = true;
        score.Show(song);
        score.Refresh();
        window.UpdateLayout();
        window.UpdateLayout();

        Assert.Equal(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
        Assert.True(viewer.Extent.Width <= viewer.Viewport.Width, $"extent {viewer.Extent.Width} wider than viewport {viewer.Viewport.Width}");
        window.Close();
    }

    [AvaloniaFact]
    public void PageViewAtDoubleZoomShowsTheHorizontalBar()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 2 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 900, Height = PaneHeight, Content = viewer };
        window.Show();
        score.Probe.Appearance.CenterSystems = false;
        score.Show(song);
        window.UpdateLayout();
        window.UpdateLayout();

        Assert.Equal(ScrollBarVisibility.Auto, viewer.HorizontalScrollBarVisibility);
        Assert.True(viewer.Extent.Width > viewer.Viewport.Width, "a page at 200% is wider than the pane");
        window.Close();
    }
}
