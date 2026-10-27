using System.IO;

namespace TabForge.Services;

/// <summary>Where a collect is: file <paramref name="FileIndex"/> of <paramref name="FileCount"/> (1-based), and the bytes copied so far of all.</summary>
public sealed record MediaCopyProgress(int FileIndex, int FileCount, string FileName, long BytesDone, long BytesTotal);

/// <summary>What a collect achieved: <paramref name="Done"/> maps each source to the file that now holds it; the rest stay linked.</summary>
public sealed record MediaCollectResult(IReadOnlyDictionary<string, string> Done, IReadOnlyList<string> Failed, bool Cancelled);

// Owns: running a collect plan: each file copied under a temporary name in the media folder and renamed when whole, progress, cancel.
// Does not own: the plan (MediaCollectPlan), changing the song (MediaCollectApply).
// Tests: TabForge.Avalonia.Tests.MediaCollectPlanTests.
/// <summary>
/// Copies the files of a <see cref="MediaCollectPlan"/>. Originals are only read, never moved or deleted; an existing file is never overwritten (the
/// rename refuses); a copy cut short removes only its own temporary file. A file counts as done only after its rename, so the caller can change a
/// clip's path only for files that really are there. Call it from any thread; it does the file work on a worker.
/// </summary>
public static class MediaCollector
{
    private const int BufferBytes = 1 << 20;
    private const string TempMarker = ".tfcopy-";

    public static Task<MediaCollectResult> RunAsync(MediaCollectPlan plan, IProgress<MediaCopyProgress>? progress, CancellationToken cancel) =>
        Task.Run(() => Run(plan, progress, cancel));

    private static MediaCollectResult Run(MediaCollectPlan plan, IProgress<MediaCopyProgress>? progress, CancellationToken cancel)
    {
        var done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        var total = plan.BytesToCopy;
        long copied = 0;
        for (var i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            if (cancel.IsCancellationRequested) return new MediaCollectResult(done, failed, true);
            var name = Path.GetFileName(item.Source);
            progress?.Report(new MediaCopyProgress(i + 1, plan.Items.Count, name, copied, total));
            if (item.Reuse)
            {
                if (File.Exists(item.Target)) done[item.Source] = item.Target; else failed.Add(item.Source);
                continue;
            }
            var before = copied;
            var ok = Copy(item, cancel, bytes => progress?.Report(new MediaCopyProgress(i + 1, plan.Items.Count, name, before + bytes, total)));
            if (cancel.IsCancellationRequested && !ok) return new MediaCollectResult(done, failed, true);
            if (ok) done[item.Source] = item.Target; else failed.Add(item.Source);
            copied += item.Bytes;
        }
        progress?.Report(new MediaCopyProgress(plan.Items.Count, plan.Items.Count, "", total, total));
        return new MediaCollectResult(done, failed, false);
    }

    private static bool Copy(MediaCollectItem item, CancellationToken cancel, Action<long> bytesDone)
    {
        var temp = item.Target + TempMarker + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
            using (var from = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes))
            using (var to = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes))
            {
                var buffer = new byte[BufferBytes];
                long sum = 0;
                int n;
                while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancel.IsCancellationRequested) { to.Dispose(); TryRemove(temp); return false; }
                    to.Write(buffer, 0, n);
                    sum += n;
                    bytesDone(sum);
                }
            }
            File.Move(temp, item.Target, overwrite: false);   // refuses when the name appeared meanwhile: nothing is overwritten
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Trace.Error(Trace.Import, "media collect: copy " + Path.GetFileName(item.Source) + ": " + ex.Message);
            TryRemove(temp);
            return false;
        }
    }

    // Only the temporary file this run made: never an original.
    private static void TryRemove(string temp)
    {
        try { if (File.Exists(temp)) File.Delete(temp); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Import, "media collect: remove temp: " + ex.Message); }
    }
}
