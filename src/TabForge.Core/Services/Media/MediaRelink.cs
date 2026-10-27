using System.IO;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>A media file a song names that is not there: where it was expected and what to look for.</summary>
public sealed record MissingMedia(string FullPath, string Name, string Folder);

/// <summary>
/// What a folder search found for one missing file: <paramref name="FoundPath"/> is the pick (null: not found), <paramref name="Matches"/> how many files had the
/// name, <paramref name="Pick"/> why this one ("" for a single match, "same size" when several files share the most common size, else "newest"), and
/// <paramref name="Subfolder"/> the pick's folder below the searched one (empty: the searched folder itself).
/// </summary>
public sealed record MediaMatch(string MissingPath, string? FoundPath, int Matches, string Pick, string Subfolder);

// Owns: finding the media files a song names that are missing, and searching a folder (and every folder below) for them by file name, with the
//   several-matches pick (the most common size, then the newest) and the "same folder" matches after one file was located by hand.
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

    /// <summary>
    /// Searches <paramref name="folder"/> and every folder below it for files named like the missing ones (case-insensitive; links are not followed). Every
    /// time a file with a wanted name turns up, <paramref name="progress"/> gets the current best match of each missing file of that name, so a list can fill live.
    /// When several files share a name the pick is the size most of them have, then the newest, then the one nearest the top. Throws
    /// <see cref="OperationCanceledException"/> when <paramref name="cancel"/> is signalled (the reports made so far stand). Reads the file system: call it on a worker.
    /// </summary>
    /// <returns>One match per missing file, in the order given; FoundPath is null where nothing was found.</returns>
    public static List<MediaMatch> Find(string folder, IReadOnlyCollection<MissingMedia> missing, CancellationToken cancel = default, IProgress<MediaMatch>? progress = null)
    {
        var wanted = missing.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(m => m.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var hits = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(folder);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        var looked = 0;
        try
        {
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
            {
                cancel.ThrowIfCancellationRequested();
                if (++looked > MaxFilesSearched) break;
                if (!wanted.TryGetValue(file.Name, out var owners)) continue;
                if (!hits.TryGetValue(file.Name, out var list)) hits[file.Name] = list = new List<FileInfo>();
                list.Add(file);
                foreach (var owner in owners) progress?.Report(MatchOf(owner, list, root));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Trace.Error(Trace.Import, "media relink: search: " + ex.Message); }
        return missing.Select(m => m.FullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(full => hits.TryGetValue(Path.GetFileName(full), out var list) ? MatchOf(full, list, root) : new MediaMatch(full, null, 0, "", "")).ToList();
    }

    private static MediaMatch MatchOf(string missingPath, List<FileInfo> candidates, string root)
    {
        var groups = candidates.GroupBy(c => c.Length).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(c => c.LastWriteTimeUtc)).ToList();
        var pick = groups[0].OrderByDescending(c => c.LastWriteTimeUtc).ThenBy(c => Depth(c.FullName)).First();
        var why = candidates.Count == 1 ? "" : groups[0].Count() > 1 ? "same size" : "newest";
        var relative = Path.GetRelativePath(root, pick.DirectoryName ?? root);
        return new MediaMatch(missingPath, pick.FullName, candidates.Count, why, relative == "." ? "" : relative);
    }

    /// <summary>
    /// After <paramref name="located"/> was picked by hand for one missing file: the other missing files whose names are found in the same folder (not below it),
    /// keyed by missing path (a missing file with the picked name too: it takes the picked file).
    /// </summary>
    public static Dictionary<string, string> SiblingsOf(string located, IEnumerable<MissingMedia> others)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Path.GetDirectoryName(located) is not { Length: > 0 } folder) return found;
        foreach (var other in others)
        {
            var candidate = Path.Combine(folder, other.Name);
            if (File.Exists(candidate)) found[other.FullPath] = candidate;
        }
        return found;
    }

    private static int Depth(string path) => path.Count(c => c == Path.DirectorySeparatorChar);
}
