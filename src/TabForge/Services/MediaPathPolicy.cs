using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using TabForge.Audio;

namespace TabForge.Services;

/// <summary>Where a project-referenced media file lives.</summary>
public enum MediaLocation
{
    /// <summary>A local fixed drive.</summary>
    Local,
    /// <summary>A removable, optical or unknown drive.</summary>
    Removable,
    /// <summary>A UNC path or a mapped network drive.</summary>
    Network,
    /// <summary>A device path, a \\?\ form, a reserved device name or an alternate data stream: never opened.</summary>
    Device,
    /// <summary>Empty, malformed, relative without a project folder, or not an audio file type: never opened.</summary>
    Invalid,
}

/// <summary>The classification of one media path (see <see cref="MediaPathPolicy"/>).</summary>
/// <param name="FullPath">Normalised absolute path (project-relative paths resolved), links resolved when the file is local.</param>
/// <param name="InProject">Inside the folder the project was opened from.</param>
/// <param name="Problem">Why the path is refused (Device / Invalid only).</param>
public sealed record MediaVerdict(MediaLocation Location, string FullPath, bool InProject, string? Problem)
{
    public bool Refused => Location is MediaLocation.Device or MediaLocation.Invalid;
    public bool Remote => Location is MediaLocation.Network or MediaLocation.Removable;
    /// <summary>The folder an approval covers.</summary>
    public string Folder => FullPath.Length == 0 ? "" : Path.GetDirectoryName(FullPath) ?? "";
}

/// <summary>The file-system questions the policy asks; tests replace it to prove that network paths are never touched.</summary>
public interface IMediaFileSystem
{
    DriveType DriveTypeOf(string root);
    /// <summary>The final path after every link and junction, or null when the file does not exist or cannot be resolved. Only called for local fixed-drive paths.</summary>
    string? FinalPathOf(string fullPath);
}

internal sealed class RealMediaFileSystem : IMediaFileSystem
{
    private static readonly ConcurrentDictionary<string, DriveType> Drives = new(StringComparer.OrdinalIgnoreCase);

