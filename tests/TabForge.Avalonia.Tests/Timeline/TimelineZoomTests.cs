using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the arrangement timeline zoom: the Core steps and clamp, the timeline width following the factor, and the zoom commands the transport buttons run.
/// Tests: TimelineZoomRules, TimelineView.ZoomFactor, MainWindowTimelineZoom, HotkeyCatalog.
/// </summary>
public class TimelineZoomTests
{
    [Fact]
    public void StepsMoveOneStepAndStopAtTheEnds()
    {
        Assert.Equal(1.5, TimelineZoomRules.Step(1.0, 1));
        Assert.Equal(0.75, TimelineZoomRules.Step(1.0, -1));
        Assert.Equal(TimelineZoomRules.MaxFactor, TimelineZoomRules.Step(TimelineZoomRules.MaxFactor, 1));
        Assert.Equal(TimelineZoomRules.MinFactor, TimelineZoomRules.Step(TimelineZoomRules.MinFactor, -1));
        Assert.Equal(TimelineZoomRules.DefaultFactor, TimelineZoomRules.Clamp(double.NaN));
        Assert.Equal(TimelineZoomRules.MaxFactor, TimelineZoomRules.Clamp(99));
    }

    [AvaloniaFact]
    public void TimelineWidthFollowsTheZoomStep()
    {
        var view = new TimelineView(DemoSongFactory.Create());
        var standard = view.BarsWidth;
        view.ZoomFactor = TimelineZoomRules.Step(view.ZoomFactor, 1);
        Assert.Equal(standard * 1.5, view.BarsWidth, 6);
        Assert.Equal(standard * 1.5, view.Width, 6);
    }

    [Fact]
    public void ZoomHotkeysDoNotClashWithTheScoreZoom()
    {
        var timeline = HotkeyCatalog.All.Where(a => a.Id.StartsWith("Timeline.Zoom")).Select(a => a.DefaultGesture).ToArray();
        var score = HotkeyCatalog.All.Where(a => a.Id is "View.ZoomIn" or "View.ZoomOut").Select(a => a.DefaultGesture).ToArray();
        Assert.Equal(2, timeline.Length);
        Assert.Empty(timeline.Intersect(score));
    }
}
