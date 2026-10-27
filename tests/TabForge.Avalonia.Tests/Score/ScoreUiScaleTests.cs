using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the continuous score's horizontal placement across a runtime UI scale change (0.8 -> 1.0 and back): the viewer must not keep an offset the
/// page does not need, and the page must sit where a host started directly at that scale puts it (no stale page width leaving a blank band).
/// Does not own: the scale clamping (ThemeSettingsBindingTests) or the general continuous reflow rule (ScoreContinuousFillTests).
/// Tests: ScorePreviewView.UpdatePageWidth, MeasureOverride, ApplyViewportWidth, OnUiScaleChanged; ThemeSettingsBinding.ApplyWindow.
/// </summary>
public sealed class ScoreUiScaleTests
{
    private const double EdgeTolerance = 1.5;

    private sealed record Host(Window Window, LayoutTransformControl Root, ScrollViewer Viewer, ScorePreviewView Score);

    /// <summary>The main window's shape: Window -> UI-scale LayoutTransformControl -> score ScrollViewer -> score (MainWindow.Compose).</summary>
    private static Host OpenAt(double scale)
    {
        var score = new ScorePreviewView();
        score.Probe.Appearance.CenterSystems = true;   // the continuous score view
        score.Probe.HorizontalScroll = false;
        var viewer = new ScrollViewer
        {
            Content = score,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var root = new LayoutTransformControl { Child = viewer };
        var window = new Window { Width = 1800, Height = 900, Content = root };
        ThemeSettingsBinding.ApplyWindow(window, root, new AppearanceSettings { UiScale = scale });
        window.Show();
        // The smallest project the other score tests use (SongDocument.NewSongBars bars) at normal size, so Show does not fit the width.
        score.Show(new SongDocument(() => null) { Project = SongDocument.BlankProject(), ZoomFactor = 1 });
        Settle(window);
        return new Host(window, root, viewer, score);
    }

    /// <summary>Lets the LayoutTransform arrange the new viewport and the score's Background-posted reflow run; no sleeps.</summary>
    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>What ScorePlayheadFollower does: writes a horizontal offset into the viewer.</summary>
    private static void ScrollLikeFollower(ScrollViewer viewer) => viewer.Offset = new Vector(450, viewer.Offset.Y);

    private static void AssertNoStaleOffset(Host host, string step)
    {
        if (host.Viewer.Extent.Width > host.Viewer.Viewport.Width + EdgeTolerance) return;   // a real horizontal scroll may keep its offset
        Assert.True(Math.Abs(host.Viewer.Offset.X) <= EdgeTolerance,
            $"{step}: viewer.Offset.X is {host.Viewer.Offset.X:0.##}, not 0, while extent {host.Viewer.Extent.Width:0.##} <= viewport {host.Viewer.Viewport.Width:0.##} " +
            $"(page width {host.Score.Probe.CurrentPageWidth:0.##})");
    }

    private static void AssertSamePageAsFresh(Host scaled, Host fresh, string step)
    {
        var left = scaled.Score.TranslatePoint(default, scaled.Viewer)!.Value.X;
        var freshLeft = fresh.Score.TranslatePoint(default, fresh.Viewer)!.Value.X;
        Assert.True(Math.Abs(left - freshLeft) <= EdgeTolerance,
            $"{step}: page left edge in the viewer is {left:0.##}, but a fresh host at the same scale puts it at {freshLeft:0.##} " +
            $"(offset X {scaled.Viewer.Offset.X:0.##}, extent {scaled.Viewer.Extent.Width:0.##}, viewport {scaled.Viewer.Viewport.Width:0.##}, " +
            $"page width {scaled.Score.Probe.CurrentPageWidth:0.##} vs fresh {fresh.Score.Probe.CurrentPageWidth:0.##})");
        Assert.True(Math.Abs(scaled.Score.Probe.CurrentPageWidth - fresh.Score.Probe.CurrentPageWidth) <= EdgeTolerance,
            $"{step}: continuous page width is {scaled.Score.Probe.CurrentPageWidth:0.##}, but a fresh host at the same scale has " +
            $"{fresh.Score.Probe.CurrentPageWidth:0.##} (offset X {scaled.Viewer.Offset.X:0.##}, extent {scaled.Viewer.Extent.Width:0.##}, " +
            $"viewport {scaled.Viewer.Viewport.Width:0.##})");
    }

    [AvaloniaFact]
    public void ScalingUpToOneKeepsTheContinuousPageWhereAFreshHostPutsIt()
    {
        var scaled = OpenAt(0.8);
        var fresh = OpenAt(1.0);
        try
        {
            ScrollLikeFollower(scaled.Viewer);
            var scrolledTo = scaled.Viewer.Offset.X;

            ThemeSettingsBinding.ApplyWindow(scaled.Window, scaled.Root, new AppearanceSettings { UiScale = 1.0 });
            Settle(scaled.Window);

            AssertNoStaleOffset(scaled, $"0.8 -> 1.0 (the follower wrote offset X {scrolledTo:0.##})");
            AssertSamePageAsFresh(scaled, fresh, "0.8 -> 1.0");
        }
        finally
        {
            scaled.Window.Close();
            fresh.Window.Close();
        }
    }

    [AvaloniaFact]
    public void ScalingBackToEightyKeepsTheContinuousPageWhereAFreshHostPutsIt()
    {
        var scaled = OpenAt(0.8);
        var fresh = OpenAt(0.8);
        try
        {
            ScrollLikeFollower(scaled.Viewer);
            var scrolledTo = scaled.Viewer.Offset.X;

            ThemeSettingsBinding.ApplyWindow(scaled.Window, scaled.Root, new AppearanceSettings { UiScale = 1.0 });
            Settle(scaled.Window);
            ThemeSettingsBinding.ApplyWindow(scaled.Window, scaled.Root, new AppearanceSettings { UiScale = 0.8 });
            Settle(scaled.Window);

            AssertNoStaleOffset(scaled, $"1.0 -> 0.8 (the follower wrote offset X {scrolledTo:0.##})");
            AssertSamePageAsFresh(scaled, fresh, "1.0 -> 0.8");
        }
        finally
        {
            scaled.Window.Close();
            fresh.Window.Close();
        }
    }
}
