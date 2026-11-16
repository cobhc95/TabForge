using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Playback;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the opt-in import and compile timing over the sample corpus (performance audit 2026-11): each file's import, the
//   worker hand-off round trip (TransferBytes / RestoreTransferBytes, what an open pays after the worker) and the playback
//   compile, plus memory growth across repeated opens of the largest song. It prints numbers and asserts nothing about speed.
// Does not own: the counting probe (CorpusProbeTests), the engine runs (EnginePerfStressTests).
// Env: TABFORGE_PERF=1 to run (else it returns at once); TABFORGE_SAMPLES_DIR = the folder to scan (default: the repo's samples).
public sealed class CorpusPerfTests
{
    private static readonly string[] Extensions = { ".gp3", ".gp4", ".gp5", ".gpx", ".gp" };   // .gtp is not importable (the corpus holds 8)
    private readonly ITestOutputHelper _output;

    public CorpusPerfTests(ITestOutputHelper output) { _output = output; AlphaTabWarmUp.EnsureReady(); }

    private static string? SamplesDir()
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_PERF") != "1") return null;
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        if (!string.IsNullOrWhiteSpace(dir)) return dir;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "samples")) && Directory.Exists(Path.Combine(d.FullName, "Source"))) return Path.Combine(d.FullName, "samples");
        return null;
    }

    private static List<string> Files(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
        .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToList();

    private static PlaybackOptions Options() => new() { RespectMuteSolo = false, RepeatExpansion = true, StartBar = 0, Speed = 1, CountIn = false, Metronome = false };

    [Fact]
    public void Import_and_compile_every_sample()
    {
        if (SamplesDir() is not { } dir) return;
        var files = Files(dir);
        var rows = new List<(string Name, double Import, double Transfer, double Compile, int Bars, int Tracks, long Alloc)>();
        var failures = new List<string>();
        GC.Collect();
        var gc = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var total = Stopwatch.StartNew();
        foreach (var path in files)
        {
            var name = Path.GetRelativePath(dir, path);
            try
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var t = Stopwatch.GetTimestamp();
                var song = GuitarProImporter.Import(path, new ImportContext());
                var import = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                t = Stopwatch.GetTimestamp();
                song = ProjectService.RestoreTransferBytes(ProjectService.TransferBytes(song));
                var transfer = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                t = Stopwatch.GetTimestamp();
                var timeline = MidiTimelineBuilder.Build(song, Options());
                var compile = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                rows.Add((name, import, transfer, compile, timeline.Bars.Count, song.Tracks.Count, GC.GetAllocatedBytesForCurrentThread() - a0));
            }
            catch (Exception ex) { failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}"); }
        }
        total.Stop();
        string Pct(Func<(string Name, double Import, double Transfer, double Compile, int Bars, int Tracks, long Alloc), double> f, int p)
        {
            var v = rows.Select(f).OrderBy(x => x).ToArray();
            return v.Length == 0 ? "-" : v[Math.Min(v.Length - 1, v.Length * p / 100)].ToString("F1");
        }
        _output.WriteLine($"{files.Count} files, {rows.Count} ok, {failures.Count} failed, {total.Elapsed.TotalSeconds:F1} s in all (one thread)");
        _output.WriteLine($"import   sum {rows.Sum(r => r.Import) / 1000:F1} s, p50 {Pct(r => r.Import, 50)} ms, p99 {Pct(r => r.Import, 99)} ms, max {rows.Max(r => r.Import):F0} ms");
        _output.WriteLine($"transfer sum {rows.Sum(r => r.Transfer) / 1000:F1} s, p50 {Pct(r => r.Transfer, 50)} ms, p99 {Pct(r => r.Transfer, 99)} ms, max {rows.Max(r => r.Transfer):F0} ms");
        _output.WriteLine($"compile  sum {rows.Sum(r => r.Compile) / 1000:F1} s, p50 {Pct(r => r.Compile, 50)} ms, p99 {Pct(r => r.Compile, 99)} ms, max {rows.Max(r => r.Compile):F0} ms");
        _output.WriteLine($"allocated {rows.Sum(r => r.Alloc) / (1 << 20):N0} MB; collections gen0/1/2 {GC.CollectionCount(0) - gc.Item1}/{GC.CollectionCount(1) - gc.Item2}/{GC.CollectionCount(2) - gc.Item3}");
        _output.WriteLine("slowest (import + transfer + compile):");
        foreach (var r in rows.OrderByDescending(r => r.Import + r.Transfer + r.Compile).Take(10))
            _output.WriteLine($"  {r.Import,7:F0} {r.Transfer,7:F0} {r.Compile,7:F0} ms  {r.Tracks,2} tracks {r.Bars,5} played bars  {r.Alloc / (1 << 20),5} MB  {r.Name}");
        foreach (var f in failures.Take(20)) _output.WriteLine("FAILED " + f);
    }

    [Fact]
    public void Repeated_opens_of_a_large_song_do_not_grow_memory()
    {
        if (SamplesDir() is not { } dir) return;
        // The heaviest song of the corpus to convert (2,039 played bars), else the largest file.
        var files = Files(dir);
        var largest = files.FirstOrDefault(f => Path.GetFileName(f) == "master_of_puppets_12.gp5") ?? files.OrderByDescending(f => new FileInfo(f).Length).First();
        long Heap() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
        void Open()
        {
            var song = ProjectService.RestoreTransferBytes(ProjectService.TransferBytes(GuitarProImporter.Import(largest, new ImportContext())));
            _ = MidiTimelineBuilder.Build(song, Options());
        }
        Open();
        var h0 = Heap();
        var times = new List<double>();
        for (var i = 0; i < 10; i++) { var t = Stopwatch.GetTimestamp(); Open(); times.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds); }
        var h1 = Heap();
        _output.WriteLine($"{Path.GetFileName(largest)} ({new FileInfo(largest).Length / 1024} KB) opened 10 times: {string.Join(", ", times.Select(t => t.ToString("F0")))} ms; " +
            $"retained heap {h0 / 1024} KB -> {h1 / 1024} KB");
        Assert.True(h1 - h0 < 16 << 20, $"retained heap grew {(h1 - h0) / 1024} KB over 10 opens");
    }
}
