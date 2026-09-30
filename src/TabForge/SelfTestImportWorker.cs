using System.Diagnostics;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// A5-07: the out-of-process import worker. A song imported through it equals the in-process import; a worker stuck inside the
    /// parse is killed by Cancel and by the time budget and its process is gone; an oversize result is refused; when the worker
    /// cannot start, the import runs in-process and says so.
    /// </summary>
    private static void TestGuitarProImportWorker()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-import-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var song = new SongProject { Title = "Worker", Tempo = 121, Artist = "Self-test" };
            song.Tracks.Add(new TrackModel { Name = "Lead", Measures = TemplateFactory.Measures(6) });
            song.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 5 });
            song.Tracks[0].Measures[3].Cells[8].Notes.Add(new TabNote { StringIndex = 4, Fret = 12 });
            song.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(6) });
            var gp = Path.Combine(folder, "worker.gp");
            GuitarProExporter.Save(song, gp, embedProject: false);
            var controller = new DocumentController();
            var direct = controller.Open(gp);

            // 1. Through the worker == in-process.
            var notices = new List<string>();
            SongProject? viaWorker = null;
            string? workerError = null;
            try { viaWorker = ImportWorker.ImportOrFallback(gp, notices); }
            catch (Exception ex) { workerError = ex.GetType().Name + ": " + ex.Message; }
            Check("import worker: a .gp imported in the worker process equals the in-process import (no fallback used)",
                viaWorker is not null && notices.Count == 0 && SameImportedSong(viaWorker, direct.Project), workerError ?? string.Join("; ", notices));

            var queued = ScoreImportQueue.ImportAsync(gp, path => controller.Open(path, (file, n) => ImportWorker.ImportOrFallback(file, n)),
                CancellationToken.None, ImportGuard.DefaultTimeBudget, ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
            var queuedDone = queued.Wait(TimeSpan.FromSeconds(60));
            Check("import worker: the background import through the worker gives the same opened score",
                queuedDone && queued.Result.ImportedFromGuitarPro && queued.Result.Notice is null && SameImportedSong(queued.Result.Project, direct.Project));

            // 2. A worker stuck in the parse: Cancel kills it and its process is gone.
            Process? hung = null;
            var hangOptions = new ImportWorkerOptions { TestHang = true, Started = p => hung = Process.GetProcessById(p.Id) };
            using (var cancel = new CancellationTokenSource())
            {
                var stuck = ScoreImportQueue.ImportAsync(gp, path => controller.Open(path, (file, n) => ImportWorker.ImportOrFallback(file, n, hangOptions)),
                    cancel.Token, ImportGuard.DefaultTimeBudget, ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
                var startWatch = Stopwatch.StartNew();
                while (hung is null && startWatch.ElapsedMilliseconds < 15_000) Thread.Sleep(20);
                Thread.Sleep(300);   // the worker has the file and is "parsing"
                var aliveBeforeCancel = hung is { HasExited: false };
                var cancelWatch = Stopwatch.StartNew();
                cancel.Cancel();
                var cancelled = false;
                try { stuck.Wait(TimeSpan.FromSeconds(5)); }
                catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { cancelled = true; }
                var gone = hung?.WaitForExit(5_000) == true;
                Check("import worker: Cancel kills a worker stuck in the parse and its process is gone",
                    aliveBeforeCancel && cancelled && gone && cancelWatch.ElapsedMilliseconds < 5_000,
                    $"alive before {aliveBeforeCancel}, cancelled {cancelled}, gone {gone}, {cancelWatch.ElapsedMilliseconds} ms");
                hung?.Dispose();
            }

            // 2b. The time budget kills a stuck worker too.
            hung = null;
            var budgetOptions = new ImportWorkerOptions { TestHang = true, KillGrace = TimeSpan.FromMilliseconds(100), Started = p => hung = Process.GetProcessById(p.Id) };
            var timed = ScoreImportQueue.ImportAsync(gp, path => controller.Open(path, (file, n) => ImportWorker.ImportOrFallback(file, n, budgetOptions)),
                CancellationToken.None, TimeSpan.FromMilliseconds(1_500), ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(3));
            var timedOut = false;
            try { timed.Wait(TimeSpan.FromSeconds(15)); }
            catch (AggregateException ex) when (ex.InnerException is TimeoutException) { timedOut = true; }
            var timedGone = hung?.WaitForExit(5_000) == true;
            Check("import worker: the time budget kills a stuck worker and its process is gone", timedOut && timedGone,
                $"timed out {timedOut}, gone {timedGone}");
            hung?.Dispose();

            // 3. An oversize result is refused (and the worker is gone).
            hung = null;
            var tinyOptions = new ImportWorkerOptions { MaxResultBytes = 64, Started = p => hung = Process.GetProcessById(p.Id) };
            string? oversize = null;
            try { ImportWorker.Import(gp, tinyOptions); }
            catch (InvalidDataException ex) { oversize = ex.Message; }
            Check("import worker: a result above the size limit is refused",
                oversize?.Contains("larger than", StringComparison.Ordinal) == true && hung?.WaitForExit(5_000) == true, oversize);
            hung?.Dispose();

            // A worker error arrives as the importer's own message.
            var broken = Path.Combine(folder, "broken.gp5");
            var header = new byte[256];
            var signature = System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v9.99");
            header[0] = (byte)signature.Length;
            signature.CopyTo(header, 1);
            File.WriteAllBytes(broken, header);
            string? brokenMessage = null;
            try { ImportWorker.Import(broken); }
            catch (InvalidDataException ex) { brokenMessage = ex.Message; }
            Check("import worker: an import error in the worker reaches the caller with the importer's message",
                brokenMessage?.Contains("damaged header", StringComparison.Ordinal) == true, brokenMessage);

            // 4. The worker cannot start: the in-process background import runs instead, with a notice.
            var missing = new ImportWorkerOptions { ExecutablePath = Path.Combine(folder, "missing-worker.exe") };
            var fallbackNotices = new List<string>();
            var fallback = ImportWorker.ImportOrFallback(gp, fallbackNotices, missing);
            var fallbackQueued = ScoreImportQueue.ImportAsync(gp, path => controller.Open(path, (file, n) => ImportWorker.ImportOrFallback(file, n, missing)),
                CancellationToken.None, ImportGuard.DefaultTimeBudget, ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
            var fallbackDone = fallbackQueued.Wait(TimeSpan.FromSeconds(30));
            Check("import worker: when the worker cannot start, the import runs in-process with a notice and the same song",
                SameImportedSong(fallback, direct.Project) && fallbackNotices.Count == 1 &&
                fallbackDone && fallbackQueued.Result.Notice?.Contains("could not start", StringComparison.Ordinal) == true &&
                SameImportedSong(fallbackQueued.Result.Project, direct.Project), string.Join("; ", fallbackNotices));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
