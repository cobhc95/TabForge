using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the single-instance lock for UI launches: an exclusive lock file per exe path under UserPaths (FileShare.None, held for the process
// lifetime, released by the OS when the process dies, so a crash leaves a stale file but never a stale lock). The second UI launch of the same
// exe asks the running window to come forward and exits 0 before Avalonia starts. Helpers bypass; any lock failure proceeds to a normal start.
// Does not own: the song hand-off (SingleInstanceService, named pipe), the Windows focus call (Shell/Win32/WindowsInstanceFocus), the helpers (Program) or the app start (App).
// Tests: SingleInstanceTests, UnifiedCrashRoundOneTests.
/// <summary>Guards UI launches so only one window per exe path runs.</summary>
internal static class AvaloniaSingleInstance
{
    private static FileStream? _held;

    /// <summary>True for helper starts that must bypass the lock (no window).</summary>
    internal static bool IsHelperArgs(string[] args)
    {
        if (args.Length == 0) return false;
        var first = args[0];
        return string.Equals(first, "--audio-engine", StringComparison.Ordinal)
            || string.Equals(first, "--plugin-host", StringComparison.Ordinal)
            || string.Equals(first, "--plugin-info", StringComparison.Ordinal)
            || string.Equals(first, global::TabForge.Services.ImportWorker.Argument, StringComparison.Ordinal);
    }

    /// <summary>The lock file name for an exe path: same exe shares one name, different paths do not.</summary>
    internal static string LockNameFor(string exePath)
    {
        var normalized = PathComparer.Comparison == StringComparison.Ordinal ? exePath : exePath.ToLowerInvariant();   // a case-sensitive file system keeps case
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return "TabForge.Avalonia." + hash + ".lock";
    }

    /// <summary>
    /// Takes the exclusive lock at <paramref name="lockPath"/>. True with the open stream when this process now owns it (a leftover file from a crashed
    /// run is simply opened again); false when another live process holds it. Any other failure (unwritable folder) throws so the caller proceeds.
    /// </summary>
    internal static bool TryAcquire(string lockPath, out FileStream? held)
    {
        held = null;
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try
        {
            held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
            return true;
        }
        catch (IOException) when (File.Exists(lockPath)) { return false; } // the file is there and cannot be opened exclusively: a live instance holds it
    }

    /// <summary>
    /// True when this UI launch is the second instance: the existing same-exe window was asked to come forward
    /// and the caller must exit 0 before Avalonia starts. False means proceed (first instance, helper, or any failure).
    /// </summary>
    internal static bool IsSecondInstance(string[] args)
    {
        if (IsHelperArgs(args)) return false;
        try
        {
            var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            if (TryAcquire(Path.Combine(UserPaths.Local, LockNameFor(exePath)), out var stream))
            {
                _held = stream;
                return false;
            }
            try { if (OperatingSystem.IsWindows()) Shell.Win32.WindowsInstanceFocus.ActivateExistingSameExeWindow(exePath); }
            catch (Exception) { } // swallow: best-effort activation of the running window
            return true;
        }
        catch (Exception)
        {
            return false; // a lock that cannot be created must never stop the app
        }
    }
}
