using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using TabForge.Autosave;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Shell;

namespace TabForge.Avalonia.Shell;

// Owns: the open Avalonia main windows that share tab drags: added when made, removed when closed, and which one's tab strip lies
//   under a screen point (TabDragRules.ResolveDropTarget, the newest window first); and the autosave registry the windows share.
// Does not own: moving the songs (DocumentTabDragFlow) or the windows' lifetimes (MainWindowLifetime), the autosave passes (MainWindowAutosave).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests, TabForge.Avalonia.Tests.MainWindowAutosaveTests.
/// <summary>The main windows a song tab can be dragged between, and the autosave state they share. UI thread only.</summary>
public sealed class TabWindowRegistry
{
    private readonly List<MainWindow> _windows = new();

    public IReadOnlyList<MainWindow> Windows => _windows;

    /// <summary>The system clipboard of the whole app (the active window's) and the ONE score clipboard over it: every window and tear-off shares them, so a range copied in one pastes in another.</summary>
    internal Platform.AvaloniaScoreClipboard SystemClipboard { get; }
    internal TabForge.Services.ClipboardService Clipboard { get; }

    /// <summary>The services every window shares (plug-in trust, the meter tick).</summary>
    internal AppServices Services { get; }

    internal TabWindowRegistry(AppServices services)
    {
        Services = services;
        SystemClipboard = new Platform.AvaloniaScoreClipboard(() => (_windows.FirstOrDefault(w => w.IsActive) ?? _windows.LastOrDefault())?.Clipboard);
        Clipboard = new TabForge.Services.ClipboardService(SystemClipboard);
    }

    /// <summary>The autosave state of every song in every window (a song moved between windows keeps its copy).</summary>
    public AutosaveRegistry Autosave { get; } = new();

    /// <summary>The degraded mode after an unexpected error, shared by every window: saving over a file asks first.</summary>
    public DegradedSaveGate Degraded { get; } = new();

    /// <summary>Raised after a window was added or removed.</summary>
    public event Action? Changed;

    public void Add(MainWindow window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => { _windows.Remove(window); if (_windows.Count == 0) Services.Dispose(); Changed?.Invoke(); };
        Changed?.Invoke();
    }

    /// <summary>The shown window other than <paramref name="except"/> whose tab strip (empty space included) contains the screen point,
    /// the newest window first; null when none does.</summary>
    public MainWindow? StripWindowAt(PixelPoint screen, MainWindow except)
    {
        var candidates = _windows.Where(w => w != except && w.IsVisible).Reverse().ToList();
        var index = TabDragRules.ResolveDropTarget(screen.X, screen.Y, candidates.Select(w => ScreenRectOf(w.TabStrip)).ToList());
        return index < 0 ? null : candidates[index];
    }

    /// <summary>A control's rectangle in screen pixels.</summary>
    public static UiRect ScreenRectOf(Control control)
    {
        var origin = control.PointToScreen(default);
        var corner = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
        return new UiRect(origin.X, origin.Y, corner.X - origin.X, corner.Y - origin.Y);
    }
}
