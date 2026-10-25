using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the per-frame cost guard of the timeline body on the demo song. One body draw must fit a 144 Hz frame (6.9 ms) with room for the rest of the frame.
/// Tests: TimelineView.DrawBody.
/// </summary>
public class TimelineFramePerfTests(ITestOutputHelper output)
{
    private const int WarmFrames = 30, TimedFrames = 400;
    private const double BodyBudgetMs = 1.5;

    [AvaloniaFact]
    public void BodyDrawStaysInsideTheFrameBudget()
    {
        var timeline = new TimelineView(DemoSongFactory.Create(), null);
        var window = new Window { Width = 1600, Height = 700, Content = timeline };
        window.Show();
        window.UpdateLayout();
        var d = new NullDrawContext();
        for (var i = 0; i < WarmFrames; i++) timeline.DrawBody(d, false);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < TimedFrames; i++) timeline.DrawBody(d, false);
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var perFrame = clock.Elapsed.TotalMilliseconds / TimedFrames;
        output.WriteLine($"DrawBody {perFrame:0.0000} ms/frame, {allocated / TimedFrames} bytes/frame");
        Assert.True(perFrame < BodyBudgetMs, $"{perFrame} ms per body draw");
        window.Close();
    }
}
