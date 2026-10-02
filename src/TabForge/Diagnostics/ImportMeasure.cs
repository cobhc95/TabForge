using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Diagnostics;

/// <summary>
/// `--import-measure &lt;song&gt; &lt;report.txt&gt; [worker|inproc]`: opens a Guitar Pro file the way the app does (worker = out-of-process import and the
/// hand-off back, inproc = the parser inside this process) and writes what it cost: bars, tracks, cells, notes, the serialised size of the
/// project in each form and the peak memory of every process involved. For finding what makes a long, dense song too big; headless.
/// </summary>
internal static class ImportMeasure
{
    internal static int Run(string[] args)
    {
        var song = FilePathPolicy.ExistingFile(args[1], "Guitar Pro file", GuitarProImporter.SupportedExtensions);
        var report = FilePathPolicy.OutputFile(args[2], "report", ".txt");
        var mode = args.Length > 3 ? args[3] : "worker";
        var text = new StringBuilder();
        void Line(string s) { text.AppendLine(s); Console.Out.WriteLine(s); }
        Line($"file: {Path.GetFileName(song)} ({new FileInfo(song).Length:N0} bytes), mode {mode}");
        var watch = Stopwatch.StartNew();
        SongProject? project = null;
        long workerPeakWorkingSet = 0, workerPeakPrivate = 0;
        try
        {
            if (mode == "worker")
            {
                var options = new ImportWorkerOptions
                {
                    Started = p =>
                    {
                        var id = p.Id;
                        new Thread(() =>
                        {
                            try
                            {
                                using var worker = Process.GetProcessById(id);
                                while (!worker.HasExited)
                                {
                                    worker.Refresh();
                                    workerPeakWorkingSet = Math.Max(workerPeakWorkingSet, worker.PeakWorkingSet64);
                                    workerPeakPrivate = Math.Max(workerPeakPrivate, worker.PrivateMemorySize64);
                                    Thread.Sleep(15);
                                }
                            }
                            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
                        }) { IsBackground = true }.Start();
                    },
                };
                project = ImportWorker.Import(song, options);
            }
            else project = GuitarProImporter.Import(song);
            Line($"import: ok in {watch.Elapsed.TotalSeconds:0.0} s");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Line($"import: FAILED after {watch.Elapsed.TotalSeconds:0.0} s: {ex.GetType().Name}: {ex.Message}");
        }
        if (workerPeakWorkingSet > 0) Line($"worker process: peak working set {workerPeakWorkingSet / 1048576.0:0} MiB, peak private bytes {workerPeakPrivate / 1048576.0:0} MiB");

        if (mode == "inproc" && project is not null)
        {
            long cells = 0, notEmpty = 0, notes = 0, voice2 = 0;
            foreach (var track in project.Tracks)
                foreach (var measure in track.Measures)
                    foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    {
                        cells++;
                        if (cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation) notEmpty++;
                        notes += cell.Notes.Count;
                    }
            voice2 = project.Tracks.Sum(t => t.Measures.Sum(m => m.Voice2Cells.Count));
            var voice2Content = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Voice2Cells.Count(c => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation)));
            var voice2Bars = project.Tracks.Sum(t => t.Measures.Count(m => m.Voice2Cells.Count > 0));
            var voice2BarsWithContent = project.Tracks.Sum(t => t.Measures.Count(m => m.Voice2Cells.Any(c => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation)));
            Line($"second voice: {voice2Bars:N0} bars have one, {voice2BarsWithContent:N0} of them with content ({voice2Content:N0} cells with content)");
            Line($"model: {project.Tracks.Count} tracks, {project.Tracks.Max(t => t.Measures.Count):N0} bars, {project.Tracks.Sum(t => t.Measures.Count):N0} bars in all, {cells:N0} cells ({voice2:N0} in second voices), {notEmpty:N0} with content, {notes:N0} notes");
            var (full, compact) = ProjectService.MeasureJsonBytes(project);
            Line($"JSON size: disk form (every property) {full:N0} bytes = {full / 1048576.0:0.0} MiB; compact form {compact:N0} bytes = {compact / 1048576.0:0.0} MiB; limit {InputLimits.MaxTforgeFileBytes / 1048576} MiB");
            try { var bytes = ProjectService.PersistBytes(project); Line($"PersistBytes (the worker's reply, embedded .gp snapshot): ok, {bytes.Length:N0} bytes compressed"); }
            catch (InvalidDataException ex) { Line($"PersistBytes: {ex.Message}"); }
        }
        using (var self = Process.GetCurrentProcess())
            Line($"this process: peak working set {self.PeakWorkingSet64 / 1048576.0:0} MiB, private bytes {self.PrivateMemorySize64 / 1048576.0:0} MiB, managed heap {GC.GetTotalMemory(false) / 1048576.0:0} MiB");
        File.WriteAllText(report, text.ToString(), new UTF8Encoding(false));
        return project is null ? 1 : 0;
    }
}
