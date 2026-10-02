using System.Collections.Concurrent;
using System.IO;

namespace TabForge.Services;

/// <summary>
/// The gate every project-referenced media file goes through (waveforms, clip playback, render, drops): refused forms never open,
/// remote / removable folders open only after the user approves that folder for that song. Every call is given the song's
/// <see cref="MediaContext"/> explicitly (its media base directory and approval scope); nothing here knows about windows, the focused
/// tab or a "current" document.
/// </summary>
public static class MediaAccess
{
    /// <summary>Approvals were added or revoked (or a song's scope changed): waveforms and the engine's clips are evaluated again.</summary>
    public static event Action? Changed;

    internal static void RaiseChanged() => Changed?.Invoke();

    public static string? FolderOf(string? projectPath) =>
        string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetDirectoryName(MediaPathPolicy.Normalize(projectPath));

    public static MediaDecision Evaluate(string? file, MediaContext? context)
    {
        context ??= MediaContext.Anonymous;
        return Decide(Classified(file, context.BaseDirectory), context);
    }

    /// <summary>
    /// The approval decision for a path that was already classified: memory only, no file-system call (classifying a link opens the file, which can
    /// stall on an unreachable share). Code on the draw path or under a lock re-checks permission with the verdict a worker obtained earlier.
    /// </summary>
    public static MediaDecision Decide(MediaVerdict v, MediaContext context)
    {
        if (v.Refused) return new(MediaAccessState.Refused, v, $"not loaded ({v.Problem})");
        // "Inside the project folder" counts only for the folder of a deliberately opened or saved song (as before R2). The folder an
        // imported song came from, or a duplicate's inherited folder, resolves relative paths but grants nothing on a network or removable drive.
        if (v.Location == MediaLocation.Local || (v.InProject && context.IsSaved) || IsApproved(v, context)) return new(MediaAccessState.Allowed, v, "");
        var where = v.Location == MediaLocation.Network ? "a network location" : "a removable drive";
        return new(MediaAccessState.NeedsApproval, v, $"This song links audio on {where}: {v.FullPath}. Allow?");
    }

    // Classifying a local file opens it once to resolve links: remembered for a few seconds (redraws and engine syncs ask often).
    private static readonly ConcurrentDictionary<(string, string), (MediaVerdict Verdict, long Tick)> Cache = new();

    private static MediaVerdict Classified(string? file, string? folder)
    {
        if (file is null) return MediaPathPolicy.Classify(file, folder);
        var key = (file, folder ?? "");
        var now = Environment.TickCount64;
        if (Cache.TryGetValue(key, out var hit) && now - hit.Tick < 3000) return hit.Verdict;
        var v = MediaPathPolicy.Classify(file, folder);
        if (Cache.Count > 4096) Cache.Clear();
        Cache[key] = (v, now);
        return v;
    }

    // ---- non-blocking evaluation for the UI thread ----
    // Classifying a link opens the file, which can stall on an unreachable share. Code that runs on the UI thread (the engine's clip sync, the
    // approval notice bar) asks here instead: a remembered verdict is used at once (the approval decision itself is memory only), and a missing or
    // old one is resolved by one background worker, which raises Resolved when something changed. The decision is always taken with the asking
    // document's own context at the moment it is used, so a result never lands on another document or on an older revision of the same one.

    /// <summary>A background classification finished with a new or changed verdict (raised on the worker thread; consumers marshal to their own thread).</summary>
    public static event Action? Resolved;

    private static readonly ConcurrentQueue<(string File, string Folder)> ResolveQueue = new();
    private static readonly ConcurrentDictionary<(string, string), byte> Queued = new();
    private static int _resolverRunning;

    /// <summary>
    /// The decision for <paramref name="file"/> without touching the file system; null while its classification is still being resolved in the
    /// background (treat as "not yet allowed"; <see cref="Resolved"/> follows).
    /// </summary>
    public static MediaDecision? EvaluateNoWait(string? file, MediaContext? context)
    {
        context ??= MediaContext.Anonymous;
        if (file is null) return Decide(MediaPathPolicy.Classify(null, context.BaseDirectory), context);
        var key = (file, context.BaseDirectory ?? "");
        if (Cache.TryGetValue(key, out var hit))
        {
            if (Environment.TickCount64 - hit.Tick >= 3000) QueueResolve(key);   // stale: keep using it, refresh in the background
            return Decide(hit.Verdict, context);
        }
        QueueResolve(key);
        return null;
    }

    private static void QueueResolve((string File, string Folder) key)
    {
        if (Queued.Count > 4096 || !Queued.TryAdd(key, 0)) return;
        ResolveQueue.Enqueue(key);
        if (Interlocked.CompareExchange(ref _resolverRunning, 1, 0) == 0) _ = Task.Run(ResolveLoop);
    }

    private static void ResolveLoop()
    {
        while (true)
        {
            var changed = false;
            while (ResolveQueue.TryDequeue(out var key))
            {
                try
                {
                    var verdict = MediaPathPolicy.Classify(key.File, key.Folder.Length == 0 ? null : key.Folder);
                    if (Cache.Count > 4096) Cache.Clear();
                    var had = Cache.TryGetValue((key.File, key.Folder), out var old);
                    Cache[(key.File, key.Folder)] = (verdict, Environment.TickCount64);
                    if (!had || !old.Verdict.Equals(verdict)) changed = true;
                }
                catch (Exception) { /* an unreadable path stays unresolved: asked again on the next sync */ }
                finally { Queued.TryRemove(key, out _); }
            }
            if (changed) { try { Resolved?.Invoke(); } catch (Exception) { } }
            Volatile.Write(ref _resolverRunning, 0);
            if (ResolveQueue.IsEmpty || Interlocked.CompareExchange(ref _resolverRunning, 1, 0) != 0) return;
        }
    }

