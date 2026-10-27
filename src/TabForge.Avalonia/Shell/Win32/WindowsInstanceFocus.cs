using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: bringing the already running same-exe window to the front from a second launch (Windows foreground-lock rules: only the launching process may do it).
// Does not own: deciding who is the second instance (AvaloniaSingleInstance, lock file) or handing over a song (SingleInstanceService, pipe).
// Tests: none (needs a second visible window); the lock-file and hand-off rules are in SingleInstanceTests.
internal static class WindowsInstanceFocus
{
    private const int SwShow = 5;
    private const int SwRestore = 9;

    /// <summary>Restores and focuses the main window of another process running the same exe; best effort, never throws.</summary>
    internal static void ActivateExistingSameExeWindow(string exePath)
    {
        if (!OperatingSystem.IsWindows()) return;
        string full;
        try { full = Path.GetFullPath(exePath); }
        catch (Exception) { full = exePath; }
        Process current;
        try { current = Process.GetCurrentProcess(); }
        catch (Exception) { return; }
        Process[] peers;
        try { peers = Process.GetProcessesByName(current.ProcessName); }
        catch (Exception) { return; }
        foreach (var peer in peers)
        {
            try
            {
                if (peer.Id == current.Id) continue;
                string? other;
                try { other = peer.MainModule?.FileName; }
                catch (Exception) { continue; }
                if (other is null) continue;
                string otherFull;
                try { otherFull = Path.GetFullPath(other); }
                catch (Exception) { continue; }
                if (!string.Equals(otherFull, full, StringComparison.OrdinalIgnoreCase)) continue;
                var hwnd = peer.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;
                try
                {
                    AllowSetForegroundWindow(peer.Id);
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                    else ShowWindow(hwnd, SwShow);
                    SetForegroundWindow(hwnd);
                }
                catch (Exception) { } // swallow: best-effort activation of the running window
                break;
            }
            finally
            {
                try { peer.Dispose(); }
                catch (Exception) { } // swallow: best-effort dispose while enumerating peers
            }
        }
        try { current.Dispose(); }
        catch (Exception) { } // swallow: best-effort dispose after peer activation
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool IsIconic(IntPtr hWnd);
}
