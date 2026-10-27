using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Playback;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the opt-in corpus counting probe: for every Guitar Pro file, the attacks the written notes call for against the NoteEvents TabForge compiles,
//   grouped by cause (the Guitar Pro parity audit, "Counting probe"). It asserts nothing: it is a measuring run, not a regression test.
// Does not own: the counting (CorpusProbeWritten, CorpusProbeActual) or the output (CorpusProbeReport).
// Env: TABFORGE_CORPUS_PROBE=1 to run (else it returns at once); TABFORGE_SAMPLES_DIR = the folder to scan (default: Samples\corpus);
//   TABFORGE_CORPUS_PROBE_OUT = where attacks.csv, causes.csv and summary.txt go (default: the temp folder, opencode\gp-parity).
public class CorpusProbeTests
{
    private static readonly string[] Extensions = { ".gp3", ".gp4", ".gp5", ".gpx", ".gp", ".gtp" };
    private readonly ITestOutputHelper _output;

    public CorpusProbeTests(ITestOutputHelper output) { _output = output; AlphaTabWarmUp.EnsureReady(); }

    [Fact]
    public void Count_every_written_attack_against_the_compiled_notes()
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_CORPUS_PROBE") != "1") return;
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine("Samples", "corpus");
        var outDir = Environment.GetEnvironmentVariable("TABFORGE_CORPUS_PROBE_OUT") ?? Path.Combine(Path.GetTempPath(), "opencode", "gp-parity");
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        using var report = new CorpusProbeReport(outDir);
        // Files are independent; only the report is shared, so its updates are serialised.
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 2) }, path =>
        {
            var name = Path.GetRelativePath(dir, path).Replace(Path.DirectorySeparatorChar, '/');
            try { CountFile(report, path, name); }
            catch (Exception ex) { lock (report) report.AddFailure(name, ex.GetType().Name + ": " + ex.Message); }
        });
        var summary = report.Finish();
        _output.WriteLine(summary);
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary);
    }

    private static void CountFile(CorpusProbeReport report, string path, string name)
    {
        var context = new ImportContext();
        var song = GuitarProImporter.Import(path, context);
        var options = new PlaybackOptions { RespectMuteSolo = false, RepeatExpansion = true, StartBar = 0, Speed = 1, CountIn = false, Metronome = false, SkipClips = true };
        var timeline = MidiTimelineBuilder.Build(song, options);
        var written = new CorpusProbeWritten(song, timeline);
        _ = new CorpusProbeActual(timeline, written);
        var bars = song.Tracks.Count == 0 ? 0 : song.Tracks.Max(t => t.Measures.Count);
        lock (report)
        {
            report.AddFile(name, written.Tallies);
            report.AddFileCause(name, ProbeCause.DupDrop, context.SkippedDuplicates);
            if (timeline.Bars.Count >= bars * 101L) report.AddFileCause(name, ProbeCause.Truncated, 1);
        }
    }
}
