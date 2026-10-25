using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TabForge.Avalonia.Platform;

// Owns: the single-instance named mutex for UI launches: the second UI launch of the same exe activates the
// existing window and exits 0 before Avalonia starts. Helpers bypass; an abandoned mutex counts as owned;
// any mutex failure proceeds to a normal start.
// Does not own: song handover (none: just activation), the helpers themselves (Program) or the app start (App).
// Tests: none dedicated(name/helper rules; no second process started).
/// <summary>Guards UI launches so only one window per exe path runs.</summary>
internal static class AvaloniaSingleInstance
{
    private static Mutex? _held;

    /// <summary>True for helper starts that must bypass the mutex (no window).</summary>
    internal static bool IsHelperArgs(string[] args)
    {
        if (args.Length == 0) return false;
        var first = args[0];
        return string.Equals(first, "--audio-engine", StringComparison.Ordinal)
            || string.Equals(first, "--plugin-host", StringComparison.Ordinal)
            || string.Equals(first, "--plugin-info", StringComparison.Ordinal)
            || string.Equals(first, global::TabForge.Services.ImportWorker.Argument, StringComparison.Ordinal);
    }

    /// <summary>The mutex name for an exe path: same exe shares one name, different paths do not.</summary>
    internal static string MutexNameFor(string exePath)
    {
        var normalized = exePath.ToLowerInvariant();
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized)))[..16];
        return "TabForge.Avalonia." + hash;
    }

    /// <summary>
    /// True when this UI launch is the second instance: the existing same-exe window was asked to come forward
    /// and the caller must exit 0 before Avalonia starts. False means proceed (first instance, helper, or any failure).
    /// </summary>
    internal static bool IsSecondInstance(string[] args)
    {
        if (IsHelperArgs(args)) return false;
        string exePath;
        try { exePath = Environment.ProcessPath ?? AppContext.BaseDirectory; }
        catch (Exception) { return false; }
        string name;
        try { name = MutexNameFor(exePath); }
        catch (Exception) { return false; }
        Mutex? candidate = null;
        try
        {
            candidate = new Mutex(false, name);
            bool owned;
            try { owned = candidate.WaitOne(0, false); }
            catch (AbandonedMutexException) { owned = true; } // abandoned = owned
            if (!owned)
            {
                try { ActivateExistingSameExeWindow(exePath); }
                catch (Exception) { } // swallow: best-effort activation of the running window
                try { candidate.Dispose(); }
                catch (Exception) { } // swallow: best-effort dispose; a second launch still exits
                return true;
            }
            _held = candidate;
            candidate = null;
            return false;
        }
        catch (Exception)
        {
            // Failures proceed: a mutex that cannot be created/waited must never stop the app.
            try { candidate?.Dispose(); }
            catch (Exception) { } // swallow: best-effort dispose; failures proceed with a new window
            return false;
        }
    }

    private static void ActivateExistingSameExeWindow(string exePath)
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

    private const int SwShow = 5;
    private const int SwRestore = 9;

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool IsIconic(IntPtr hWnd);
}
