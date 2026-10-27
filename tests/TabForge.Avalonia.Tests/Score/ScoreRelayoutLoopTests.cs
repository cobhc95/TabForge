using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the P0 startup crash guard — the score relayout loop terminates.
/// ViewerResized only reflows after the layout pass and only past the settle deadband, and a repeated
/// share-bar floor with the same values lays out nothing again, so the LayoutUpdated retry converges.
/// Does not own: the reflow width rule (ScoreContinuousFillTests) or the bar sharing (BandCardFitTests).
/// Tests: ScorePreviewView.ViewerResized, ScorePreviewView.ShareBarWidths, ScoreLayoutEngine.MinBarWidths.
/// </summary>
public sealed class ScoreRelayoutLoopTests
{
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.UpdateLayout();
    }
    [AvaloniaFact]
    public void ShareBarWidthsWithSameValuesDoesNotRelayOut()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        score.Show(song);
        var first = new double[] { 120, 130, 140 };
        score.ShareBarWidths(first);
        var floor = score.Probe.Layout.MinBarWidths;
        var layout = score.Probe.Layout.GetLayout(score.Probe.Track!);
        score.ShareBarWidths(new double[] { 120, 130, 140 });
        Assert.Same(floor, score.Probe.Layout.MinBarWidths);
        Assert.Same(layout, score.Probe.Layout.GetLayout(score.Probe.Track!));
    }

    [AvaloniaFact]
    public void ContinuousViewportSettlesAfterTwoLayouts()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        score.Show(song);
        score.Refresh();
        Settle(window);
        var want = score.ReflowPageWidth(viewer.Viewport.Width);
        Assert.Equal(want, score.Probe.PageWidthOverride, 1);
        Settle(window);
        Assert.Equal(want, score.Probe.PageWidthOverride, 1);
        Assert.Equal(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
        window.Close();
    }

    [AvaloniaFact]
    public void SubDeadbandViewportChangeDoesNotReflow()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 };
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 1000, Height = 700, Content = viewer };
        window.Show();
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        score.Show(song);
        score.Refresh();
        Settle(window);
        var settled = score.Probe.PageWidthOverride;
        window.Width = 1001;   // ~1 DIP of viewport: rounding residue, not a resize
        Settle(window);
        Assert.Equal(settled, score.Probe.PageWidthOverride, 1);
        window.Width = 1100;   // a real resize accumulates past the deadband and reflows
        Settle(window);
        Assert.Equal(score.ReflowPageWidth(viewer.Viewport.Width), score.Probe.PageWidthOverride, 1);
        Assert.NotEqual(settled, score.Probe.PageWidthOverride, 1);
        window.Close();
    }
}
