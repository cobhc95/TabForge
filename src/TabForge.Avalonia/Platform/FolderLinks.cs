using System;
using System.IO;

namespace TabForge.Avalonia.Platform;

// Owns: making a link to a folder or file the way each OS allows without privilege: a junction on Windows, a symbolic link elsewhere.
// Does not own: the junction itself (Shell/Win32/WindowsFolderLink).
// Tests: SelfTestPairRecovery.
internal static class FolderLinks
{
    /// <summary>Makes <paramref name="link"/> a file link (a symbolic link, else on Windows a privilege-free reparse point); false when refused.</summary>
    internal static bool TryCreateFileLink(string link, string target)
    {
        try { File.CreateSymbolicLink(link, target); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // swallow: no right to make a symbolic link; Windows falls back to a reparse point
        return OperatingSystem.IsWindows() && Shell.Win32.WindowsFileReparse.TryCreate(link);
    }

    /// <summary>Creates <paramref name="link"/> pointing at <paramref name="target"/>; false when the system refused.</summary>
    internal static bool TryCreate(string link, string target)
    {
        if (OperatingSystem.IsWindows()) return Shell.Win32.WindowsFolderLink.TryCreate(link, target);
        try { Directory.CreateSymbolicLink(link, target); return Directory.Exists(link); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } // swallow: no permission to link here; the caller reports it
    }
}
