using System;
using Avalonia.Threading;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// <see cref="IUiTimer"/> over an Avalonia <see cref="DispatcherTimer"/>: <see cref="Tick"/> fires on the UI thread.
/// Owns: the timer wrapper. Does not own: the priority (<see cref="AvaloniaUiThread.CreateTimer"/> maps it) or the dispatcher (Avalonia's <see cref="DispatcherTimer"/> picks it).
/// Tests: TabForge.Avalonia.Tests.UiThreadTests.
/// </summary>
internal sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;

    /// <summary>A timer at background priority, for views outside Platform/ that must not name the dispatcher.</summary>
    public AvaloniaUiTimer(TimeSpan interval) : this(interval, DispatcherPriority.Background) { }

    public AvaloniaUiTimer(TimeSpan interval, DispatcherPriority priority) =>
        _timer = new DispatcherTimer(priority) { Interval = interval };

    public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
    public bool IsEnabled => _timer.IsEnabled;
    public event EventHandler? Tick { add => _timer.Tick += value; remove => _timer.Tick -= value; }
    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
