using System;
using System.Diagnostics;
using System.IO;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: creating a folder junction on Windows (needs no privilege, unlike a symbolic link without Developer Mode).
// Does not own: the portable choice between junction and symbolic link (Platform/FolderLinks).
// Tests: SelfTestPairRecovery (the folder-link case).
internal static class WindowsFolderLink
{
    /// <summary>Makes <paramref name="link"/> a junction to <paramref name="target"/> (cmd mklink /J); false when it could not be made.</summary>
    internal static bool TryCreate(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, target }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        if (mklink is null) return false;
        if (!mklink.WaitForExit(15000)) { try { mklink.Kill(); } catch (InvalidOperationException) { } return false; } // swallow: the helper process may already have exited
        return mklink.ExitCode == 0 && Directory.Exists(link);
    }
}
