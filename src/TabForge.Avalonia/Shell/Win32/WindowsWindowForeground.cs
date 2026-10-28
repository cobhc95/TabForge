using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: asking Windows to bring one of our own windows to the foreground when Avalonia's Activate() was not enough (live video recording).
// Does not own: deciding when to bring it forward (LiveVideoFocus) or restoring a minimised window (the video host).
// Tests: none (needs a visible window); the decision rules are in LiveVideoFocus tests.
internal static class WindowsWindowForeground
{
    /// <summary>Best effort: a Win32 foreground request for the window; does nothing off Windows or without a handle.</summary>
    internal static void Request(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (window.TryGetPlatformHandle() is { } handle) SetForegroundWindow(handle.Handle);
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
