using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score keeps its cursor in view: a cursor move that leaves the cursor's system mostly out of the viewer scrolls the viewer to it,
//   and the rule that decides this (Core ScoreCursorSight).
// Does not own: the cursor's moves (Core ScorePreviewEditor), the follow during playback (ScorePlayheadFollower).
// Tests: ScoreCursorSightTests (this file).
public class ScoreCursorSightTests
{
    /// <summary>Right-arrow presses that move the cursor well down the demo score (bar 12 and beyond).</summary>
    private const int PressLimit = 800, TargetBar = 12;

    [AvaloniaFact]
    public void ArrowKeysThatMoveTheCursorDownTheScoreScrollItsSystemIntoView()
    {
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 300 };
        var window = new Window { Width = 900, Height = 300, Content = viewer };
        window.Show();
        score.SetZoom(1);
        score.Refresh();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        score.Focus();
        score.Editor.SetPosition(0, 0, 0);

        for (var press = 0; press < PressLimit && (score.Probe.Cursor?.Measure ?? 0) < TargetBar; press++)
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);

        var bar = score.Probe.Cursor!.Value.Measure;
        Assert.True(bar >= TargetBar, $"the cursor reached bar {bar}");
        var top = ScoreSectionLinks.BarTop(score.Probe, bar) * score.Zoom;
        var height = score.Probe.SystemBandHeight * score.Zoom;
        var shown = Math.Min(top + height, viewer.Offset.Y + viewer.Viewport.Height) - Math.Max(top, viewer.Offset.Y);
        Assert.True(viewer.Offset.Y > 0, "the viewer scrolled down to the cursor");
        Assert.True(shown >= height * ScoreCursorSight.MinimumVisibleShare, "the cursor's system shows at least a quarter");
        window.Close();
    }

    [Fact]
    public void CursorSightScrollsOnlyWhenLessThanAQuarterOfTheSystemShows()
    {
        const double viewportHeight = 300, height = 200;

        Assert.Null(ScoreCursorSight.VerticalOffset(top: 100, height, viewportTop: 0, viewportHeight, marginPercent: 20));
        var moved = ScoreCursorSight.VerticalOffset(top: 500, height, viewportTop: 0, viewportHeight, marginPercent: 20);
        Assert.Equal(500 - viewportHeight * 20 / 100.0, moved);
    }
}