    /// <summary>
    /// Classifies every linked clip now (this may wait on the file system, as every evaluation did before the non-blocking path). For a deliberate
    /// whole-song operation (File > Render): the sync that follows must not leave out an allowed clip only because the background resolver has not
    /// reached its path yet, which would silently drop that audio from the rendered file.
    /// </summary>
    public static void ResolveNow(IEnumerable<TabForge.Models.AudioClip> clips, MediaContext? context)
    {
        context ??= MediaContext.Anonymous;
        foreach (var clip in clips)
            if (!clip.IsMidi && !string.IsNullOrEmpty(clip.File)) _ = Classified(clip.File, context.BaseDirectory);
    }

    /// <summary>Tests: waits until no background classification is queued or running.</summary>
    internal static bool WaitResolved(int timeoutMs)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (Queued.IsEmpty && Volatile.Read(ref _resolverRunning) == 0) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    /// <summary>Forgets remembered classifications (a changed file system, or a test that swaps <see cref="MediaPathPolicy.FileSystem"/>).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// True when the folder of <paramref name="v"/> was approved for this song: a saved song by an approval stored for exactly its path,
    /// an unsaved song by its own session's approvals. An approval with an empty path (written before unsaved songs had their own scope)
    /// matches nothing.
    /// </summary>
    public static bool IsApproved(MediaVerdict v, MediaContext context)
    {
        if (context.IsSessionFolderApproved(v.FullPath)) return true;
        if (context.SavedPath is not { } key) return false;
        var list = context.Settings?.ApprovedMedia;
        if (list is null) return false;
        lock (list) return list.Any(a => string.Equals(a.Project, key, StringComparison.OrdinalIgnoreCase) && MediaPathPolicy.IsInside(v.FullPath, a.Folder));
    }

    /// <summary>Allows the folder of <paramref name="v"/> (and its subfolders) for this song: stored for a saved song, memory only for an unsaved one.</summary>
    public static void Approve(MediaVerdict v, MediaContext context)
    {
        if (v.Refused || v.Folder.Length == 0 || context.IsAnonymous || context.IsClosed) return;
        if (context.SavedPath is not { } key) context.AddSessionFolder(v.Folder);
        else if (context.Settings?.ApprovedMedia is { } list)
        {
            lock (list)
                if (!list.Any(a => string.Equals(a.Project, key, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Folder, v.Folder, StringComparison.OrdinalIgnoreCase)) && list.Count < 512)
                    list.Add(new MediaApproval { Project = key, Folder = v.Folder });
        }
        else return;
        Changed?.Invoke();
    }

    /// <summary>Ends an approval: a stored one (any song's, as listed in the review window) or this unsaved song's session approval.</summary>
    public static void Revoke(MediaApproval approval, MediaContext context)
    {
        var removed = false;
        if (context.Settings?.ApprovedMedia is { } list) lock (list) removed = list.Remove(approval);
        if (!removed && approval.Project == context.ScopeKey) removed = context.RemoveSessionFolder(approval.Folder);
        if (removed) Changed?.Invoke();
    }

    /// <summary>The approvals that apply to this song, for the review window: stored ones for its path plus its session's.</summary>
    public static List<MediaApproval> ApprovalsOf(MediaContext context)
    {
        var result = new List<MediaApproval>();
        if (context.SavedPath is { } key && context.Settings?.ApprovedMedia is { } list)
            lock (list) result.AddRange(list.Where(a => string.Equals(a.Project, key, StringComparison.OrdinalIgnoreCase)));
        else foreach (var folder in context.SessionFolders()) result.Add(new MediaApproval { Project = context.ScopeKey, Folder = folder });
        return result;
    }

    /// <summary>As <see cref="Unapproved"/> without touching the file system (for the UI thread): clips whose classification is still being resolved are left out until <see cref="Resolved"/>.</summary>
    public static List<MediaDecision> UnapprovedNoWait(IEnumerable<TabForge.Models.AudioClip> clips, MediaContext context)
    {
        var result = new List<MediaDecision>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            if (clip.IsMidi || string.IsNullOrEmpty(clip.File)) continue;
            if (EvaluateNoWait(clip.File, context) is { State: MediaAccessState.NeedsApproval } d && seen.Add(d.Verdict.Folder)) result.Add(d);
        }
        return result;
    }

    /// <summary>The project's linked audio folders that still wait for approval (one entry per folder).</summary>
    public static List<MediaDecision> Unapproved(IEnumerable<TabForge.Models.AudioClip> clips, MediaContext context)
    {
        var result = new List<MediaDecision>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            if (clip.IsMidi || string.IsNullOrEmpty(clip.File)) continue;
            var d = Evaluate(clip.File, context);
            if (d.State == MediaAccessState.NeedsApproval && seen.Add(d.Verdict.Folder)) result.Add(d);
        }
        return result;
    }
}
