using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>
/// The main window's saved position, size and maximised state. A start with window restore on opens at the saved position (when it is still
/// mostly on a screen) and size, maximised when the saved state says so (a fresh settings file says so: AppSettings.Maximised defaults to true).
/// The window copies its bounds and state back into the settings as they change, so a snapped window keeps its snapped bounds.
/// Owns: the position, size and maximised state of the main window in the settings. Does not own: when the settings file is written
/// beyond the scheduled save (MainWindowSettings, AppSettingsStore) or the dock layout.
/// Tests: TabForge.Avalonia.Tests.MainWindowPlacementTests.
/// </summary>
public static class MainWindowPlacement
{
    /// <summary>The share of the window's area that must lie on one screen's working area for the saved position to be used.</summary>
    internal const double MinimumOnScreenShare = 0.5;

    /// <summary>Opens the window at the saved position and size and state; a window without settings keeps its default size and is not maximised.
    /// True when the placement was applied (or no placement was needed); false when a guarded step failed and the caller should retry once
    /// the window is opened (Screens/Position are not ready on every machine at construction time). Never throws.</summary>
    public static bool Restore(Window window, MainWindowSettings? settings)
    {
        if (settings is null || !settings.Settings.General.RestoreWindow) return true;
        try
        {
            var saved = settings.Settings;
            // UI scale (or any appearance setting) only rescales content inside the window: an already-maximized
            // window is the target state, so write nothing instead of perturbing it with a size/state rewrite.
            if (saved.Maximised && window.WindowState == WindowState.Maximized) return true;
            double width, height;
            try
            {
                width = Math.Max(MainWindow.EffectiveMinimumWidth(window), saved.WindowWidth);
                height = Math.Max(MainWindow.EffectiveMinimumHeight(window), saved.WindowHeight);
                if (Math.Abs(window.Width - width) > 1e-9) window.Width = width;
                if (Math.Abs(window.Height - height) > 1e-9) window.Height = height;
            }
            catch (Exception ex)
            {
                try { Trace.Error(Trace.Ui, "placement: size: " + ex.Message); }
                catch (Exception) { } // swallow: the trace write itself failed
                return false;
            }
            if (window.WindowState == WindowState.Normal && saved.WindowLeft is { } left && saved.WindowTop is { } top)
            {
                PixelPoint origin;
                try { origin = new PixelPoint((int)Math.Round(left), (int)Math.Round(top)); }
                catch (Exception ex)
                {
                    try { Trace.Error(Trace.Ui, "placement: position: " + ex.Message); }
                    catch (Exception) { } // swallow: the trace write itself failed
                    return false;
                }
                bool onScreen;
                try { onScreen = IsMostlyOnScreen(window.Screens.All, origin, width, height); }
                catch (Exception ex)
                {
                    // Screens.All is the owner-PC crash neighbourhood (Win32/DPI path): leave the OS placement and ask for the deferred retry.
                    try { Trace.Error(Trace.Ui, "placement: screens: " + ex.Message); }
                    catch (Exception) { } // swallow: the trace write itself failed
                    return false;
                }
                if (onScreen)
                {
                    try
                    {
                        if (window.WindowStartupLocation != WindowStartupLocation.Manual) window.WindowStartupLocation = WindowStartupLocation.Manual;
                        if (window.Position != origin) window.Position = origin;
                    }
                    catch (Exception ex)
                    {
                        try { Trace.Error(Trace.Ui, "placement: position: " + ex.Message); }
                        catch (Exception) { } // swallow: the trace write itself failed
                        return false;
                    }
                }
            }
            if (saved.Maximised)
            {
                try { if (window.WindowState != WindowState.Maximized) window.WindowState = WindowState.Maximized; }
                catch (Exception ex)
                {
                    try { Trace.Error(Trace.Ui, "placement: maximized: " + ex.Message); }
                    catch (Exception) { } // swallow: the trace write itself failed
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            try { Trace.Error(Trace.Ui, "placement: restore: " + ex.Message); }
            catch (Exception) { } // swallow: the trace write itself failed
            return false;
        }
    }

    /// <summary>Applies the saved placement again only when the restore-window setting itself changes, so switching
    /// it on in Preferences takes effect at once; UI scale (or any other) preview/apply never touches placement.</summary>
    public static void FollowSettings(Window window, MainWindowSettings? settings)
    {
        if (settings is null) return;
        bool last = settings.Settings.General.RestoreWindow;
        Action<AppSettings> apply = live =>
        {
            bool current = live.General.RestoreWindow;
            if (current == last) return;
            last = current;
            if (current) Restore(window, settings);
        };
        settings.SettingsChanged += apply;
        window.Closed += (_, _) => settings.SettingsChanged -= apply;
    }

    /// <summary>Copies the window's position, size and state into the settings whenever one of them changes, and schedules the save.</summary>
    public static void Track(Window window, MainWindowSettings settings)
    {
        window.PositionChanged += (_, _) => Capture(window, settings);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty || e.Property == Window.WindowStateProperty) Capture(window, settings);
        };
    }

    /// <summary>True when the window, at <paramref name="origin"/> with the size in DIPs, has at least <see cref="MinimumOnScreenShare"/> of its area on one screen's working area.</summary>
    internal static bool IsMostlyOnScreen(IEnumerable<Screen> screens, PixelPoint origin, double widthDips, double heightDips)
    {
        foreach (var screen in screens)
        {
            var size = new PixelSize((int)Math.Round(widthDips * screen.Scaling), (int)Math.Round(heightDips * screen.Scaling));
            if (IsMostlyWithin(new PixelRect(origin, size), screen.WorkingArea)) return true;
        }
        return false;
    }

    /// <summary>True when at least <see cref="MinimumOnScreenShare"/> of <paramref name="window"/> lies inside <paramref name="area"/>.</summary>
    internal static bool IsMostlyWithin(PixelRect window, PixelRect area)
    {
        var overlap = window.Intersect(area);
        long windowArea = (long)window.Width * window.Height;
        if (windowArea <= 0) return false;
        long overlapArea = (long)overlap.Width * overlap.Height;
        return overlapArea >= MinimumOnScreenShare * windowArea;
    }

    private static void Capture(Window window, MainWindowSettings settings)
    {
        // Guarded: runs on PositionChanged/PropertyChanged on the Win32 path; it must never throw into layout.
        try
        {
            // Before the window is shown its position is not the OS's yet, and a minimised window has no meaningful bounds: nothing is copied then.
            // Full screen is never saved: the placement keeps the state and bounds from before it.
            if (!window.IsVisible || window.WindowState is WindowState.Minimized or WindowState.FullScreen) return;
            var saved = settings.Settings;
            saved.Maximised = window.WindowState == WindowState.Maximized;
            // A maximised window keeps the last normal bounds: its own bounds are the screen's.
            if (!saved.Maximised) CaptureNormalBounds(window, saved);
            settings.SaveWindowPlacement();
        }
        catch (Exception ex)
        {
            try { Trace.Error(Trace.Ui, "placement: capture: " + ex.Message); }
            catch (Exception) { } // swallow: the trace write itself failed
        }
    }

    private static void CaptureNormalBounds(Window window, AppSettings saved)
    {
        try
        {
            var bounds = window.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            saved.WindowWidth = bounds.Width;
            saved.WindowHeight = bounds.Height;
            saved.WindowLeft = window.Position.X;
            saved.WindowTop = window.Position.Y;
        }
        catch (Exception ex)
        {
            try { Trace.Error(Trace.Ui, "placement: capture: " + ex.Message); }
            catch (Exception) { } // swallow: the trace write itself failed
        }
    }
}
