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
    /// The out-of-process import worker. A song imported through it equals the in-process import; a worker stuck inside the
    /// parse is killed by Cancel and by the time budget and its process is gone; an oversize result is refused; when the worker
    /// or its Job Object cannot be set up, nothing is parsed in-process unless the user agrees for that file.
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
            try { viaWorker = ImportWorker.Import(gp); }
            catch (Exception ex) { workerError = ex.GetType().Name + ": " + ex.Message; }
            Check("import worker: a .gp imported in the worker process equals the in-process import (no fallback used)",
                viaWorker is not null && notices.Count == 0 && SameImportedSong(viaWorker, direct.Project), workerError ?? string.Join("; ", notices));

            var queued = ScoreImportQueue.ImportAsync(gp, (path, context) => controller.Open(path, (file, _) => ImportWorker.Import(file, null, null, context), context),
                CancellationToken.None, ImportGuard.DefaultTimeBudget, ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
            var queuedDone = queued.Wait(TimeSpan.FromSeconds(60));
            Check("import worker: the background import through the worker gives the same opened score",
                queuedDone && queued.Result.ImportedFromGuitarPro && queued.Result.Notice is null && SameImportedSong(queued.Result.Project, direct.Project));

            // 2. A worker stuck in the parse: Cancel kills it and its process is gone.
            Process? hung = null;
            var hangOptions = new ImportWorkerOptions { TestHang = true, Started = p => hung = Process.GetProcessById(p.Id) };
            using (var cancel = new CancellationTokenSource())
            {
                var stuck = ScoreImportQueue.ImportAsync(gp, (path, context) => controller.Open(path, (file, _) => ImportWorker.Import(file, hangOptions, null, context), context),
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
            var timed = ScoreImportQueue.ImportAsync(gp, (path, context) => controller.Open(path, (file, _) => ImportWorker.Import(file, budgetOptions, null, context), context),
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

            // 4. The worker cannot start, or its Job Object cannot be created / assigned: never a silent in-process parse.
            var missing = new ImportWorkerOptions { ExecutablePath = Path.Combine(folder, "missing-worker.exe") };
            Check("import worker: a missing worker program, a failed job creation and a failed job assignment each report 'unavailable'",
                ThrowsUnavailable(gp, missing) && ThrowsUnavailable(gp, new ImportWorkerOptions { TestFailJobCreate = true }) &&
                ThrowsUnavailable(gp, new ImportWorkerOptions { TestFailJobAssign = true }));
            hung = null;
            try { ImportWorker.Import(gp, new ImportWorkerOptions { TestFailJobAssign = true, Started = p => hung = Process.GetProcessById(p.Id) }); }
            catch (ImportWorkerUnavailableException) { }
            Check("import worker: a worker that could not be assigned to its job never receives the file (Started seam not reached)", hung is null);
            Task.Run(() => ConsentChecks(controller, gp, direct.Project)).Wait(TimeSpan.FromSeconds(90));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool ThrowsUnavailable(string gp, ImportWorkerOptions options)
    {
        try { ImportWorker.Import(gp, options); return false; }
        catch (ImportWorkerUnavailableException) { return true; }
    }

    /// <summary>Through the queue (no synchronization context: callbacks run on the pool): prompt per file, default no.</summary>
    private static void ConsentChecks(DocumentController controller, string gp, SongProject expected)
    {
        var failAssign = new ImportWorkerOptions { TestFailJobAssign = true };
        foreach (var consent in new[] { false, true })
        {
            var inProcessParses = 0;
            var prompts = new List<string>();
            var queue = new ScoreImportQueue((path, context) => controller.Open(path, (file, _) => ImportWorker.Import(file, failAssign, null, context), context), 2,
                (path, reason, context) => { Interlocked.Increment(ref inProcessParses); return controller.Open(path, (file, n) => ImportWorker.ImportInProcess(file, n, reason, context), context); })
            { ConfirmInProcess = (job, reason) => { lock (prompts) prompts.Add(job.Name + ": " + reason); return consent; } };
            OpenedScore? applied = null;
            var failed = false;
            Exception? failError = null;
            var job = queue.Start(gp, (_, r) => applied = r, (_, e) => { failed = true; failError = e; });
            var done = job.Completion.Wait(TimeSpan.FromSeconds(30));
            if (!consent)
                Check("import worker: a job-assign failure asks the user, and 'No' opens nothing and parses nothing in-process",
                    done && prompts.Count == 1 && prompts[0].Contains("limits", StringComparison.Ordinal) && inProcessParses == 0 &&
                    applied is null && failed && failError is null, $"done {done}, prompts [{string.Join("; ", prompts)}], in-process {inProcessParses}");
            else
                Check("import worker: 'Yes' opens that one file in-process with a notice and the same song",
                    done && prompts.Count == 1 && inProcessParses == 1 && applied is { } a && SameImportedSong(a.Project, expected) &&
                    a.Notice?.Contains("could not start", StringComparison.Ordinal) == true, $"done {done}, in-process {inProcessParses}, failed {failed}");
        }

        // No consent callback at all (e.g. a caller without UI): not opened.
        var silentParses = 0;
        var silent = new ScoreImportQueue((path, context) => controller.Open(path, (file, _) => ImportWorker.Import(file, failAssign, null, context), context), 2,
            (path, reason, context) => { Interlocked.Increment(ref silentParses); return controller.Open(path, null, context); });
        var silentApplied = false;
        var silentJob = silent.Start(gp, (_, _) => silentApplied = true, (_, _) => { });
        Check("import worker: without a consent prompt the unprotected parse never runs",
            silentJob.Completion.Wait(TimeSpan.FromSeconds(30)) && !silentApplied && silentParses == 0);
    }
}
