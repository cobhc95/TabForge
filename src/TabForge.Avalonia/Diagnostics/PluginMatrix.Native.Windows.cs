using System;
using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the Win32 externs the matrix diagnostics call (the owner window and the message pump). They live in this
// Windows-adapter file so the portability guard does not count them; every call site is a Windows-only path
// (the owner half is inside its WINDOWS block, the pump checks OperatingSystem.IsWindows at run time).
// Does not own: the checks themselves (PluginMatrix and its check partials).
// Tests: none; a missing owner is recorded as a readable editor failure.
internal static class PluginMatrixNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMsg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowExW(int ex, string cls, string title, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out NativeMsg message, IntPtr window, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref NativeMsg message);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessageW(ref NativeMsg message);

    [DllImport("user32.dll")]
    internal static extern bool PeekMessageW(out NativeMsg message, IntPtr window, uint min, uint max, uint remove);
}
