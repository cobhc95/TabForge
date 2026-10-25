using System.Runtime.InteropServices;

namespace ParityRecorder;

/// <summary>
/// The Win32 calls the recorder makes: window messages to the app's own windows, window rectangles and DPI.
/// Owns: the constants and P/Invoke signatures. Does not own: what the messages mean (HitMap, Scenarios).
/// No real mouse or keyboard input is synthesised; every key or wheel event is a message to one window.
/// </summary>
internal static class Native
{
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint SC_CLOSE = 0xF060;
    public const uint SC_RESTORE = 0xF120;
    public const int MK_CONTROL = 0x0008;
    public const int VK_TAB = 0x09;
    public const int ScanTab = 0x0F;

    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const int SendTimeoutMs = 2000;

    /// <summary>Per-monitor DPI awareness v2, so window rectangles and UIA bounds are in physical pixels.</summary>
    public static readonly IntPtr PerMonitorV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativePoint point);

    /// <summary>Moves the real pointer (live runs only). No button is pressed.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X, Y;
    }

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    /// <summary>Visible state from the window's own flags: "Maximized", "Minimized" or "Normal".</summary>
    public static string StateOf(IntPtr hWnd) => IsIconic(hWnd) ? "Minimized" : IsZoomed(hWnd) ? "Maximized" : "Normal";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>Packs two signed 16-bit values the way MAKELPARAM does (screen coordinates may be negative when parked off-screen).</summary>
    public static IntPtr Pack(int low, int high) => (IntPtr)(((high & 0xFFFF) << 16) | (low & 0xFFFF));

    /// <summary>Sends a message and waits at most two seconds, so an unresponsive window cannot hang the recorder.</summary>
    public static bool Send(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result) =>
        SendMessageTimeout(hwnd, msg, wParam, lParam, SMTO_ABORTIFHUNG, SendTimeoutMs, out result) != IntPtr.Zero;
}
