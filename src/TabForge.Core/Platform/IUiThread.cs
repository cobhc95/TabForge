using System;
using System.Threading.Tasks;

namespace TabForge.Platform;

/// <summary>
/// The UI thread as a port: queue work, run work, timers. The WPF adapter is <c>Platform/Wpf/WpfUiThread</c>; WPF code gets one with <c>Ui.Of(element)</c> or <c>Ui.Current</c>.
/// Owns: the threading contract. Does not own: what runs on it.
/// Tests: UiThreadTests.
/// </summary>
public interface IUiThread
{
    /// <summary>True when the calling thread is this UI thread.</summary>
    bool CheckAccess();
    /// <summary>True once the thread has begun shutting down; queued work may no longer run.</summary>
    bool IsShuttingDown { get; }
    /// <summary>Queues <paramref name="work"/> and returns at once.</summary>
    void Post(Action work, UiPriority priority = UiPriority.Normal);
    /// <summary>Queues <paramref name="work"/>; the task completes after it ran.</summary>
    Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal);
    /// <summary>Queues <paramref name="work"/>; the task completes with its result after it ran.</summary>
    Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal);
    /// <summary>Yields the thread: the task completes at a turn queued at <paramref name="priority"/>.</summary>
    Task YieldAsync(UiPriority priority);
    /// <summary>Runs <paramref name="work"/> on this thread and waits for it. Never call it from a scheduler or output thread (it waits for the UI thread: deadlock).</summary>
    void Invoke(Action work, UiPriority priority = UiPriority.Send);
    /// <summary>Runs <paramref name="work"/> on this thread and returns its result. Never call it from a scheduler or output thread (it waits for the UI thread: deadlock).</summary>
    T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send);
    /// <summary>A stopped timer on this thread.</summary>
    IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background);
}

/// <summary>A repeating timer on a UI thread; <see cref="Tick"/> fires on that thread.</summary>
public interface IUiTimer
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    event EventHandler? Tick;
    void Start();
    void Stop();
}
