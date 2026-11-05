using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Docking;

namespace TabForge.Avalonia;

/// <summary>Dialog positions belong to this process only, never to the saved application settings.</summary>
internal sealed class DialogWindowPlacement
{
    private readonly Dictionary<Type, PixelPoint> _positions = new();
    private readonly ConditionalWeakTable<Window, Tracking> _windows = new();
    private sealed class Tracking { internal bool Ready; internal bool Subscribed; }
    private static DialogWindowPlacement? _session;

    /// <summary>Forgets where this session's windows were, as a new session would.</summary>
    internal static void ForgetSession() => _session?._positions.Clear();

    internal static void RegisterWindowHook()
    {
        if (_session is not null) return;
        var session = _session = new DialogWindowPlacement();
        // This event runs before initial layout and before Avalonia calculates the startup position.
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => session.Prepare(window));
    }

    internal void Prepare(Window window)
    {
        // These are document/docking surfaces, not dialogs; they have their own placement policies.
        if (window is MainWindow or DockFloatingWindow or DockDragGhost) return;
        // Automated captures deliberately keep their windows off screen and inactive.
        if (!window.ShowActivated && !window.ShowInTaskbar) return;
        var tracking = _windows.GetOrCreateValue(window);
        tracking.Ready = false;
        var remembered = _positions.TryGetValue(window.GetType(), out var position);
        window.WindowStartupLocation = remembered ? WindowStartupLocation.Manual : WindowStartupLocation.CenterOwner;
        if (remembered) window.Position = position;

        EventHandler? opened = null;
        opened = (_, _) =>
        {
            window.Opened -= opened;
            Place(window, remembered ? position : null);
            tracking.Ready = true;
            if (window.WindowState == WindowState.Normal) _positions[window.GetType()] = window.Position;
        };
        window.Opened += opened;
        // Track while visible, not on Closed (the native window has already been destroyed then).
        if (tracking.Subscribed) return;
        tracking.Subscribed = true;
        window.PositionChanged += (_, _) =>
        {
            if (tracking.Ready && window.IsVisible && window.WindowState == WindowState.Normal)
                _positions[window.GetType()] = window.Position;
        };
    }

    private static void Place(Window window, PixelPoint? remembered)
    {
        var owner = window.Owner as Window;
        var screen = (remembered is { } point ? window.Screens.ScreenFromPoint(point) : null)
            ?? (owner is not null ? window.Screens.ScreenFromWindow(owner) : null)
            ?? window.Screens.Primary;
        var scaling = screen?.Scaling ?? window.DesktopScaling;
        var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, scaling);
        PixelPoint origin;
        if (remembered is { } saved) origin = saved;
        else if (owner is not null && owner.WindowState != WindowState.Minimized)
        {
            // Width/Height describe the restored size of a maximised owner. Use its live client bounds,
            // after initial layout, rather than centring against a stale normal-window rectangle.
            var ownerRect = new PixelRect(owner.Position, PixelSize.FromSize(owner.ClientSize, owner.DesktopScaling));
            origin = Centre(ownerRect, size);
        }
        else if (screen is not null) origin = Centre(screen.WorkingArea, size);
        else return;
        window.Position = screen is not null ? Clamp(origin, size, screen.WorkingArea) : origin;
    }

    internal static PixelPoint Centre(PixelRect owner, PixelSize size) =>
        new(owner.X + (owner.Width - size.Width) / 2, owner.Y + (owner.Height - size.Height) / 2);

    internal static PixelPoint Clamp(PixelPoint position, PixelSize size, PixelRect area) =>
        new(Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - size.Width)),
            Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
}
