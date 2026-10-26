using System.IO;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>A media file a song names that is not there: where it was expected and what to look for.</summary>
public sealed record MissingMedia(string FullPath, string Name, string Folder);

// Owns: finding the media files a song names that are missing, and searching a folder for them by file name.
// Does not own: the dialog, the file pickers, the model change (MediaCollectApply.Apply is given the answers).
// Tests: TabForge.Avalonia.Tests.MediaRelinkTests.
/// <summary>Missing-media detection and the by-name folder search. Both read the file system: call them on a worker.</summary>
public static class MediaRelink
{
    /// <summary>The most files one search looks at before it stops (a whole drive is not walked forever).</summary>
    public const int MaxFilesSearched = 500_000;

    /// <summary>The usable media files of the song that do not exist, one per distinct path. A file that waits for approval is not touched.</summary>
    public static List<MissingMedia> FindMissing(SongProject project, MediaContext context) => FindMissing(MediaCollectPlan.Files(project), context);

    /// <param name="storedFiles">The clip paths as stored (<see cref="MediaCollectPlan.Files"/>), copied on the UI thread.</param>
    public static List<MissingMedia> FindMissing(IEnumerable<string> storedFiles, MediaContext context)
    {
        var list = new List<MissingMedia>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stored in storedFiles)
        {
            if (MediaStoredPaths.Resolve(stored, context.BaseDirectory) is not { } full || !seen.Add(full)) continue;
            var decision = MediaAccess.Evaluate(stored, context);
            if (!decision.Allowed || File.Exists(decision.Verdict.FullPath)) continue;
            list.Add(new MissingMedia(full, Path.GetFileName(full), Path.GetDirectoryName(full) ?? ""));
        }
        return list;
    }

    /// <summary>
    /// Looks through <paramref name="folder"/> and below for files named like the missing ones (case-insensitive; links are not followed). Returns the
    /// matches keyed by the missing file's full path, the one nearest the folder's top winning a tie. The project stores no file size, so the name is what is matched.
    /// </summary>
    public static Dictionary<string, string> Search(string folder, IReadOnlyCollection<MissingMedia> missing, CancellationToken cancel = default)
    {
        var wanted = missing.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(m => m.FullPath).ToList(), StringComparer.OrdinalIgnoreCase);
        var best = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // wanted name -> nearest file found
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        var looked = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                if (++looked > MaxFilesSearched) break;
                if ((looked & 255) == 0) cancel.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (!wanted.ContainsKey(name)) continue;
                if (!best.TryGetValue(name, out var have) || Depth(file) < Depth(have)) best[name] = file;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Trace.Error(Trace.Import, "media relink: search: " + ex.Message); }
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, hit) in best)
            foreach (var full in wanted[name]) found[full] = hit;
        return found;
    }

    private static int Depth(string path) => path.Count(c => c == Path.DirectorySeparatorChar);
}
