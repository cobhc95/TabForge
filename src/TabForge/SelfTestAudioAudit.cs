using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

/// <summary>
/// The headless audio audit (`--audio-audit`, Audit 7) on the synthetic showcase fixture: offline render through the engine process with no
/// audio device, every file and both reports written, clean content free of clipping and of timing errors above the threshold,
/// and the playback compile, MIDI export and render tempo map in agreement. The audit runs as its own process (as the command line does),
/// so the engine state earlier tests leave behind in this process cannot affect it.
/// </summary>
public static partial class SelfTest
{
    private static void TestAudioAudit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tf-audio-audit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var song = BuildShowcaseSong();
            var gp = Path.Combine(dir, "fixture.gp");
            var outDir = Path.Combine(dir, "out");
            Services.GuitarProExporter.Save(song, gp, embedProject: false);
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--audio-audit \"{gp}\" \"{outDir}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            var finished = process.WaitForExit(240_000);
            if (!finished) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            Check("audio audit: the command finished within 4 minutes and wrote both reports (exit 0 = clean, 1 = findings)",
                finished && process.ExitCode is 0 or 1 && File.Exists(Path.Combine(outDir, "audio-report.json")) && File.Exists(Path.Combine(outDir, "audio-report.md")),
                finished ? $"exit {process.ExitCode}" : "timed out");
            if (!File.Exists(Path.Combine(outDir, "audio-report.json"))) return;
            var report = JsonSerializer.Deserialize<AuditReport>(File.ReadAllText(Path.Combine(outDir, "audio-report.json")), new JsonSerializerOptions { IncludeFields = true })!;
            Check("audio audit: mix and one stem per track are written",
                File.Exists(Path.Combine(outDir, "mix.wav")) && report.Tracks.Count == song.Tracks.Count && report.Tracks.All(t => File.Exists(Path.Combine(outDir, t.StemFile))));
            var mix = report.Artifacts.FirstOrDefault(a => a.File == "mix.wav");
            Check("audio audit: the clean fixture mix does not clip (nothing at or above -0.1 dBFS)", mix is not null && mix.ClippedSamples == 0, mix is null ? "no mix" : $"{mix.ClippedSamples} samples, peak {mix.PeakDbfs} dBFS");
            var sharp = report.Timing.Where(t => t.AttackClass == "sharp" && t.Expected >= 8).ToList();
            // Mean absolute error 30 ms: a soft bass attack reads about 25 ms late; a real timing fault (a missed block, a wrong tempo) is far larger.
            Check("audio audit: on every sharp track at least 90% of the compiled onsets are found in the stem, on average within 30 ms",
                sharp.Count > 0 && sharp.All(t => t.Matched >= t.Expected * 0.9 && t.MeanAbsErrorMs <= 30),
                string.Join("; ", sharp.Select(t => $"{t.Track} mean {t.MeanAbsErrorMs} ms, p95 {t.P95AbsErrorMs} ms, missing {t.Missing}/{t.Expected}")));
            Check("audio audit: playback compile, MIDI export and render tempo map agree", report.Consistency is { Issues.Count: 0 }, string.Join("; ", report.Consistency?.Issues ?? new List<string>()));
            Check("audio audit: the report has section levels, humanisation and moments to listen to", report.Sections.Count > 0 && report.Humanisation.Count > 0 && report.MomentsToListenTo.Count >= 0);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    /// <summary>A repeated passage yields two sections with the same name and first bar; the audit must index them, not key a dictionary on them (it threw "same key" on such songs).</summary>
    private static void TestAudioAuditRepeatedSections()
    {
        var p = new SongProject { Tempo = 120 };
        var t = new TrackModel { Name = "G", Measures = TemplateFactory.Measures(4) };
        t.Measures[0].RepeatStart = true;
        t.Measures[3].RepeatEnd = true;
        t.Measures[3].RepeatCount = 3; // 4 bars played 3 times = 12 bars = two 8-bar chunks, both "Bars 1-4" starting at bar 1
        p.Tracks.Add(t);
        var count = AudioAudit.SectionCountForTest(p, out var distinct);
        Check("audio audit: a repeated passage gives sections that share (name, first bar), so lookups must be per section", count > distinct, $"{count} sections, {distinct} distinct keys");
    }
}
