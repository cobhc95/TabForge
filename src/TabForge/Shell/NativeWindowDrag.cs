using System.Runtime.InteropServices;
using System.Windows;

namespace TabForge.Shell;

// The few Win32 calls a tab drag between windows needs: the cursor, the left button and starting a real caption drag.
// Owns: the user32 declarations for caption drags (Aero Snap, double-click maximise and the system menu keep working through the non-client path).
// Does not own: which window moves or when (TabTransferController and its host).
// Tests: TestTabUi (the drag paths run through it).
internal static class NativeWindowDrag
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;
    private const int VK_LBUTTON = 0x01;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeScreenPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeScreenPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    /// <summary>True while the left mouse button is held.</summary>
    public static bool LeftButtonDown => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    public static bool TryGetCursor(out Point screen)
    {
        if (GetCursorPos(out var native)) { screen = new Point(native.X, native.Y); return true; }
        screen = default;
        return false;
    }

    public static void ReleaseMouseCapture() => ReleaseCapture();

    /// <summary>Starts a caption drag of the window <paramref name="hwnd"/> through the non-client path; returns when the drag ends.</summary>
    public static void BeginCaptionDrag(IntPtr hwnd) => SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
}
