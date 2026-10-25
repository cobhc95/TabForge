using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: background import-worker self-test.

public static partial class SelfTest
{
    /// <summary>
    /// The out-of-process import worker. A song imported through it equals the in-process import; a worker stuck inside the
    /// parse is killed by Cancel and by the time budget and its process is gone; an oversize result is refused; when the worker
    /// or its Job Object cannot be set up, the import reports it instead of parsing in-process.
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

            ImportWorkerEqualsInProcess(gp, direct.Project);
            ImportWorkerCancelKills(gp);
            ImportWorkerBudgetAndLimits(gp, folder);
            ImportWorkerUnavailable(gp, folder);
        }
        finally
        {
            TryDelete(folder);
        }
    }

    /// <summary>1. Through the worker == in-process.</summary>
    private static void ImportWorkerEqualsInProcess(string gp, SongProject directProject)
    {
        var notices = new List<string>();
        SongProject? viaWorker = null;
        string? workerError = null;
        try { viaWorker = ImportWorker.Import(gp); }
        catch (Exception ex) { workerError = ex.GetType().Name + ": " + ex.Message; }
        Check("import worker: a .gp imported in the worker process equals the in-process import (no fallback used)",
            viaWorker is not null && notices.Count == 0 && SameImportedSong(viaWorker, directProject), workerError ?? string.Join("; ", notices));
    }

    /// <summary>2. A worker stuck in the parse: Cancel kills it and its process is gone.</summary>
    private static void ImportWorkerCancelKills(string gp)
    {
        Process? hung = null;
        var hangOptions = new ImportWorkerOptions { TestHang = true, Started = p => hung = Process.GetProcessById(p.Id) };
        using (var cancel = new CancellationTokenSource())
        {
            var stuck = Task.Run(() => ImportWorker.Import(gp, hangOptions, null, new ImportContext(new ImportGuard(cancel.Token))));
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
    }

    /// <summary>2b. The time budget kills a stuck worker too; 3. an oversize result is refused; a worker error arrives as the message of the importer.</summary>
    private static void ImportWorkerBudgetAndLimits(string gp, string folder)
    {
        Process? hung = null;
    hung = null;
    var budgetOptions = new ImportWorkerOptions { TestHang = true, KillGrace = TimeSpan.FromMilliseconds(100), Started = p => hung = Process.GetProcessById(p.Id) };
    var timed = Task.Run(() => ImportWorker.Import(gp, budgetOptions, null, new ImportContext(new ImportGuard(CancellationToken.None, TimeSpan.FromMilliseconds(1_500)))));
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
    }

    /// <summary>4. The worker cannot start, or its Job Object cannot be created / assigned: never a silent in-process parse.</summary>
    private static void ImportWorkerUnavailable(string gp, string folder)
    {
        Process? hung;
    var missing = new ImportWorkerOptions { ExecutablePath = Path.Combine(folder, "missing-worker.exe") };
    Check("import worker: a missing worker program, a failed job creation and a failed job assignment each report 'unavailable'",
        ThrowsUnavailable(gp, missing) && ThrowsUnavailable(gp, new ImportWorkerOptions { TestFailJobCreate = true }) &&
        ThrowsUnavailable(gp, new ImportWorkerOptions { TestFailJobAssign = true }));
    hung = null;
    try { ImportWorker.Import(gp, new ImportWorkerOptions { TestFailJobAssign = true, Started = p => hung = Process.GetProcessById(p.Id) }); }
    catch (ImportWorkerUnavailableException) { } // swallow: the unavailable worker is the expected outcome; asserted below
    Check("import worker: a worker that could not be assigned to its job never receives the file (Started seam not reached)", hung is null);
    }

    private static bool ThrowsUnavailable(string gp, ImportWorkerOptions options)
    {
        try { ImportWorker.Import(gp, options); return false; }
        catch (ImportWorkerUnavailableException) { return true; }
    }
}
