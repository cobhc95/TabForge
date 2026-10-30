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
    private readonly ConcurrentDictionary<string, DriveType> Drives = new(StringComparer.OrdinalIgnoreCase);

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
// Tests: MediaLinkJudgementTests, ScopedServiceTests.
/// <summary>
/// Classifies a path a project names for audio (clips): local / removable / network / device. Device paths, \\?\ forms,
/// reserved device names, alternate data streams and non-audio extensions are refused; remote paths (and links that lead
/// there) need the user's approval (<see cref="MediaAccess"/>). Nothing on a remote path is touched to classify it.
/// </summary>
public static class MediaPathPolicy
{
    private static readonly ServiceSlot<MediaPathPolicyService> Slot = new(new MediaPathPolicyService(new RealMediaFileSystem(null)));

    /// <summary>The service the facade currently delegates to (the app sets the real file system on it at startup).</summary>
    internal static MediaPathPolicyService Current => Slot.Current;

    /// <summary>Makes <paramref name="service"/> the classifier until the returned scope is disposed.</summary>
    internal static IDisposable Use(MediaPathPolicyService service) => Slot.Use(service);

    /// <summary>The file-system questions; the app sets the real one at startup (Program.Main), tests replace it.</summary>
    internal static IMediaFileSystem FileSystem
    {
        get => Current.FileSystem;
        set => Current.FileSystem = value;
    }

    public static bool IsAudioExtension(string path) =>
        WaveformCache.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>True for the \\.\, \\?\, \??\ and //./ forms (and GLOBALROOT): such a path is never opened.</summary>
    public static bool IsDeviceForm(string path)
    {
        var p = path.TrimStart().Replace('/', '\\');
        return p.StartsWith(@"\\.\", StringComparison.Ordinal) || p.StartsWith(@"\\?\", StringComparison.Ordinal)
            || p.StartsWith(@"\??\", StringComparison.Ordinal) || p.Contains("GLOBALROOT", StringComparison.OrdinalIgnoreCase);
    }

    public static MediaVerdict Classify(string? path, string? projectFolder) => Current.Classify(path, projectFolder);

    /// <summary>As <see cref="Classify(string?, string?)"/>; <paramref name="requireAudio"/> false judges any file type the same way
    /// (a dropped MIDI file: device forms refused, network and removable places told apart, links followed).</summary>
    public static MediaVerdict Classify(string? path, string? projectFolder, bool requireAudio) => Current.Classify(path, projectFolder, requireAudio);

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { path = Path.GetFullPath(path.Trim()); } catch (Exception) { return path.Trim().Replace('/', '\\'); } // Not logged: normalise: the trimmed raw path is used
        var root = Path.GetPathRoot(path) ?? "";
        while (path.Length > root.Length && (path[^1] is '\\' or '/')) path = path[..^1];
        return path;
    }

    internal static string Full(string path)
    {
        try { return Path.GetFullPath(path); } catch (Exception) { return path; } // Not logged: normalise: the raw path is compared (a path that cannot resolve is not trusted further than before)
    }

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="folder"/> or lies below it. Both sides are made canonical first (".." and "/" resolved,
    /// then 8.3 names, junctions and symbolic links resolved through the deepest part that exists); when that cannot be done the answer is no.
    /// A network path is compared as written (nothing on the network is touched).
    /// </summary>
    public static bool IsInside(string path, string folder) => Current.IsInside(path, folder);

    /// <summary>Forgets the canonical folders now (call when the folders in the settings change).</summary>
    public static void ClearFolderCache() => Current.ClearFolderCache();

    /// <summary>The path with links and short names resolved through its deepest existing ancestor (the rest appended as written); null when that fails.</summary>
    internal static string? Canonical(string full)
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

    private static string? FinalPathOfExisting(string existing) =>
#if WINDOWS
        TabForge.Platform.Windows.WindowsFinalPathName.Of(existing);
#else
        new FileInfo(existing).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(existing);   // Unix: links followed by the runtime
#endif
}

/// <summary>What may be done with one linked file.</summary>
public enum MediaAccessState { Allowed, NeedsApproval, Refused }

public sealed record MediaDecision(MediaAccessState State, MediaVerdict Verdict, string Message)
{
    public bool Allowed => State == MediaAccessState.Allowed;
}

