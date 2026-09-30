using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TabForge.Shell;

/// <summary>
/// One-time, per-window fix-ups applied to every WPF window when it loads:
/// a dark native title bar in dark themes, and a size/position that fits the monitor it opens on
/// (per-monitor DPI aware). Runs once on load and again only when the theme changes — no per-frame work.
/// </summary>
public static class WindowPolish
{
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window w) Apply(w); }));
        // Esc behaves like Cancel in every secondary window. Bubbling KeyDown, so an open dropdown
        // or an editing text box that uses Esc itself gets it first.
        EventManager.RegisterClassHandler(typeof(Window), UIElement.KeyDownEvent,
            new System.Windows.Input.KeyEventHandler(OnWindowKeyDown));
    }

    private static void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled || e.Key != System.Windows.Input.Key.Escape || sender is not Window window) return;
        if (window is TabForge.MainWindow) return; // the main window uses Esc for loop/playback/selection
        var cancel = FindCancelButton(window);
        if (cancel is { IsEnabled: true })
            cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, cancel));
        else
            window.Close(); // Closing handlers still get to confirm unsaved changes
        e.Handled = true;
    }

    private static System.Windows.Controls.Button? FindCancelButton(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is System.Windows.Controls.Button { IsCancel: true } b) return b;
            if (child is System.Windows.Controls.Button { Content: "Cancel" } named) return named;
            if (FindCancelButton(child) is { } found) return found;
        }
        return null;
    }

    public static void Apply(Window window)
    {
        ApplyTitleBarTheme(window);
        FitToMonitor(window);
    }

    /// <summary>Re-applies the title-bar colour to every open window (call after a theme change).</summary>
    public static void RefreshTitleBars()
    {
        if (Application.Current is null) return;
        foreach (Window window in Application.Current.Windows) ApplyTitleBarTheme(window);
    }

    private static bool IsDarkTheme()
    {
        if (Application.Current?.TryFindResource("WindowBrush") is not SolidColorBrush brush) return true;
        var c = brush.Color;
        return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B < 128;
    }

    public static void ApplyTitleBarTheme(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDarkTheme() ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+); 19 on earlier builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        // Title-bar colour: match the app's chrome strip on Windows 11 (ignored where unsupported).
        if (Application.Current?.TryFindResource("ChromeStripBrush") is SolidColorBrush chrome)
        {
            var c = chrome.Color;
            var colorRef = c.R | (c.G << 8) | (c.B << 16);
            DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int)); // DWMWA_CAPTION_COLOR
        }
    }

    /// <summary>
    /// Keeps a normal (not maximised) window inside the work area of the monitor it is on, in that
    /// monitor's DIPs. Windows that are too tall get a max size so their content scrolls instead.
    /// </summary>
    private static void FitToMonitor(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.WindowState != WindowState.Normal) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null) return;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
        var work = new Rect(topLeft, bottomRight);
        if (work.Width <= 0 || work.Height <= 0) return;

        const double margin = 8;
        var maxWidth = Math.Max(200, work.Width - margin * 2);
        var maxHeight = Math.Max(160, work.Height - margin * 2);
        // A permanent Max size on a resizable window also caps it when maximised, leaving a strip of
        // desktop along the right and bottom edges. Only fixed-size windows get the cap; resizable ones
        // are just brought to fit (below) and may later use the whole work area.
        var resizable = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        if (!resizable && window.MaxWidth > maxWidth) window.MaxWidth = maxWidth;
        if (!resizable && window.MaxHeight > maxHeight) window.MaxHeight = maxHeight;
        if (window.MinWidth > maxWidth) window.MinWidth = maxWidth;
        if (window.MinHeight > maxHeight) window.MinHeight = maxHeight;
        if (window.ActualWidth > maxWidth) window.Width = maxWidth;
        if (window.ActualHeight > maxHeight) window.Height = maxHeight;

        // Pull it back on-screen if it hangs over an edge.
        var width = Math.Min(window.ActualWidth, maxWidth);
        var height = Math.Min(window.ActualHeight, maxHeight);
        if (!double.IsFinite(window.Left) || !double.IsFinite(window.Top)) return;
        var left = Math.Clamp(window.Left, work.Left + margin, Math.Max(work.Left + margin, work.Right - margin - width));
        var top = Math.Clamp(window.Top, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - margin - height));
        if (Math.Abs(left - window.Left) > 0.5) window.Left = left;
        if (Math.Abs(top - window.Top) > 0.5) window.Top = top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
