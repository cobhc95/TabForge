using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace TabForge.Shell;

/// <summary>
/// Ensures a borderless WPF window exposes native resize hit targets on every edge and corner.
/// Windows then supplies the resize cursor and drag behaviour as it does for a standard frame.
/// </summary>
internal sealed class WpfResizeBorderFrame : IDisposable
{
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private readonly Window _window;
    private HwndSource? _source;

    public WpfResizeBorderFrame(Window window)
    {
        _window = window;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) Attach(handle);
        else window.SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        Attach(new WindowInteropHelper(_window).Handle);

    private void Attach(IntPtr handle)
    {
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);
        _window.StateChanged += (_, _) => UpdateMaximisedInset();
        _window.LocationChanged += (_, _) => UpdateMaximisedInset();
        _window.SizeChanged += (_, _) => UpdateMaximisedInset();
        _window.DpiChanged += (_, _) => UpdateMaximisedInset();
        UpdateMaximisedInset();
    }

    private Thickness? _originalMargin;

    /// <summary>
    /// A maximised borderless window can extend past the monitor's work area (under the taskbar, or by
    /// the hidden frame overhang). Pad the content by exactly that overlap so the status bar and the
    /// scroll bars stay visible above the taskbar; nothing changes for a normal window.
    /// </summary>
    private void UpdateMaximisedInset()
    {
        if (_window.Content is not FrameworkElement root) return;
        _originalMargin ??= root.Margin;
        var inset = new Thickness();
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (_window.WindowState == WindowState.Maximized && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var bounds))
        {
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var dpi = VisualTreeHelper.GetDpi(_window);
                var work = info.WorkArea;
                inset = new Thickness(
                    Math.Max(0, work.Left - bounds.Left) / dpi.DpiScaleX,
                    Math.Max(0, work.Top - bounds.Top) / dpi.DpiScaleY,
                    Math.Max(0, bounds.Right - work.Right) / dpi.DpiScaleX,
                    Math.Max(0, bounds.Bottom - work.Bottom) / dpi.DpiScaleY);
            }
        }
        var baseMargin = _originalMargin.Value;
        var wanted = new Thickness(baseMargin.Left + inset.Left, baseMargin.Top + inset.Top,
            baseMargin.Right + inset.Right, baseMargin.Bottom + inset.Bottom);
        if (root.Margin != wanted) root.Margin = wanted;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;
    }

    private const int WmWindowPosChanging = 0x0046;
    private const int SwpNoSize = 0x0001, SwpNoMove = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos { public IntPtr Hwnd, InsertAfter; public int X, Y, Cx, Cy, Flags; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

    /// <summary>
    /// Maximised borderless windows can be given the wrong size by Windows (the primary monitor's size, or the
    /// whole monitor including the taskbar), which cut the status bar and track list off on some screens.
    /// While maximised, the window is placed exactly on the work area of the monitor it is on.
    /// </summary>
    private static void FitMaximisedToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        if (!IsZoomed(hwnd)) return;
        var pos = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((pos.Flags & SwpNoSize) != 0 && (pos.Flags & SwpNoMove) != 0) return;
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
        var work = info.WorkArea;
        pos.X = work.Left; pos.Y = work.Top;
        pos.Cx = work.Right - work.Left; pos.Cy = work.Bottom - work.Top;
        pos.Flags &= ~(SwpNoSize | SwpNoMove);
        Marshal.StructureToPtr(pos, lParam, false);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmWindowPosChanging)
        {
            FitMaximisedToWorkArea(hwnd, lParam);
            return IntPtr.Zero;
        }
        // WindowChrome owns maximized work-area bounds and DPI/frame compensation. Do not
        // override WM_GETMINMAXINFO here: doing so bypasses its native frame adjustments and can
        // leave an inset along the bottom/right edges. This hook only supplies resize hit targets.
        if (message != WmNcHitTest || !_window.IsVisible ||
            _window.WindowState != WindowState.Normal || _window.ResizeMode != ResizeMode.CanResize)
            return IntPtr.Zero;

        try
        {
            if (!GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var bounds))
                return IntPtr.Zero;

            var chrome = WindowChrome.GetWindowChrome(_window);
            var border = chrome?.ResizeBorderThickness ?? new Thickness(6);
            var dpi = VisualTreeHelper.GetDpi(_window);
            var left = Math.Max(1, border.Left * dpi.DpiScaleX);
            var right = Math.Max(1, border.Right * dpi.DpiScaleX);
            var top = Math.Max(1, border.Top * dpi.DpiScaleY);
            var bottom = Math.Max(1, border.Bottom * dpi.DpiScaleY);
            var onLeft = cursor.X < bounds.Left + left;
            var onRight = cursor.X >= bounds.Right - right;
            var onTop = cursor.Y < bounds.Top + top;
            var onBottom = cursor.Y >= bounds.Bottom - bottom;

            var hit = (onTop, onBottom, onLeft, onRight) switch
            {
                (true, _, true, _) => HtTopLeft,
                (true, _, _, true) => HtTopRight,
                (_, true, true, _) => HtBottomLeft,
                (_, true, _, true) => HtBottomRight,
                (true, _, _, _) => HtTop,
                (_, true, _, _) => HtBottom,
                (_, _, true, _) => HtLeft,
                (_, _, _, true) => HtRight,
                _ => 0
            };

            if (hit == 0) return IntPtr.Zero;
            handled = true;
            return new IntPtr(hit);
        }
        catch
        {
            // Leave WPF's built-in WindowChrome hit testing in charge if the frame is transitioning.
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WindowProc);
        _source = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
}
