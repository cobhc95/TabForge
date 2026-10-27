using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the continuous score's horizontal origin after every trigger that used to leave a blank band on the left: follow, resize,
/// UI scale, incremental relayout and detach/re-attach. One shared helper asserts the viewer offset stays in range, the laid-out
/// content matches the extent, a centred page fills the viewport, and the drawn page starts at or left of the viewport's left edge.
/// Does not own: the reflow itself (ScoreContinuousFillTests), the follow maths (FollowSettingsTests) or the scale mechanism (WindowUiScaleTests).
/// Tests: ScorePreviewView.FollowPlayhead, ScorePreviewView.Refresh, ScoreGeometryProbe.ContentSize.
/// </summary>
public sealed class ScoreHorizontalOriginTests
{
    /// <summary>Pixels the edges may differ by: the viewer border (ScorePreviewView ViewerBorder) and rounding.</summary>
    private const double EdgeTolerance = 2;

    /// <summary>The followed bar (bar 20). BlankProject holds only SongDocument.NewSongBars bars, so the follow tests use the full
    /// demo song, the fixture the other follow tests use for distant bars.</summary>
    private const int FollowedBar = 19;

    /// <summary>Lets the layout arrange and the Background-posted reflow run; no sleeps.</summary>
    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// The regression invariant: the horizontal offset stays inside the scrollable range, the laid-out content matches the extent, a
    /// centred continuous page fills the viewport, and the drawn page (its paper rect starts at 0,0) starts at or left of the
    /// viewport's left edge, so no blank band sits on the left.
    /// </summary>
    private static void AssertNoLeftGap(ScorePreviewView score, ScrollViewer viewer, string step)
    {
        var extent = viewer.Extent.Width;
        var viewport = viewer.Viewport.Width;
        var maxOffset = Math.Max(0, extent - viewport);
        Assert.True(viewer.Offset.X >= -EdgeTolerance && viewer.Offset.X <= maxOffset + EdgeTolerance,
            $"{step}: offset X {viewer.Offset.X} is outside [0, {maxOffset}] (extent {extent}, viewport {viewport}).");
        var (pageWidth, _) = score.Probe.ContentSize();
        Assert.True(Math.Abs(extent - pageWidth * score.Zoom) <= EdgeTolerance,
            $"{step}: extent {extent} is not the laid-out page {pageWidth} at zoom {score.Zoom}.");
        if (score.Probe.Appearance.CenterSystems && !score.Probe.HorizontalScroll)
            Assert.True(pageWidth * score.Zoom >= viewport - EdgeTolerance,
                $"{step}: centred page width {pageWidth} at zoom {score.Zoom} is narrower than the viewport {viewport}.");
        var pageLeft = score.TranslatePoint(default, viewer)!.Value.X;
        Assert.True(pageLeft <= EdgeTolerance,
            $"{step}: the drawn page starts {pageLeft} past the viewport's left edge (a left gap).");
    }

    /// <summary>
    /// Parks the playhead on bar 20 through the follower. Not verified: the other follow tests reach FollowPlayhead through a
    /// session PumpFrame; this calls it directly on a Play state set from the shown song's own timeline, so the test needs no sleeps.
    /// </summary>
    private static void FollowToBar19(ScorePreviewView score, SongProject project, Window window)
    {
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var span = timeline.Bars.First(b => b.Bar == FollowedBar);
        score.Probe.Play.Set(timeline, 0, FollowedBar, 0, 0.5, (span.StartMs + span.EndMs) / 2);
        score.FollowPlayhead();
        Settle(window);
    }

