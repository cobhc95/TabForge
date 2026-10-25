using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TabForge.Platform.Windows;

// Owns: the Win32 call that reads a file's final path (GetFinalPathNameByHandleW), and the walk that finds the first link on a path
//   without following it (link flag read, target text read through the file system, never the target opened).
// Does not own: the \\?\ prefix handling or any trust decision (the callers keep those).
// Tests: TestPathResolverAdapter (full suite), TabForge.Avalonia.Tests.WindowsPathResolverTests, TabForge.Avalonia.Tests.MediaLinkJudgementTests.
/// <summary>
/// <see cref="IPathResolver"/> over Win32. Each component is checked top-down, so no link target is opened: a remote, removable or device
/// target is returned as text for the caller to judge. A link whose target text cannot be read, or that leads to a device form, raises
/// <see cref="UnresolvedLinkException"/>.
/// </summary>
public sealed class WindowsPathResolver : IPathResolver
{
    /// <summary>The longest path Windows reports (extended form).</summary>
    private const int BufferChars = 32_767;
    /// <summary>Links followed one after another before the path is judged unknown.</summary>
    private const int MaxLinkHops = 16;

    public string? FinalPath(string fullPath)
    {
        var current = fullPath;
        for (var hop = 0; hop <= MaxLinkHops; hop++)
        {
            if (IsDeviceForm(current)) throw new UnresolvedLinkException("the path is a device path");
            if (!IsLocalFixed(current)) return current;   // a network, removable or relative path is judged by the caller; nothing on it is touched here
            if (FirstLinkOn(current) is not { } link) return CanonicalPath(current);
            current = NextHop(link.Link, link.Remaining);
        }
        throw new UnresolvedLinkException("the link chain is longer than the adapter follows");
    }

    /// <summary>The first component that is a link, and the components after it; null when the path has no link.</summary>
    private static (string Link, string Remaining)? FirstLinkOn(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        var segments = path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var prefix = root;
        for (var i = 0; i < segments.Length; i++)
        {
            prefix = Path.Combine(prefix, segments[i]);
            // The attribute of this component alone: every component above it was checked (and was no link) on an earlier step.
            if ((File.GetAttributes(prefix) & FileAttributes.ReparsePoint) != 0) return (prefix, string.Join('\\', segments[(i + 1)..]));
        }
        return null;
    }

    private static string NextHop(string link, string remaining)
    {
        try { return Path.GetFullPath(Path.Combine(TargetOf(link), remaining)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new UnresolvedLinkException("the link's target is not a valid path", ex);
        }
    }

    /// <summary>The text a link points to, as the file system reads it (the link itself, not its target); relative targets are made absolute.</summary>
    private static string TargetOf(string link)
    {
        string? target;
        try { target = Directory.Exists(link) ? new DirectoryInfo(link).LinkTarget : new FileInfo(link).LinkTarget; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new UnresolvedLinkException("the link could not be read", ex); }
        if (string.IsNullOrEmpty(target)) throw new UnresolvedLinkException("this kind of link is not followed");
        if (!Path.IsPathFullyQualified(target)) target = Path.Combine(Path.GetDirectoryName(link) ?? "", target);
        if (!Path.IsPathFullyQualified(target) || IsDeviceForm(target)) throw new UnresolvedLinkException("the link leads to a device or volume path");
        return target;
    }

    /// <summary>
    /// The final path of a path the walk found no link on (GetFinalPathNameByHandleW); null when it does not fit the buffer. The file is opened
    /// with FILE_FLAG_OPEN_REPARSE_POINT, so a link that appears on the last component after the walk is opened as a link and refused. A link
    /// that appears on an earlier component is followed by the open, and the final path then reports where the file really is, which the caller judges.
    /// </summary>
    private static string? CanonicalPath(string path)
    {
        using var handle = OpenWithoutFollowingLast(path);
        if (!GetFileInformationByHandle(handle, out var information)) throw OpenFailure(Marshal.GetLastWin32Error(), path);
        if ((information.FileAttributes & FileAttributeReparsePoint) != 0) throw new UnresolvedLinkException("the file became a link while it was checked");
        var buffer = new char[BufferChars];
        var n = GetFinalPathNameByHandleW(handle.DangerousGetHandle(), buffer, (uint)buffer.Length, 0);
        return n == 0 || n >= buffer.Length ? null : new string(buffer, 0, (int)n);
    }

    /// <summary>Opens an existing file for reading without following a link on its last component; a directory is refused (no backup semantics).</summary>
    private static SafeFileHandle OpenWithoutFollowingLast(string path)
    {
        var handle = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw OpenFailure(Marshal.GetLastWin32Error(), path);
        return handle;
    }

    /// <summary>The exception for a failed open, the same kinds File.OpenHandle throws (callers catch IOException and UnauthorizedAccessException).</summary>
    private static Exception OpenFailure(int error, string path) => error switch
    {
        ErrorFileNotFound or ErrorPathNotFound => new FileNotFoundException("The file was not found.", path),
        ErrorAccessDenied => new UnauthorizedAccessException($"Access to '{path}' is denied."),
        _ => new IOException($"The file could not be opened (Windows error {error}).", error),
    };

    /// <summary>True for the \\.\, \\?\ and \??\ forms: such a path is never followed or judged by its text.</summary>
    private static bool IsDeviceForm(string path) =>
        path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal);

    /// <summary>True when the path is on a fixed drive (not a network, removable or relative path).</summary>
    private static bool IsLocalFixed(string path)
    {
        if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        try { return new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Fixed; }
        catch (ArgumentException) { return false; }   // not a drive: not judged as local
    }

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x1, FileShareWrite = 0x2, FileShareDelete = 0x4;
    private const uint OpenExisting = 3;
    /// <summary>Open the link itself, not its target, when the last component is a link.</summary>
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeReparsePoint = 0x400;
    private const int ErrorFileNotFound = 2, ErrorPathNotFound = 3, ErrorAccessDenied = 5;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr file, char[] buffer, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    /// <summary>BY_HANDLE_FILE_INFORMATION: only the attributes are read; the layout (52 bytes, 4-byte packing) must match the Win32 structure.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}
