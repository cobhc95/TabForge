using System;
using System.IO;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Avalonia side's refusal of a chosen file path before it is opened or written: a device form or a leaf that is a reparse point.
// Does not own: full path rules and atomic writes (FilePathPolicy), media approval and device forms (MediaPathPolicy).
// Tests: TabForge.Avalonia.Tests.MainWindowDocumentsTests.
public static class FilePathBoundary
{
    /// <summary>The reason a chosen path must not be opened or written, or null when it is allowed.</summary>
    public static string? RefusalReason(string path) =>
        MediaPathPolicy.IsDeviceForm(path) ? "a device path is not allowed"
        : IsLeafReparsePoint(path) ? "a linked file is not allowed"
        : null;

    // Only the leaf is checked: songs legitimately live under a junction higher in the path.
    private static bool IsLeafReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;   // not inspectable: the opener reports what it cannot read
        }
    }
}
