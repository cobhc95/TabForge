using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Headless checks of the Avalonia UI-thread adapter: background posts run on the UI thread, priorities keep their order, and timers tick and stop.
/// Owns: the adapter behaviour checks. Does not own: the adapter (AvaloniaUiThread, AvaloniaUiTimer).
/// </summary>
public class UiThreadTests
{
    private static AvaloniaUiThread UiThreadOfTheTest() => new(Dispatcher.UIThread);

    [AvaloniaFact]
    public async Task PostFromBackgroundThreadRunsOnUiThread()
    {
        var uiThread = UiThreadOfTheTest();
        var postedOffUiThread = false;
        int? ranOnThreadId = null;

        await Task.Run(() =>
        {
            postedOffUiThread = !uiThread.CheckAccess();
            uiThread.Post(() => ranOnThreadId = Environment.CurrentManagedThreadId);
        });
        // Normal work runs in queue order, so this turn comes after the posted work.
        await uiThread.InvokeAsync(() => { });

        Assert.True(postedOffUiThread);
        Assert.True(uiThread.CheckAccess());
        Assert.Equal(Environment.CurrentManagedThreadId, ranOnThreadId);
    }

    [AvaloniaFact]
    public void MappedPrioritiesKeepUiPriorityOrder()
    {
        var values = Enum.GetValues<UiPriority>()
            .Select(priority => AvaloniaUiThread.ToAvalonia(priority).Value)
            .ToList();

        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i - 1] < values[i], $"Priority {i - 1} must run before priority {i}.");
    }

    [AvaloniaFact]
    public async Task QueuedWorkRunsHighestPriorityFirst()
    {
        var uiThread = UiThreadOfTheTest();
        var ran = new List<UiPriority>();

        // Posted lowest first; the higher priorities still run first.
        foreach (var priority in new[] { UiPriority.SystemIdle, UiPriority.Background, UiPriority.Normal, UiPriority.Send })
            uiThread.Post(() => ran.Add(priority), priority);
        // The lowest-priority turn queued last runs after every item above it.
        await uiThread.InvokeAsync(() => { }, UiPriority.SystemIdle);

        Assert.Equal(new[] { UiPriority.Send, UiPriority.Normal, UiPriority.Background, UiPriority.SystemIdle }, ran);
    }

    [AvaloniaFact]
    public async Task TimerTicksAtIntervalAndStops()
    {
        var timer = UiThreadOfTheTest().CreateTimer(TimeSpan.FromMilliseconds(10));
        var ticks = 0;
        timer.Tick += (_, _) => ticks++;

        Assert.Equal(TimeSpan.FromMilliseconds(10), timer.Interval);
        Assert.False(timer.IsEnabled);

        timer.Start();
        Assert.True(timer.IsEnabled);
        await Task.Delay(500);
        Assert.True(ticks >= 3, $"Expected at least 3 ticks in 500 ms at a 10 ms interval, got {ticks}.");

        timer.Stop();
        Assert.False(timer.IsEnabled);
        var ticksAtStop = ticks;
        await Task.Delay(100);
        Assert.Equal(ticksAtStop, ticks);
    }
}
