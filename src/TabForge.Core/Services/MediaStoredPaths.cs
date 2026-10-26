using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: how a clip's file path is stored in a saved song: a path inside the song's folder is written relative to it, any other stays absolute;
//   resolving a stored path; the scoped swap a save does on its private copy of the song.
// Does not own: the read side's classification and approval (MediaPathPolicy, MediaAccess), the write itself (DocumentController).
// Tests: TabForge.Avalonia.Tests.MediaStoredPathsTests.
/// <summary>Relative storage of media paths: written relative inside the song folder, resolved against the folder the song was opened from.</summary>
public static class MediaStoredPaths
{
    /// <summary>The audio (not MIDI) clips of the song that name a file.</summary>
    public static IEnumerable<AudioClip> AudioClips(SongProject project) =>
        project.Tracks.SelectMany(t => t.AudioClips).Where(c => !c.IsMidi && !string.IsNullOrWhiteSpace(c.File));

    /// <summary>The absolute path a stored path names (relative ones joined to <paramref name="baseDirectory"/>); null when it cannot be told (device form, relative without a base, malformed).</summary>
    public static string? Resolve(string? file, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(file) || MediaPathPolicy.IsDeviceForm(file) || file.Any(c => c < 32)) return null;
        try
        {
            var p = file.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathFullyQualified(p)) return Path.GetFullPath(p);
            if (p.StartsWith(Path.DirectorySeparatorChar) ||p.Contains(':') || string.IsNullOrWhiteSpace(baseDirectory)) return null;
            return Path.GetFullPath(Path.Combine(baseDirectory, p));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;   // Not logged: path check: null means "cannot tell", the caller leaves the path as it is
        }
    }

    /// <summary>The text to store for <paramref name="fullPath"/> in a song whose folder is <paramref name="folder"/>: relative when inside it, else the full path.</summary>
    public static string ToStored(string fullPath, string folder)
    {
        if (!MediaPathPolicy.IsInside(fullPath, folder)) return fullPath;
        var relative = Path.GetRelativePath(folder, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative;
    }

    /// <summary>True when both name the same folder.</summary>
    public static bool SameFolder(string? a, string? b) =>
        a is not null && b is not null && string.Equals(MediaPathPolicy.Normalize(a), MediaPathPolicy.Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rewrites every clip path of <paramref name="project"/> as it should be stored in a song kept in <paramref name="newFolder"/>: inside it relative,
    /// outside it absolute (a relative path that led out of an unchanged folder stays as written). Relative paths resolve against
    /// <paramref name="oldBase"/>, the folder the song was opened from. Dispose the result to put the old paths back.
    /// </summary>
    public static Scope Apply(SongProject project, string? oldBase, string newFolder)
    {
        var scope = new Scope();
        var unchanged = SameFolder(oldBase, newFolder);
        foreach (var clip in AudioClips(project))
        {
            if (Resolve(clip.File, oldBase) is not { } full) continue;
            var wasRelative = !Path.IsPathFullyQualified(clip.File.Trim());
            var stored = ToStored(full, newFolder);
            if (stored == full && wasRelative && unchanged) continue;   // still outside the same folder: kept as written
            if (stored == clip.File) continue;
            scope.Swap(clip, stored);
        }
        return scope;
    }

    /// <summary>
    /// Runs <paramref name="write"/> with the clip paths in storable form, then puts them back. Returns what <paramref name="write"/> returned (the hash of
    /// what it wrote); when a path was swapped, the hash is recomputed over the restored song so it still equals the live song's.
    /// </summary>
    public static byte[]? Write(SongProject project, string? oldBase, string targetPath, Func<byte[]?> write)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (folder is null) return write();
        using var scope = Apply(project, oldBase, folder);
        var hash = write();
        if (!scope.Changed) return hash;
        scope.Dispose();
        return ProjectService.ContentHash(project);
    }

    /// <summary>The clip paths swapped by <see cref="Apply"/>; disposing restores them.</summary>
    public sealed class Scope : IDisposable
    {
        private readonly List<(AudioClip Clip, string Original)> _swapped = new();
        internal void Swap(AudioClip clip, string stored) { _swapped.Add((clip, clip.File)); clip.File = stored; }
        public bool Changed => _swapped.Count > 0;
        public void Dispose()
        {
            foreach (var (clip, original) in _swapped) clip.File = original;
            _swapped.Clear();
        }
    }
}
