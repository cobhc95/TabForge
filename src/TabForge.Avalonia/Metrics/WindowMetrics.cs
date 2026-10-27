// Owns: the one window-size entry point: the named window kinds and the default, minimum and clamped sizes on the
//   owner screen working area, plus the mostly-on-screen check and the CenterOwner startup location.
// Does not own: the size maths (WindowUiScale), the placement check (MainWindowPlacement), the settings-window and song-info
//   sizes (ChromeMetrics), the small-dialog width (DialogMetrics), or a window own minimums (its code).
// Tests: WindowMetricsTests.
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia;

/// <summary>
/// A named window size in inner DIPs with its minimum. Settings and SongInfo read their owners; SmallDialog reads the
/// dialog width and sizes its height to the content, so its heights are zero and unused; later batches add their kinds
/// when they delete their literals.
/// </summary>
public sealed record WindowKind(double Width, double Height, double MinWidth, double MinHeight)
{
    /// <summary>The Settings window: 1040 by 690, minimum 834 by 597.</summary>
    public static WindowKind Settings { get; } = new WindowKind(
        ChromeMetrics.SettingsWindowWidth, ChromeMetrics.SettingsWindowHeight,
        ChromeMetrics.SettingsWindowMinWidth, ChromeMetrics.SettingsWindowMinHeight);

    /// <summary>The song info window: 660 by 600, minimum 540 by 460.</summary>
    public static WindowKind SongInfo { get; } = new WindowKind(
        ChromeMetrics.SongInfoWindowWidth, ChromeMetrics.SongInfoWindowHeight,
        ChromeMetrics.SongInfoWindowMinWidth, ChromeMetrics.SongInfoWindowMinHeight);

    /// <summary>A small dialog: 420 wide, minimum 420 wide, height sized to the content.</summary>
    public static WindowKind SmallDialog { get; } = new WindowKind(
        DialogMetrics.SmallDialogWidth, 0, DialogMetrics.SmallDialogWidth, 0);

    /// <summary>A caller-owned size, for the batches that add their kinds.</summary>
    public static WindowKind Custom(double width, double height, double minWidth, double minHeight) =>
        new WindowKind(width, height, minWidth, minHeight);
}

/// <summary>
/// The one window-size entry point. The Window overloads delegate to WindowUiScale on the owner screen; the Size
/// overloads take an explicit working area so tests pin the numbers without a screen. The default deliberately does not
/// raise to the minimum (it matches the helper today); callers set the minimum separately. Nothing in the app calls it yet.
/// </summary>
public static class WindowMetrics
{
    /// <summary>The size a window of the kind opens at on its screen: the preferred size at the clamped scale, capped to the working area.</summary>
    public static Size Default(Window window, WindowKind kind, double uiScale) =>
        WindowUiScale.DefaultSize(window, UiDensity.ClampScale(uiScale), kind.Width, kind.Height);

    /// <summary>The same default with an explicit working area, so tests pin the numbers without a screen.</summary>
    public static Size Default(Size workingArea, WindowKind kind, double uiScale) =>
        WindowUiScale.DefaultSize(workingArea, UiDensity.ClampScale(uiScale), kind.Width, kind.Height);

    /// <summary>The minimum a window of the kind opens at on its screen: the scaled minimum, winning only inside the working area.</summary>
    public static Size Min(Window window, WindowKind kind, double uiScale)
    {
        var scale = UiDensity.ClampScale(uiScale);
        return WindowUiScale.ClampRememberedSize(
            new Size(kind.Width * scale, kind.Height * scale),
            new Size(kind.MinWidth * scale, kind.MinHeight * scale),
            window);
    }

    /// <summary>The same minimum with an explicit working area, so tests pin the numbers without a screen.</summary>
    public static Size Min(Size workingArea, WindowKind kind, double uiScale)
    {
        var scale = UiDensity.ClampScale(uiScale);
        return WindowUiScale.ClampRememberedSize(
            new Size(kind.Width * scale, kind.Height * scale),
            new Size(kind.MinWidth * scale, kind.MinHeight * scale),
            workingArea);
    }

    /// <summary>The inner size as outer DIPs at the clamped scale, never larger than the owner screen working area.</summary>
    public static Size Clamp(Window window, Size inner, double uiScale) =>
        WindowUiScale.ClampToWorkingArea(WindowUiScale.ToOuter(inner, UiDensity.ClampScale(uiScale)), window);

    /// <summary>The same clamp with an explicit working area, so tests pin the numbers without a screen.</summary>
    public static Size Clamp(Size outer, Size workingArea) =>
        WindowUiScale.ClampToWorkingArea(outer, workingArea);

    /// <summary>True when the window at the origin with its current size lies mostly on one screen working area.</summary>
    public static bool IsMostlyOnScreen(Window window, PixelPoint origin) =>
        MainWindowPlacement.IsMostlyOnScreen(window.Screens.All, origin, window.Width, window.Height);

    /// <summary>Centres the window on its owner. A null owner leaves ownership alone; call before showing the window.</summary>
    public static void CenterOwner(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    /// <summary>The owner screen working area in DIPs, or null when the window has no screen yet.</summary>
    public static Size? WorkingArea(Window window) => WindowUiScale.WorkingArea(window);

    private const string DesignedMinWidthKey = "TabForge.DesignedMinWidth";   // remembered on the window's own resources, so no static table
    private static double DesignedMinWidth(Window w)
    {
        if (w.Resources.TryGetValue(DesignedMinWidthKey, out var stored) && stored is double width) return width;
        w.Resources[DesignedMinWidthKey] = w.MinWidth;
        return w.MinWidth;
    }

    /// <summary>Keeps <paramref name="window"/> within its screen working area, so the title buttons are never pushed off the edge.</summary>
    public static void FitToScreen(Window window)
    {
        if (window.Screens.ScreenFromWindow(window) is not { } screen) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var max = area.Width / scale;
        var maxHeight = area.Height / scale;
        var designed = DesignedMinWidth(window);   // the MinWidth the window was built with
        window.MinWidth = max < designed ? max : designed;   // lowered only while the screen is narrower than it, restored otherwise
        if (window.Width > max) window.Width = max;
        if (window.Height > maxHeight) window.Height = maxHeight;
        var x = window.Position.X; var y = window.Position.Y;
        if (x + window.Width * scale > area.Right) x = System.Math.Max(area.X, (int)(area.Right - window.Width * scale));
        if (y + window.Height * scale > area.Bottom) y = System.Math.Max(area.Y, (int)(area.Bottom - window.Height * scale));
        if (x != window.Position.X || y != window.Position.Y) window.Position = new PixelPoint(x, y);
    }
}
