using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TabForge.Docking;

/// <summary>Keeps a floating dock window finite and inside the work area of the monitor it is on.</summary>
internal static class FloatingWindowPlacement
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    internal static void ClampBounds(Window window, DockFloatingState state)
    {
        // Keep finite, usable DIP values until the HWND is available. The actual target monitor's
        // pixel work area is applied immediately after Show, which also handles secondary displays.
        window.Width = Math.Max(Math.Min(240, window.MinWidth),
            double.IsFinite(state.Width) ? state.Width : Math.Max(320, window.MinWidth));
        window.Height = Math.Max(Math.Min(150, window.MinHeight),
            double.IsFinite(state.Height) ? state.Height : Math.Max(220, window.MinHeight));
        window.Left = double.IsFinite(state.Left) ? state.Left : 180;
        window.Top = double.IsFinite(state.Top) ? state.Top : 140;
    }

    internal static void ClampToMonitorWorkArea(Window window, DockFloatingState state)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var work = info.WorkArea;
        var workWidth = Math.Max(1, work.Right - work.Left);
        var workHeight = Math.Max(1, work.Bottom - work.Top);
        var source = PresentationSource.FromVisual(window);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice;
        var workDip = fromDevice.HasValue
            ? fromDevice.Value.Transform(new Vector(workWidth, workHeight))
            : new Vector(workWidth, workHeight);

        // Window.Width/Height exclude the non-client frame. Leave a small frame allowance so the
        // outer rectangle fits the work area while respecting panel minimums whenever possible.
        const double frameAllowanceDip = 20;
        var maxWidth = Math.Max(180, workDip.X - frameAllowanceDip);
        var maxHeight = Math.Max(120, workDip.Y - frameAllowanceDip);
        window.MinWidth = Math.Min(window.MinWidth, maxWidth);
        window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        if (window.Width > maxWidth) window.Width = maxWidth;
        if (window.Height > maxHeight) window.Height = maxHeight;
        window.UpdateLayout();

        if (!GetWindowRect(handle, out bounds)) return;
        var width = Math.Min(bounds.Right - bounds.Left, workWidth);
        var height = Math.Min(bounds.Bottom - bounds.Top, workHeight);
        var left = Math.Clamp(bounds.Left, work.Left, work.Right - width);
        var top = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
        if (left != bounds.Left || top != bounds.Top || width != bounds.Right - bounds.Left || height != bounds.Bottom - bounds.Top)
            SetWindowPos(handle, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);

        // WM_WINDOWPOSCHANGED updates WPF's DIP location; copy that normalized geometry for saves.
        state.Left = window.Left;
        state.Top = window.Top;
        state.Width = window.Width;
        state.Height = window.Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);
}
