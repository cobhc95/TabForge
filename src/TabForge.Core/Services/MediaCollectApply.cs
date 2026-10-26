using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the one model change a collect, a relink or a Save As makes to clip paths (changed together, so the caller makes it one undo step).
// Does not own: the undo step and the refresh (the window), what is copied (MediaCollector), how paths are written (MediaStoredPaths).
// Tests: TabForge.Avalonia.Tests.MediaCollectPlanTests.
/// <summary>Points clips at their new files. Call it on the UI thread inside one document edit.</summary>
public static class MediaCollectApply
{
    /// <summary>
    /// Every audio clip whose resolved path is a key of <paramref name="moved"/> now names the value (an absolute path; the save writes it relative when it is
    /// inside the song folder). When the song moves to another folder (<paramref name="newBase"/> differs from <paramref name="oldBase"/>), the clips that
    /// remain relative are made absolute first, so they still lead to the same file once the song's folder changes. Returns how many clips changed.
    /// </summary>
    public static int Apply(SongProject project, IReadOnlyDictionary<string, string> moved, string? oldBase, string? newBase)
    {
        var changed = 0;
        var folderMoves = newBase is not null && !MediaStoredPaths.SameFolder(oldBase, newBase);
        foreach (var clip in MediaStoredPaths.AudioClips(project))
        {
            if (MediaStoredPaths.Resolve(clip.File, oldBase) is not { } full) continue;
            if (moved.TryGetValue(full, out var target) && !string.Equals(clip.File, target, StringComparison.Ordinal)) { clip.File = target; changed++; }
            else if (folderMoves && !Path.IsPathFullyQualified(clip.File.Trim())) { clip.File = full; changed++; }
        }
        return changed;
    }

    /// <summary>True when <see cref="Apply"/> would change a clip (nothing to record then).</summary>
    public static bool WouldChange(SongProject project, IReadOnlyDictionary<string, string> moved, string? oldBase, string? newBase)
    {
        var folderMoves = newBase is not null && !MediaStoredPaths.SameFolder(oldBase, newBase);
        return MediaStoredPaths.AudioClips(project).Any(clip => MediaStoredPaths.Resolve(clip.File, oldBase) is { } full
            && (moved.ContainsKey(full) || folderMoves && !Path.IsPathFullyQualified(clip.File.Trim())));
    }
}
