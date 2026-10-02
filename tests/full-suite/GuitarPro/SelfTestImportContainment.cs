using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Threading;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// A5-07: Guitar Pro import is contained. Zip bombs, oversized GPX and damaged GP3-5 headers are refused before alphaTab
    /// parses; the background import gives the same song as the synchronous one; Cancel and failures apply nothing; the calling
    /// (UI) thread keeps running while an import is slow; the time and memory budgets stop an import cleanly.
    /// </summary>
    private static void TestGuitarProImportContainment()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            // --- pre-parse limits ---
            var bomb = Path.Combine(folder, "bomb.gp");
            using (var zip = new ZipArchive(File.Create(bomb), ZipArchiveMode.Create))
            {
                using var entry = zip.CreateEntry("Content/score.gpif", CompressionLevel.Fastest).Open();
                var zeros = new byte[1024 * 1024];
                for (var i = 0; i <= GuitarProPreParse.MaxZipEntryBytes / zeros.Length; i++) entry.Write(zeros);
            }
            Check("import containment: a zip-bomb-like .gp (small file, >128 MiB unpacked) is refused before parsing",
                new FileInfo(bomb).Length < 4 * 1024 * 1024 &&
                ThrowsInvalidData(() => GuitarProImporter.Import(bomb), out var bombError) && bombError.Contains("unpacks to more than", StringComparison.Ordinal),
                new FileInfo(bomb).Length.ToString());

            var crowded = Path.Combine(folder, "crowded.gp");
            using (var zip = new ZipArchive(File.Create(crowded), ZipArchiveMode.Create))
                for (var i = 0; i <= GuitarProPreParse.MaxZipEntries; i++) zip.CreateEntry($"Content/{i}.bin");
            Check("import containment: a .gp archive with too many entries is refused before parsing",
                ThrowsInvalidData(() => GuitarProImporter.Import(crowded), out var crowdedError) && crowdedError.Contains("holds", StringComparison.Ordinal));

            var gpx = Path.Combine(folder, "huge.gpx");
            File.WriteAllBytes(gpx, new byte[] { (byte)'B', (byte)'C', (byte)'F', (byte)'Z', 0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0, 0 });
            Check("import containment: a .gpx declaring an oversized unpacked size is refused before parsing",
                ThrowsInvalidData(() => GuitarProImporter.Import(gpx), out var gpxError) && gpxError.Contains("unpacked size", StringComparison.Ordinal));

            var badHeader = Path.Combine(folder, "bad-header.gp5");
            var header = new byte[256];
            var signature = System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v9.99");
            header[0] = (byte)signature.Length;
            signature.CopyTo(header, 1);
            File.WriteAllBytes(badHeader, header);
            Check("import containment: a Guitar Pro 3-5 file with a damaged header is refused before parsing",
                ThrowsInvalidData(() => GuitarProImporter.Import(badHeader), out var headerError) && headerError.Contains("damaged header", StringComparison.Ordinal));

            // --- async import == sync import ---
            var song = new SongProject { Title = "Contained", Tempo = 132 };
            song.Tracks.Add(new TrackModel { Name = "Lead", Measures = TemplateFactory.Measures(4) });
            song.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 5 });
            song.Tracks[0].Measures[2].Cells[4].Notes.Add(new TabNote { StringIndex = 3, Fret = 7 });
            song.Tracks.Add(new TrackModel { Name = "Rhythm", Measures = TemplateFactory.Measures(4) });
            var gp = Path.Combine(folder, "normal.gp");
            GuitarProExporter.Save(song, gp, embedProject: false);
            var controller = new DocumentController();
            var sync = controller.Open(gp);
            var viaAsync = ScoreImportQueue.ImportAsync(gp, (path, context) => controller.Open(path, null, context), CancellationToken.None,
                ImportGuard.DefaultTimeBudget, ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
            var asyncDone = viaAsync.Wait(TimeSpan.FromSeconds(30));
            Check("import containment: a normal .gp imported in the background equals the synchronous import",
                asyncDone && SameImportedSong(viaAsync.Result.Project, sync.Project) &&
                viaAsync.Result.ImportedFromGuitarPro && viaAsync.Result.Project.Tracks.Count == 2, asyncDone ? null : "timed out");

            // --- the queue on a UI thread: apply runs there, cancel applies nothing, the thread is never blocked ---
            // Earlier self-tests shut this thread's dispatcher down, so the queue checks get their own STA dispatcher thread.
            Exception? queueCrash = null;
            var uiRunner = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                try { ImportQueueChecks(controller, gp, sync.Project); }
                catch (Exception ex) { queueCrash = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            }) { IsBackground = true };
            uiRunner.SetApartmentState(ApartmentState.STA);
            uiRunner.Start();
            var queueFinished = uiRunner.Join(TimeSpan.FromSeconds(90));
            Check("import containment: the UI-thread queue checks ran to completion", queueFinished && queueCrash is null, queueCrash?.Message);

            RunImportBudgetChecks(controller, gp);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void ImportQueueChecks(DocumentController controller, string gp, SongProject expected)
    {
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var realQueue = new ScoreImportQueue((path, context) => controller.Open(path, null, context));
            OpenedScore? applied = null;
            var appliedOnUi = false;
            var realJob = realQueue.Start(gp, (_, r) => { applied = r; appliedOnUi = Environment.CurrentManagedThreadId == uiThread; }, (_, _) => { });
            PumpUntil(() => realJob.Completion.IsCompleted, 30_000);
            Check("import containment: the queued import is applied once, on the UI thread, with the same song",
                applied is { } a && appliedOnUi && SameImportedSong(a.Project, expected) && realQueue.Pending.Count == 0,
                $"applied {applied is not null}, on UI {appliedOnUi}, pending {realQueue.Pending.Count}");

            var workerSawCancel = false;
            var cancelQueue = new ScoreImportQueue((path, context) =>
            {
                try
                {
                    for (var i = 0; i < 500; i++) { context.Check(); Thread.Sleep(10); }
                }
                catch (OperationCanceledException) { workerSawCancel = true; throw; }
                return controller.Open(path);
            });
            var cancelApplied = false;
            var cancelReported = false;
            Exception? cancelError = new InvalidOperationException("not reported");
            var cancelJob = cancelQueue.Start(gp, (_, _) => cancelApplied = true, (_, error) => { cancelReported = true; cancelError = error; });
            var pendingWhileRunning = cancelQueue.Pending.Count;
            PumpUntil(() => false, 100);
            var cancelWatch = Stopwatch.StartNew();
            cancelQueue.Cancel(cancelJob);
            PumpUntil(() => cancelJob.Completion.IsCompleted, 5_000);
            var cancelMs = cancelWatch.ElapsedMilliseconds;
            PumpUntil(() => workerSawCancel, 2_000);
            Check("import containment: Cancel ends the import at once and leaves no document (nothing applied, reported as cancelled)",
                pendingWhileRunning == 1 && cancelJob.Completion.IsCompleted && !cancelApplied && cancelReported && cancelError is null &&
                cancelQueue.Pending.Count == 0 && workerSawCancel && cancelMs < 2_000, $"{cancelMs} ms, worker saw cancel: {workerSawCancel}");

            var failQueue = new ScoreImportQueue((_, _) => throw new InvalidDataException("broken"));
            var failApplied = false;
            Exception? failError = null;
            var failJob = failQueue.Start(gp, (_, _) => failApplied = true, (_, error) => failError = error);
            PumpUntil(() => failJob.Completion.IsCompleted, 5_000);
            Check("import containment: a failed import applies nothing and reports its error",
                !failApplied && failError is InvalidDataException { Message: "broken" } && failQueue.Pending.Count == 0);

            // A6-04: six imports at once run at most two at a time and apply in start order; Cancel all drops the waiting ones.
            var running = 0;
            var peak = 0;
            var opens = 0;
            var poolQueue = new ScoreImportQueue((path, context) =>
            {
                Interlocked.Increment(ref opens);
                var now = Interlocked.Increment(ref running);
                int seen;
                while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
                Thread.Sleep(150);
                Interlocked.Decrement(ref running);
                return controller.Open(path);
            });
            var order = new List<int>();
            var poolJobs = Enumerable.Range(0, 6).Select(i => poolQueue.Start(gp, (_, _) => order.Add(i), (_, _) => order.Add(-1 - i))).ToArray();
            var pendingAtStart = poolQueue.Pending.Count;
            PumpUntil(() => poolJobs.All(j => j.Completion.IsCompleted), 20_000);
            Check("import containment: six simultaneous imports never run more than two at once and all apply in order",
                pendingAtStart == 6 && peak == 2 && opens == 6 && order.SequenceEqual(Enumerable.Range(0, 6)) && poolQueue.Pending.Count == 0,
                $"peak {peak}, opens {opens}, order [{string.Join(",", order)}]");

            opens = 0;
            var cancelPoolQueue = new ScoreImportQueue((path, context) =>
            {
                Interlocked.Increment(ref opens);
                for (var i = 0; i < 100; i++) { context.Check(); Thread.Sleep(10); }
                return controller.Open(path);
            });
            var cancelPoolApplied = 0;
            var cancelPoolReported = 0;
            var cancelPoolJobs = Enumerable.Range(0, 5).Select(_ => cancelPoolQueue.Start(gp, (_, _) => cancelPoolApplied++,
                (_, error) => { if (error is null) cancelPoolReported++; })).ToArray();
            PumpUntil(() => Volatile.Read(ref opens) >= 2, 5_000);
            cancelPoolQueue.CancelAll();   // what closing the window does
            PumpUntil(() => cancelPoolJobs.All(j => j.Completion.IsCompleted), 5_000);
            PumpUntil(() => false, 200);
            Check("import containment: Cancel all ends the running imports and the waiting ones never start",
                cancelPoolJobs.All(j => j.Completion.IsCompleted) && cancelPoolApplied == 0 && cancelPoolReported == 5 &&
                opens == 2 && cancelPoolQueue.Pending.Count == 0, $"opens {opens}, applied {cancelPoolApplied}, reported {cancelPoolReported}");

            // A synthetic slow import (like a long alphaTab parse: no cooperative checks) must not block the calling thread.
            var slowQueue = new ScoreImportQueue((path, context) => { Thread.Sleep(600); return controller.Open(path); });
            var ticks = 0;
            var heartbeat = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Normal, (_, _) => ticks++, Dispatcher.CurrentDispatcher);
            var startWatch = Stopwatch.StartNew();
            var slowApplied = false;
            var slowJob = slowQueue.Start(gp, (_, _) => slowApplied = true, (_, _) => { });
            var startMs = startWatch.ElapsedMilliseconds;
            heartbeat.Start();
            PumpUntil(() => slowJob.Completion.IsCompleted, 10_000);
            heartbeat.Stop();
            Check("import containment: a slow import leaves the UI thread free (Start returns at once, the dispatcher keeps ticking)",
                startMs < 100 && slowApplied && ticks >= 10, $"start {startMs} ms, {ticks} heartbeat ticks during a 600 ms import");
        }
    }

    private static void RunImportBudgetChecks(DocumentController controller, string gp)
    {
        {
            var stuck = ScoreImportQueue.ImportAsync(gp, (path, context) => { Thread.Sleep(3_000); return controller.Open(path); }, CancellationToken.None,
                TimeSpan.FromMilliseconds(200), ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromMilliseconds(200));
            var stuckWatch = Stopwatch.StartNew();
            var stuckTimedOut = false;
            try { stuck.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException ex) when (ex.InnerException is TimeoutException) { stuckTimedOut = true; }
            Check("import containment: an import stuck past its time budget is abandoned with a clear error",
                stuckTimedOut && stuckWatch.ElapsedMilliseconds < 2_000, $"{stuckWatch.ElapsedMilliseconds} ms");

            var slowCooperative = ScoreImportQueue.ImportAsync(gp, (path, context) => { for (var i = 0; i < 300; i++) { context.Check(); Thread.Sleep(10); } return controller.Open(path); },
                CancellationToken.None, TimeSpan.FromMilliseconds(150), ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
            string? budgetMessage = null;
            try { slowCooperative.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException ex) when (ex.InnerException is InvalidDataException inner) { budgetMessage = inner.Message; }
            Check("import containment: the time budget stops the import at the next stage check",
                budgetMessage?.Contains("took longer than", StringComparison.Ordinal) == true, budgetMessage);

            var memoryGuard = new ImportGuard(CancellationToken.None, TimeSpan.FromMinutes(1), memoryBudgetBytes: 1024 * 1024);
            var ballast = new byte[16 * 1024 * 1024];
            ballast[^1] = 1;
            var memoryStopped = ThrowsInvalidData(memoryGuard.Check, out var memoryMessage) && memoryMessage.Contains("memory", StringComparison.Ordinal);
            GC.KeepAlive(ballast);
            Check("import containment: the managed-memory watermark stops the import", memoryStopped, memoryMessage);
            Check("import containment: no guard outside a background import (headless tools stay synchronous and unbounded by time)",
                new ImportContext().Guard is null);
        }
    }

    /// <summary>Runs the dispatcher until <paramref name="done"/> or the timeout (async continuations posted here run meanwhile).</summary>
    private static void PumpUntil(Func<bool> done, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < timeoutMs)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background,
                (sender, _) => { ((DispatcherTimer)sender!).Stop(); frame.Continue = false; }, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>Same song content; track ids are fresh per import, so they are taken from <paramref name="expected"/> first.</summary>
    private static bool SameImportedSong(SongProject actual, SongProject expected)
    {
        if (actual.Tracks.Count != expected.Tracks.Count) return false;
        for (var i = 0; i < actual.Tracks.Count; i++) actual.Tracks[i].Id = expected.Tracks[i].Id;
        return ProjectService.ContentHash(actual).AsSpan().SequenceEqual(ProjectService.ContentHash(expected));
    }
}