    [AvaloniaFact]
    public void FollowToBar19WithContinuousFollowKeepsTheOriginAcrossSystems()
    {
        var project = FullDemoSongFactory.Create();
        var score = new ScorePreviewView { Follow = new FollowSettings { Mode = FollowModes.Smooth, ContinuousScroll = true } };
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 600 };
        var window = new Window { Content = viewer, Width = 900, Height = 600 };
        try
        {
            window.Show();
            score.Show(new SongDocument(() => null) { Project = project, ZoomFactor = 1 });
            Settle(window);
            var before = viewer.Offset.Y;
            FollowToBar19(score, project, window);
            Assert.True(viewer.Offset.Y > before, "The follow did not scroll to bar 20.");
            AssertNoLeftGap(score, viewer, "follow to bar 20, wrapped systems");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FollowToBar19WithContinuousFollowKeepsTheOriginOnOneLine()
    {
        var project = FullDemoSongFactory.Create();
        var score = new ScorePreviewView { Follow = new FollowSettings { Mode = FollowModes.Smooth, ContinuousScroll = true } };
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = true;
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 600 };
        var window = new Window { Content = viewer, Width = 900, Height = 600 };
        try
        {
            window.Show();
            score.Show(new SongDocument(() => null) { Project = project, ZoomFactor = 1 });
            Settle(window);
            FollowToBar19(score, project, window);
            Assert.True(viewer.Offset.X > 0, "The follow did not scroll sideways to bar 20.");
            AssertNoLeftGap(score, viewer, "follow to bar 20, one line");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ResizeBetween2000And1300KeepsTheOrigin()
    {
        var score = new ScorePreviewView();
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        var viewer = new ScrollViewer { Content = score };
        var window = new Window { Content = viewer, Width = 2000, Height = 900 };
        try
        {
            window.Show();
            score.Show(new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 });
            Settle(window);
            AssertNoLeftGap(score, viewer, "at 2000");
            window.Width = 1300;
            Settle(window);
            AssertNoLeftGap(score, viewer, "at 1300");
            window.Width = 2000;
            Settle(window);
            AssertNoLeftGap(score, viewer, "back at 2000");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UiScaleBetweenEightyAndOneKeepsTheOrigin()
    {
        var score = new ScorePreviewView();
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        var viewer = new ScrollViewer { Content = score };
        var root = new LayoutTransformControl { Child = viewer };
        var window = new Window { Width = 1800, Height = 900, Content = root };
        try
        {
            ThemeSettingsBinding.ApplyWindow(window, root, new AppearanceSettings { UiScale = 0.8 });
            window.Show();
            score.Show(new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 });
            Settle(window);
            AssertNoLeftGap(score, viewer, "at scale 0.8");
            ThemeSettingsBinding.ApplyWindow(window, root, new AppearanceSettings { UiScale = 1.0 });
            Settle(window);
            AssertNoLeftGap(score, viewer, "at scale 1.0");
            ThemeSettingsBinding.ApplyWindow(window, root, new AppearanceSettings { UiScale = 0.8 });
            Settle(window);
            AssertNoLeftGap(score, viewer, "back at scale 0.8");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IncrementalRelayoutMidSongKeepsTheOrigin()
    {
        var score = new ScorePreviewView();
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        var viewer = new ScrollViewer { Content = score };
        var window = new Window { Content = viewer, Width = 1600, Height = 900 };
        try
        {
            window.Show();
            score.Show(new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 });
            Settle(window);
            AssertNoLeftGap(score, viewer, "before the relayout");
            score.Probe.Layout.InvalidateMeasures(1, 1);
            score.Refresh();
            Settle(window);
            AssertNoLeftGap(score, viewer, "after an incremental relayout of bar 2");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DetachAndReattachKeepsTheOrigin()
    {
        var score = new ScorePreviewView();
        score.Probe.Appearance.CenterSystems = true;
        score.Probe.HorizontalScroll = false;
        var viewer = new ScrollViewer { Content = score };
        var window = new Window { Content = viewer, Width = 1600, Height = 900 };
        try
        {
            window.Show();
            score.Show(new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 });
            Settle(window);
            AssertNoLeftGap(score, viewer, "before the detach");
            viewer.Content = null;
            Dispatcher.UIThread.RunJobs();
            viewer.Content = score;
            Settle(window);
            AssertNoLeftGap(score, viewer, "after re-attach");
        }
        finally
        {
            window.Close();
        }
    }
}
