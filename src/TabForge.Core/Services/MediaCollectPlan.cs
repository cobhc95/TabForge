using System.IO;
using System.Security.Cryptography;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>One external file the collect copies (or reuses): <paramref name="Reuse"/> means a byte-identical file already sits at <paramref name="Target"/>.</summary>
public sealed record MediaCollectItem(string Source, long Bytes, string Target, bool Reuse);

/// <summary>A file the collect leaves linked, and why.</summary>
public sealed record MediaCollectSkip(string Path, string Reason);

// Owns: which media files a song uses outside its own folder, where each would be copied (same-named identical file reused, else a number),
//   and what is left linked (missing, not approved). It copies nothing and changes nothing.
// Does not own: the copying (MediaCollector), the path swap (MediaCollectApply), the prompt rules (MediaSavePolicy).
// Tests: TabForge.Avalonia.Tests.MediaCollectPlanTests.
/// <summary>The collect plan of one song for one destination folder. Build it on a worker: it reads file sizes and hashes.</summary>
public sealed class MediaCollectPlan
{
    public MediaCollectPlan(string mediaFolder, IReadOnlyList<MediaCollectItem> items, IReadOnlyList<MediaCollectSkip> skipped)
    {
        MediaFolder = mediaFolder;
        Items = items;
        Skipped = skipped;
    }

    /// <summary>The folder the copies go to ("&lt;song&gt; Media").</summary>
    public string MediaFolder { get; }
    public IReadOnlyList<MediaCollectItem> Items { get; }
    public IReadOnlyList<MediaCollectSkip> Skipped { get; }

    /// <summary>Every external file the collect can bring in; zero means nothing to ask about.</summary>
    public int ExternalCount => Items.Count;

    /// <summary>Bytes that really have to be copied (reused files cost nothing).</summary>
    public long BytesToCopy => Items.Where(i => !i.Reuse).Sum(i => i.Bytes);

    /// <summary>The full paths of the external files (for "new since the last save").</summary>
    public IEnumerable<string> Sources => Items.Select(i => i.Source);

    /// <summary>Largest numbered name tried before the plan gives up on a file (it is then left linked).</summary>
    private const int MaxNumbered = 999;

    /// <param name="songFolder">The folder of the song file the collect is for; a file inside its tree is not external.</param>
    /// <param name="mediaFolder">Where copies go.</param>
    /// <param name="context">The song's media context: relative paths resolve against its base, and a file that is not approved is never read.</param>
    public static MediaCollectPlan Build(SongProject project, MediaContext context, string songFolder, string mediaFolder, CancellationToken cancel = default) =>
        Build(Files(project), context, songFolder, mediaFolder, cancel);

    /// <summary>The stored clip paths of the song, copied now (UI thread) so a worker can plan without reading the live song.</summary>
    public static List<string> Files(SongProject project) => MediaStoredPaths.AudioClips(project).Select(c => c.File).ToList();

    /// <param name="storedFiles">The clip paths as stored (see <see cref="Files"/>).</param>
    public static MediaCollectPlan Build(IEnumerable<string> storedFiles, MediaContext context, string songFolder, string mediaFolder, CancellationToken cancel = default)
    {
        var items = new List<MediaCollectItem>();
        var skipped = new List<MediaCollectSkip>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = Existing(mediaFolder);   // lower-case name -> the file holding that name's content
        foreach (var stored in storedFiles)
        {
            cancel.ThrowIfCancellationRequested();
            if (MediaStoredPaths.Resolve(stored, context.BaseDirectory) is not { } full || !seen.Add(full)) continue;
            if (MediaPathPolicy.IsInside(full, songFolder)) continue;
            var decision = MediaAccess.Evaluate(stored, context);
            if (!decision.Allowed) { skipped.Add(new MediaCollectSkip(full, decision.State == MediaAccessState.NeedsApproval ? "not approved" : "not usable")); continue; }
            var info = new FileInfo(decision.Verdict.FullPath);
            if (!info.Exists) { skipped.Add(new MediaCollectSkip(full, "missing")); continue; }
            var (target, reuse) = Name(info.FullName, info.Length, mediaFolder, taken);
            if (target is null) { skipped.Add(new MediaCollectSkip(full, "too many files with this name")); continue; }
            items.Add(new MediaCollectItem(full, info.Length, target, reuse));
        }
        return new MediaCollectPlan(mediaFolder, items, skipped);
    }

    private static Dictionary<string, string> Existing(string folder)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder)) names[Path.GetFileName(file)] = file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Import, "media collect: list folder: " + ex.Message); }
        return names;
    }

    // The first free "name", "name (2)" ...; a name already taken by a byte-identical file is reused instead.
    private static (string? Target, bool Reuse) Name(string source, long length, string folder, Dictionary<string, string> taken)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        var ext = Path.GetExtension(source);
        for (var n = 1; n <= MaxNumbered; n++)
        {
            var name = n == 1 ? stem + ext : $"{stem} ({n}){ext}";
            if (!taken.TryGetValue(name, out var holder))
            {
                var target = Path.Combine(folder, name);
                taken[name] = source;   // this plan's own claim: a later same-named source numbers past it, or reuses it when identical
                return (target, false);
            }
            if (SameContent(holder, source, length)) return (Path.Combine(folder, name), true);
        }
        return (null, false);
    }

    /// <summary>True when both files have the same size and the same SHA-256.</summary>
    public static bool SameContent(string a, string b, long? lengthOfB = null)
    {
        try
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            var first = new FileInfo(a);
            if (!first.Exists || first.Length != (lengthOfB ?? new FileInfo(b).Length)) return false;
            using var x = File.OpenRead(a);
            using var y = File.OpenRead(b);
            return SHA256.HashData(x).AsSpan().SequenceEqual(SHA256.HashData(y));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Import, "media collect: compare files: " + ex.Message); return false; }
    }
}
