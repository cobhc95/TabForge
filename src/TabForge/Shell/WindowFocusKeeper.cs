using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TabForge.Shell;

// Owns: keeping focus inside TabForge when one of its windows closes. When a window that had focus closes and the foreground
//   window is no longer TabForge's, the window it was opened from (its owner), else the most recently active open TabForge window,
//   else the main window, is activated. A modal dialog of a candidate takes the focus instead of the disabled candidate.
// Does not own: the windows, their owners or modal dialogs (DialogHost, ThemedMessageBox), or the native focus rules themselves
//   (Windows gives focus to the next window in z-order, possibly another program, when a window is destroyed).
// Tests: TestWindowFocusKeeper (the host seam fakes the foreground window and records the activation).
// Created once at start-up by Register and held by App. Holds no static state.

/// <summary>What the keeper needs from the running app. The app's implementation is <see cref="AppWindowFocusHost"/>.</summary>
internal interface IWindowFocusHost
{
    /// <summary>Every window of the app that is still open.</summary>
    IEnumerable<Window> OpenWindows { get; }
    /// <summary>The app's main window, the last resort.</summary>
    Window? MainWindow { get; }
    /// <summary>True once app exit has started: nothing is activated then.</summary>
    bool ShuttingDown { get; }
    /// <summary>True when the foreground window belongs to this process.</summary>
    bool ForegroundIsTabForge { get; }
    /// <summary>False while the window is disabled by a modal dialog that is open over it.</summary>
    bool IsEnabled(Window window);
    void Activate(Window window);
    /// <summary>Runs an action once the close in progress has finished (Windows has picked the next foreground window by then).</summary>
    void Post(Action action);
}

internal sealed class WindowFocusKeeper
{
    private sealed record ClosingState(bool ForegroundWasTabForge, Window? Owner, bool WasActive);

    private readonly IWindowFocusHost _host;
    private readonly HashSet<Window> _tracked = new();
    private readonly HashSet<Window> _closing = new();
    private readonly Dictionary<Window, ClosingState> _pending = new();
    private readonly List<Window> _recent = new();   // most recently active first

    internal WindowFocusKeeper(IWindowFocusHost host) => _host = host;

    /// <summary>Starts keeping focus for every window that loads from now on. The caller keeps the returned keeper alive.</summary>
    public static WindowFocusKeeper Register(IWindowFocusHost host)
    {
        var keeper = new WindowFocusKeeper(host);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window) keeper.Track(window); }));
        return keeper;
    }

    /// <summary>Follows one window: its activations, and its close.</summary>
    internal void Track(Window window)
    {
        if (!_tracked.Add(window)) return;
        window.Activated += (_, _) => NoteActive(window);
        window.Closing += (_, _) =>
        {
            _closing.Add(window);
            _pending[window] = new ClosingState(_host.ForegroundIsTabForge, window.Owner, window.IsActive);
            // A close that gets cancelled never reaches Closed: the window is open again and may be a candidate again.
            _host.Post(() => { if (!IsClosed(window)) _closing.Remove(window); });
        };
        window.Closed += (_, _) =>
        {
            _tracked.Remove(window);
            _closing.Remove(window);
            _recent.Remove(window);
            if (!_pending.Remove(window, out var state) || !state.ForegroundWasTabForge) return;   // the user had already switched to another program
            // The closing window had the focus: hand it on now, while this process is still the foreground one (afterwards Windows may only
            // flash the taskbar button instead of activating). The posted check repairs what this could not.
            if (state.WasActive && !_host.ShuttingDown && Choose(window, state.Owner) is { } next) _host.Activate(next);
            _host.Post(() => Restore(window, state.Owner));
        };
    }

    /// <summary>Moves the window to the front of the recent list (called on every activation).</summary>
    internal void NoteActive(Window window)
    {
        _recent.Remove(window);
        _recent.Insert(0, window);
    }

    /// <summary>Activates the window that should take the focus after <paramref name="closed"/> closed; null when nothing should.</summary>
    internal Window? Restore(Window closed, Window? owner)
    {
        if (_host.ShuttingDown) return null;
        // Focus is still in TabForge and a TabForge window has it: nothing to repair.
        if (_host.ForegroundIsTabForge && AnyTabForgeWindowActive()) return null;
        var target = Choose(closed, owner);
        if (target is null) return null;
        _host.Activate(target);
        return target;
    }

    /// <summary>The first usable window in the order: owner, recent windows, other open windows, main window.</summary>
    internal Window? Choose(Window closed, Window? owner)
    {
        var open = _host.OpenWindows.ToList();
        var order = new List<Window?> { owner };
        order.AddRange(_recent);
        order.AddRange(open);
        order.Add(_host.MainWindow);
        foreach (var candidate in order)
        {
            if (candidate is null || ReferenceEquals(candidate, closed) || !Usable(candidate, closed)) continue;
            if (_host.IsEnabled(candidate)) return candidate;
            // Disabled behind a modal dialog: the dialog takes the focus.
            if (open.FirstOrDefault(w => ReferenceEquals(w.Owner, candidate) && Usable(w, closed)) is { } dialog) return dialog;
        }
        return null;
    }

    private bool AnyTabForgeWindowActive() => _host.OpenWindows.Any(w => w.IsActive);

    private bool Usable(Window window, Window closed) =>
        window.IsLoaded && window.IsVisible && window.WindowState != WindowState.Minimized
        && !ReferenceEquals(window, closed) && !_closing.Contains(window);

    private bool IsClosed(Window window) => !window.IsVisible || !_host.OpenWindows.Contains(window);
}

/// <summary>The keeper's host in the running app: the real windows, the foreground window of this process, and the native enabled state.</summary>
internal sealed class AppWindowFocusHost : IWindowFocusHost
{
    public IEnumerable<Window> OpenWindows => System.Windows.Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>();
    public Window? MainWindow => System.Windows.Application.Current?.MainWindow;
    public bool ShuttingDown => System.Windows.Application.Current?.Dispatcher.HasShutdownStarted ?? true;

    public bool ForegroundIsTabForge
    {
        get
        {
            var foreground = GetForegroundWindow();
            return foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out var processId) != 0 && processId == (uint)Environment.ProcessId;
        }
    }

    public bool IsEnabled(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle == IntPtr.Zero || IsWindowEnabled(handle);
    }

    public void Activate(Window window)
    {
        if (window.WindowState != WindowState.Minimized) window.Activate();
    }

    public void Post(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
}