    public DriveType DriveTypeOf(string root) => Drives.GetOrAdd(root, r =>
    {
        try { return new DriveInfo(r).DriveType; }
        catch (Exception) { return DriveType.Unknown; }
    });

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr file, char[] buffer, uint length, uint flags);

    public string? FinalPathOf(string fullPath)
    {
        try
        {
            using var handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new char[1024];
            var n = GetFinalPathNameByHandleW(handle.DangerousGetHandle(), buffer, (uint)buffer.Length, 0);
            return n == 0 || n >= buffer.Length ? null : new string(buffer, 0, (int)n);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}

/// <summary>
/// Classifies a path a project names for audio (clips): local / removable / network / device. Device paths, \\?\ forms,
/// reserved device names, alternate data streams and non-audio extensions are refused; remote paths (and links that lead
/// there) need the user's approval (<see cref="MediaAccess"/>). Nothing on a remote path is touched to classify it.
/// </summary>
public static class MediaPathPolicy
{
    internal static IMediaFileSystem FileSystem { get; set; } = new RealMediaFileSystem();

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsAudioExtension(string path) =>
        WaveformCache.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>True for the \\.\, \\?\, \??\ and //./ forms (and GLOBALROOT): such a path is never opened.</summary>
    public static bool IsDeviceForm(string path)
    {
        var p = path.TrimStart().Replace('/', '\\');
        return p.StartsWith(@"\\.\", StringComparison.Ordinal) || p.StartsWith(@"\\?\", StringComparison.Ordinal)
            || p.StartsWith(@"\??\", StringComparison.Ordinal) || p.Contains("GLOBALROOT", StringComparison.OrdinalIgnoreCase);
    }

    public static MediaVerdict Classify(string? path, string? projectFolder) => Classify(path, projectFolder, requireAudio: true);

    /// <summary>As <see cref="Classify(string?, string?)"/>; <paramref name="requireAudio"/> false judges any file type the same way
    /// (a dropped MIDI file: device forms refused, network and removable places told apart, links followed).</summary>
    public static MediaVerdict Classify(string? path, string? projectFolder, bool requireAudio)
    {
        static MediaVerdict Bad(MediaLocation l, string why) => new(l, "", false, why);
        if (string.IsNullOrWhiteSpace(path)) return Bad(MediaLocation.Invalid, "no file path");
        if (path.Length > InputLimits.MaxPathLength) return Bad(MediaLocation.Invalid, "the path is too long");
        if (path.Any(c => c < 32)) return Bad(MediaLocation.Invalid, "the path has control characters");
        if (IsDeviceForm(path)) return Bad(MediaLocation.Device, "device paths are never opened");
        var p = path.Trim().Replace('/', '\\');
        // Alternate data streams and "C:name" (drive-relative): a colon is only allowed as the drive letter's.
        var afterDrive = p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':' ? p[2..] : p;
        if (afterDrive.Contains(':')) return Bad(MediaLocation.Invalid, "the path has a stream or drive-relative form");
        if (!Path.IsPathFullyQualified(p))
        {
            if (p.StartsWith('\\') || string.IsNullOrWhiteSpace(projectFolder)) return Bad(MediaLocation.Invalid, "a relative path needs a saved project folder");
            p = Path.Combine(projectFolder, p);
        }
        string full;
        try { full = Path.GetFullPath(p); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        { return Bad(MediaLocation.Invalid, "the path is not valid"); }
        if (IsDeviceForm(full)) return Bad(MediaLocation.Device, "device paths are never opened");
        foreach (var segment in full.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = segment.TrimEnd('.', ' ');
            var dot = stem.IndexOf('.');
            if (Reserved.Contains(dot >= 0 ? stem[..dot] : stem)) return Bad(MediaLocation.Device, "a reserved device name in the path");
        }
        if (requireAudio && !IsAudioExtension(full)) return Bad(MediaLocation.Invalid, "not an audio file type");

        var folder = string.IsNullOrWhiteSpace(projectFolder) ? null : Normalize(projectFolder);
        var location = LocationOf(full);
        // A local file may be a link to somewhere else: judge the place it really leads to.
        if (location == MediaLocation.Local && FileSystem.FinalPathOf(full) is { } final)
        {
            if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
            else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
            if (IsDeviceForm(final)) return Bad(MediaLocation.Device, "the file leads to a device path");
            var resolved = Normalize(final);
            if (!resolved.Equals(full, StringComparison.OrdinalIgnoreCase)) { full = resolved; location = LocationOf(full); }
        }
        return new MediaVerdict(location, full, folder is not null && IsInside(full, folder), null);
    }

    private static MediaLocation LocationOf(string full)
    {
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return MediaLocation.Network;
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return MediaLocation.Invalid;
        return FileSystem.DriveTypeOf(root) switch
        {
            DriveType.Fixed => MediaLocation.Local,
            DriveType.Network => MediaLocation.Network,
            _ => MediaLocation.Removable,   // removable, optical, RAM disk, unknown: approval first
        };
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { path = Path.GetFullPath(path.Trim()); } catch (Exception) { return path.Trim().Replace('/', '\\'); }
        var root = Path.GetPathRoot(path) ?? "";
        while (path.Length > root.Length && (path[^1] is '\\' or '/')) path = path[..^1];
        return path;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies below it.</summary>
    public static bool IsInside(string path, string folder)
    {
        var f = folder.TrimEnd('\\') + "\\";
        return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A folder the user allowed a project to read linked audio from (network or removable).</summary>
public sealed class MediaApproval
{
    /// <summary>The project's file path (empty for a song that was never saved).</summary>
    public string Project { get; set; } = "";
    public string Folder { get; set; } = "";
}

/// <summary>What may be done with one linked file.</summary>
public enum MediaAccessState { Allowed, NeedsApproval, Refused }

public sealed record MediaDecision(MediaAccessState State, MediaVerdict Verdict, string Message)
{
    public bool Allowed => State == MediaAccessState.Allowed;
}

