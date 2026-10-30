using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO;
using TabForge.Audio;
using TabForge.Platform;

namespace TabForge.Services;

// Owns: the mutable half of the media path policy: the file-system adapter it asks and the brief cache of canonical folders.
// Does not own: the path rules themselves (MediaPathPolicy: pure, static) or the approval state (MediaAccess).
// Tests: MediaLinkJudgementTests (the facade), ScopedServiceTests (an isolated service).

/// <summary>
/// One media path classifier with its own file-system adapter and folder cache. The app uses the process-wide
/// <see cref="MediaPathPolicy"/> facade; a test or an isolated host may classify through its own service.
/// </summary>
public sealed class MediaPathPolicyService
{
    private static readonly FrozenSet<string> Reserved = new[]
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Folders (the project folder, approved folders) change rarely but are asked about for every file: their canonical form is kept briefly so each call
    // resolves only the file. Key: the full folder path; ceiling: Clear when over 256; eviction: the entry is dropped after FolderTtlMs (a junction retargeted
    // or a folder created meanwhile is seen within that time); a failed resolution is never kept. Thread: any (background resolver and UI).
    private const long FolderTtlMs = 2000;
    private readonly ConcurrentDictionary<string, (string Canonical, long At)> _folderCache = new(PathComparer.Default);

    public MediaPathPolicyService(IMediaFileSystem fileSystem) => FileSystem = fileSystem;

    /// <summary>The file-system questions; the app sets the real one at startup (Program.Main), tests replace it.</summary>
    public IMediaFileSystem FileSystem { get; set; }

    public MediaVerdict Classify(string? path, string? projectFolder) => Classify(path, projectFolder, requireAudio: true);

    /// <summary>As <see cref="Classify(string?, string?)"/>; <paramref name="requireAudio"/> false judges any file type the same way.</summary>
    public MediaVerdict Classify(string? path, string? projectFolder, bool requireAudio)
    {
        if (!OperatingSystem.IsWindows()) return UnixPathRules.Classify(path, projectFolder, requireAudio, UnixFinalPath);   // drive letters, UNC, device forms, reserved names and 8.3 names are Windows rules
        static MediaVerdict Bad(MediaLocation l, string why) => new(l, "", false, why);
        if (string.IsNullOrWhiteSpace(path)) return Bad(MediaLocation.Invalid, "no file path");
        if (path.Length > InputLimits.MaxPathLength) return Bad(MediaLocation.Invalid, "the path is too long");
        if (path.Any(c => c < 32)) return Bad(MediaLocation.Invalid, "the path has control characters");
        if (MediaPathPolicy.IsDeviceForm(path)) return Bad(MediaLocation.Device, "device paths are never opened");
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
        if (MediaPathPolicy.IsDeviceForm(full)) return Bad(MediaLocation.Device, "device paths are never opened");
        foreach (var segment in full.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = segment.TrimEnd('.', ' ');
            var dot = stem.IndexOf('.');
            if (Reserved.Contains(dot >= 0 ? stem[..dot] : stem)) return Bad(MediaLocation.Device, "a reserved device name in the path");
        }
        if (requireAudio && !MediaPathPolicy.IsAudioExtension(full)) return Bad(MediaLocation.Invalid, "not an audio file type");

        var folder = string.IsNullOrWhiteSpace(projectFolder) ? null : MediaPathPolicy.Normalize(projectFolder);
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
                    if (MediaPathPolicy.IsDeviceForm(final)) return Bad(MediaLocation.Device, "the file leads to a device path");
                    var resolved = MediaPathPolicy.Normalize(final);
                    if (!resolved.Equals(full, StringComparison.OrdinalIgnoreCase)) { full = resolved; location = LocationOf(full); }
                }
            }
            catch (UnresolvedLinkException) { location = MediaLocation.Removable; }   // judged like a removable place: the user approves it first
        }
        var inProject = folder is not null && MediaPathPolicy.IsInside(full, folder);
        return new MediaVerdict(location, full, inProject, null) { EscapesProject = relative && !inProject };
    }

    // Unix: the link target of the file itself, or null when it is not a link or cannot be read.
    private static string? UnixFinalPath(string full)
    {
        try { return new FileInfo(full).ResolveLinkTarget(true)?.FullName; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; } // Not logged: link probe: null means it stays where it says
    }

    private MediaLocation LocationOf(string full)
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

    /// <summary>Forgets the canonical folders now (call when the folders in the settings change).</summary>
    public void ClearFolderCache() => _folderCache.Clear();

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies below it (see <see cref="MediaPathPolicy.IsInside"/>).</summary>
    public bool IsInside(string path, string folder)
    {
        path = MediaPathPolicy.Full(path); folder = MediaPathPolicy.Full(folder);
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) && !folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (MediaPathPolicy.Canonical(path) is not { } p || CanonicalFolder(folder) is not { } f) return false;
            path = p; folder = f;
        }
        var root = folder.TrimEnd(Path.DirectorySeparatorChar);
        return path.StartsWith(root + Path.DirectorySeparatorChar, PathComparer.Comparison) || string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root, PathComparer.Comparison);
    }

    private string? CanonicalFolder(string folder)
    {
        var now = Environment.TickCount64;
        if (_folderCache.TryGetValue(folder, out var hit) && now - hit.At < FolderTtlMs) return hit.Canonical;
        var resolved = MediaPathPolicy.Canonical(folder);
        if (resolved is null) return null;
        if (_folderCache.Count > 256) _folderCache.Clear();
        _folderCache[folder] = (resolved, now);
        return resolved;
    }
}
