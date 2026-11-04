using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: timer lifetime guards for hidden, minimized and detached editor controls.
// Does not own: the engine's polling or parameter protocol.
public sealed class VisibilityPollingTests
{
    [AvaloniaFact]
    public void HiddenAncestorMinimizationAndDetachStopTheTimer()
    {
        var owner = new Border();
        var parent = new Border { Child = owner };
        var window = new Window { Content = parent };
        var timer = new AvaloniaUiThread(Dispatcher.UIThread).CreateTimer(TimeSpan.FromMilliseconds(UiTiming.SharedMeterTickMs));
        var resumed = 0;
        _ = new VisibilityPolling(owner, timer, () => resumed++);
        window.Show();
        Assert.True(timer.IsEnabled);
        Assert.Equal(1, resumed);
        parent.IsVisible = false;
        Assert.False(timer.IsEnabled);
        parent.IsVisible = true;
        Assert.True(timer.IsEnabled);
        Assert.Equal(2, resumed);
        window.WindowState = WindowState.Minimized;
        Assert.False(timer.IsEnabled);
        window.WindowState = WindowState.Normal;
        Assert.True(timer.IsEnabled);
        window.Content = null;
        Assert.False(timer.IsEnabled);
        window.Close();
    }
}
