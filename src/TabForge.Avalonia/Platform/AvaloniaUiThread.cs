using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// <see cref="IUiThread"/> over an Avalonia <see cref="Dispatcher"/>: each call maps one to one onto the dispatcher call with the same priority.
/// Owns: the Avalonia dispatcher mapping and the shutdown flag. Does not own: which dispatcher (the caller passes it; the app passes <c>Dispatcher.UIThread</c>).
/// Tests: TabForge.Avalonia.Tests.UiThreadTests.
/// </summary>
public sealed class AvaloniaUiThread : IUiThread
{
    private readonly Dispatcher _dispatcher;
    private bool _isShuttingDown;

    /// <summary>The adapter over the application's UI thread, for the composition root.</summary>
    public static AvaloniaUiThread ForApplication() => new(Dispatcher.UIThread);

    /// <summary>Raised when an exception escapes a queued call on this dispatcher. It is not marked handled, so it still ends the call as before.</summary>
    public event Action<Exception>? UnhandledException;

    public AvaloniaUiThread(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        // One handler per adapter on a long-lived dispatcher; each only sets the flag or forwards the exception.
        _dispatcher.ShutdownStarted += (_, _) => _isShuttingDown = true;
        _dispatcher.UnhandledException += (_, args) => UnhandledException?.Invoke(args.Exception);
    }

    /// <summary>The Avalonia priority with the same meaning as <paramref name="priority"/>. Values rise with the TabForge value, so the order is kept (tested).</summary>
    public static DispatcherPriority ToAvalonia(UiPriority priority) => priority switch
    {
        UiPriority.SystemIdle => DispatcherPriority.SystemIdle,
        UiPriority.ApplicationIdle => DispatcherPriority.ApplicationIdle,
        UiPriority.ContextIdle => DispatcherPriority.ContextIdle,
        UiPriority.Background => DispatcherPriority.Background,
        UiPriority.Input => DispatcherPriority.Input,
        UiPriority.Loaded => DispatcherPriority.Loaded,
        UiPriority.Render => DispatcherPriority.Render,
#pragma warning disable CS0618 // Avalonia marks DataBind obsolete (a WPF-compatibility leftover); the value still sorts between Render and Normal.
        UiPriority.DataBind => DispatcherPriority.DataBind,
#pragma warning restore CS0618
        UiPriority.Normal => DispatcherPriority.Normal,
        UiPriority.Send => DispatcherPriority.Send,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown UI priority."),
    };

    public bool CheckAccess() => _dispatcher.CheckAccess();
    public bool IsShuttingDown => _isShuttingDown;
    public void Post(Action work, UiPriority priority = UiPriority.Normal) => _dispatcher.Post(work, ToAvalonia(priority));
    public async Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) => await _dispatcher.InvokeAsync(work, ToAvalonia(priority));
    public async Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => await _dispatcher.InvokeAsync(work, ToAvalonia(priority));
    public async Task YieldAsync(UiPriority priority) => await _dispatcher.InvokeAsync(static () => { }, ToAvalonia(priority));
    public void Invoke(Action work, UiPriority priority = UiPriority.Send) => _dispatcher.Invoke(work, ToAvalonia(priority));
    public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => _dispatcher.Invoke(work, ToAvalonia(priority));

    public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background) =>
        new AvaloniaUiTimer(interval, ToAvalonia(priority));
}
