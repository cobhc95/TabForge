using System.Collections.Concurrent;
using System.IO;
using TabForge.Audio;
using TabForge.Platform;

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
    /// <summary>A relative path that leads out of the project folder (through ".."): never trusted as the project's own, so the user approves it.</summary>
    public bool EscapesProject { get; init; }
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
    // Cache: key = drive root (a handful of roots), no ceiling, thread-safe (any thread may classify a path).
    private static readonly ConcurrentDictionary<string, DriveType> Drives = new(StringComparer.OrdinalIgnoreCase);

    private readonly IPathResolver? _paths;

    /// <param name="paths">The final-path adapter the app passes in; null means none was set up, and a link probe then throws.</param>
    public RealMediaFileSystem(IPathResolver? paths) => _paths = paths;

    public DriveType DriveTypeOf(string root) => Drives.GetOrAdd(root, r =>
    {
        try { return new DriveInfo(r).DriveType; }
        catch (Exception) { return DriveType.Unknown; } // Not logged: drive type probe: unknown
    });

    public string? FinalPathOf(string fullPath)
    {
        if (_paths is null) throw new InvalidOperationException("No path resolver is set up: the app passes one in at startup (Program.Main).");
        try { return _paths.FinalPath(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; } // Not logged: final path probe: null means unknown
    }
}

// Owns: classifying a path a project names for audio as local, removable, network or device.
// Does not own: the approval state and the file opening.
// Tests: no dedicated test class.
/// <summary>
/// Classifies a path a project names for audio (clips): local / removable / network / device. Device paths, \\?\ forms,
/// reserved device names, alternate data streams and non-audio extensions are refused; remote paths (and links that lead
/// there) need the user's approval (<see cref="MediaAccess"/>). Nothing on a remote path is touched to classify it.
/// </summary>
public static class MediaPathPolicy
{
    /// <summary>The file-system questions; the app sets the real one at startup (Program.Main), tests replace it.</summary>
    internal static IMediaFileSystem FileSystem { get; set; } = new RealMediaFileSystem(null);

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
        var relative = !Path.IsPathFullyQualified(p);
        if (relative)
        {
            if (p.StartsWith('\\') || string.IsNullOrWhiteSpace(projectFolder)) return Bad(MediaLocation.Invalid, "a relative path needs a saved project folder");
            p = Path.Combine(projectFolder, p);
        }
        string full;
        try { full = Path.GetFullPath(p); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException) // Not logged: path check: the reason is returned to the caller
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
        // A local file may be a link to somewhere else: judge the place it really leads to. A link that cannot be read is unknown: approval first.
        if (location == MediaLocation.Local)
        {
            try
            {
                if (FileSystem.FinalPathOf(full) is { } final)
                {
                    if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) final = @"\\" + final[8..];
                    else if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
                    if (IsDeviceForm(final)) return Bad(MediaLocation.Device, "the file leads to a device path");
                    var resolved = Normalize(final);
                    if (!resolved.Equals(full, StringComparison.OrdinalIgnoreCase)) { full = resolved; location = LocationOf(full); }
                }
            }
            catch (UnresolvedLinkException) { location = MediaLocation.Removable; }   // judged like a removable place: the user approves it first
        }
        var inProject = folder is not null && IsInside(full, folder);
        return new MediaVerdict(location, full, inProject, null) { EscapesProject = relative && !inProject };
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
        try { path = Path.GetFullPath(path.Trim()); } catch (Exception) { return path.Trim().Replace('/', '\\'); } // Not logged: normalise: the trimmed raw path is used
        var root = Path.GetPathRoot(path) ?? "";
        while (path.Length > root.Length && (path[^1] is '\\' or '/')) path = path[..^1];
        return path;
    }

    private static string Full(string path)
    {
        try { return Path.GetFullPath(path); } catch (Exception) { return path; } // Not logged: normalise: the raw path is compared (a path that cannot resolve is not trusted further than before)
    }

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="folder"/> or lies below it. Both sides are made canonical first (".." and "/" resolved,
    /// then 8.3 names, junctions and symbolic links resolved through the deepest part that exists); when that cannot be done the answer is no.
    /// A network path is compared as written (nothing on the network is touched).
    /// </summary>
    public static bool IsInside(string path, string folder)
    {
        path = Full(path); folder = Full(folder);
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) && !folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (Canonical(path) is not { } p || CanonicalFolder(folder) is not { } f) return false;
            path = p; folder = f;
        }
        var root = folder.TrimEnd('\\');
        return path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(path.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase);
    }

    // Folders (the project folder, approved folders) change rarely but are asked about for every file: their canonical form is kept briefly so each call
    // resolves only the file. Key: the full folder path; ceiling: Clear when over 256; eviction: the entry is dropped after FolderTtlMs (a junction retargeted
    // or a folder created meanwhile is seen within that time); a failed resolution is never kept. Thread: any (background resolver and UI).
    private const long FolderTtlMs = 2000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Canonical, long At)> FolderCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forgets the canonical folders now (call when the folders in the settings change).</summary>
    public static void ClearFolderCache() => FolderCache.Clear();

    private static string? CanonicalFolder(string folder)
    {
        var now = Environment.TickCount64;
        if (FolderCache.TryGetValue(folder, out var hit) && now - hit.At < FolderTtlMs) return hit.Canonical;
        var resolved = Canonical(folder);
        if (resolved is null) return null;
        if (FolderCache.Count > 256) FolderCache.Clear();
        FolderCache[folder] = (resolved, now);
        return resolved;
    }

    /// <summary>The path with links and short names resolved through its deepest existing ancestor (the rest appended as written); null when that fails.</summary>
    private static string? Canonical(string full)
    {
        try
        {
            var existing = full;
            var rest = new Stack<string>();
            while (!Directory.Exists(existing) && !File.Exists(existing))
            {
                var parent = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(parent)) return null;
                rest.Push(Path.GetFileName(existing));
                existing = parent;
            }
            var final = FinalPathOfExisting(existing);
            if (final is null) return null;
            while (rest.Count > 0) final = Path.Combine(final, rest.Pop());
            return final;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;   // Not logged: canonicalisation failed: the caller treats the path as outside
        }
    }

    private static string? FinalPathOfExisting(string existing)
    {
        // Backup semantics so a folder opens; no access rights are needed to ask for the name. Links on the way are followed, so the name is the real place.
        using var handle = CreateFileW(existing, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new char[32_768];
        var n = GetFinalPathNameByHandleW(handle.DangerousGetHandle(), buffer, (uint)buffer.Length, 0);
        if (n == 0 || n >= buffer.Length) return null;
        var text = new string(buffer, 0, (int)n);
        if (text.StartsWith(@"\?\UNC\", StringComparison.OrdinalIgnoreCase)) return null;   // resolved onto the network: not judged here
        return text.StartsWith(@"\?\", StringComparison.Ordinal) ? text[4..] : text;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr file, char[] buffer, uint length, uint flags);
}

/// <summary>What may be done with one linked file.</summary>
public enum MediaAccessState { Allowed, NeedsApproval, Refused }

public sealed record MediaDecision(MediaAccessState State, MediaVerdict Verdict, string Message)
{
    public bool Allowed => State == MediaAccessState.Allowed;
}

