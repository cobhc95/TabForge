using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 15 load and soak data. These tests are opt-in because they start dozens of real bridged processes and the
// soak runs for an hour on CI. They report rather than gate on missed-block counts; link failure and crashes still fail.
// Does not own: the chain/loop/metrics kit (Stage15StressKit) or the tests of each fault case.
[Collection("Vst32Native")]
public sealed class Stage15StressTests
{
    private readonly ITestOutputHelper _output;

    public Stage15StressTests(ITestOutputHelper output) => _output = output;

    [Stage15LoadFact]
    public void AllCoreChainsOfEightMixedPluginsReportMissedBlocksPerBufferSize()
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        var chainCount = EnvInt("TABFORGE_LOAD_CHAINS", cores);
        var warmSeconds = EnvInt("TABFORGE_LOAD_WARM_SECONDS", 1);
        var measureSeconds = EnvInt("TABFORGE_LOAD_SECONDS", 8);
        var sizes = Env("TABFORGE_LOAD_SIZES") is { Length: > 0 } text
            ? text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value.Trim())).ToArray()
            : [64, 128, 256, 512, 1024];
        var tag = Env("TABFORGE_LOAD_UNSHARED") == "1" ? "-unshared" : "";
        _output.WriteLine($"load: cores={cores} chains={chainCount} plugins_per_chain={Stage15StressKit.PluginsPerChain} " +
                          $"warm={warmSeconds}s measure={measureSeconds}s sizes={string.Join('/', sizes)} budget={(tag.Length == 0 ? "shared" : "per-link")}");

        // The engine holds the 1 ms timer for its lifetime (EngineHost). Without it Thread.Sleep(1) rounds up to the
        // Windows 15.6 ms tick and the harness would report misses the shipping engine cannot have.
        NativeTimer.Acquire();
        try
        {
            using var writer = MetricsFile(Env("TABFORGE_LOAD_OUT"), "stage15-load.csv",
                "mode,chains,frames,blocks,missed,budget_skipped,late_callbacks,pinned_chains,link_failed");
            // A fresh set per buffer size: a link that fails at one size must not poison the next size's data.
            foreach (var frames in sizes)
            {
                using (var single = Stage15StressKit.StartSingle(0))
                    RunStage([single], "single" + tag, 1, frames, warmSeconds, measureSeconds, writer, pin: false);
                using (var baseline = Stage15StressKit.StartChain(0))
                    RunStage([baseline], "baseline" + tag, 1, frames, warmSeconds, measureSeconds, writer, pin: false);
                var chains = new List<Stage15StressKit.Chain>();
                for (var i = 0; i < chainCount; i++) chains.Add(Stage15StressKit.StartChain(i));
                _output.WriteLine($"load: {chainCount} chains x {Stage15StressKit.PluginsPerChain} plug-ins at {frames} ({coreDescription()})");
                try { RunStage(chains, "all-cores" + tag, chainCount, frames, warmSeconds, measureSeconds, writer, pin: true); }
                finally { foreach (var chain in chains) chain.Dispose(); }
            }
        }
        finally { NativeTimer.Release(); }
    }

    [Stage15SoakFact]
    public void SixtyMinuteSoakSamplesMemoryHandlesAndMissedBlocksEveryMinute()
    {
        var minutes = EnvInt("TABFORGE_SOAK_MINUTES", 60);
        var sampleSeconds = EnvInt("TABFORGE_SOAK_SAMPLE_SECONDS", 60);
        var frames = EnvInt("TABFORGE_SOAK_FRAMES", 256);
        var totalSeconds = minutes * 60;
        _output.WriteLine($"soak: frames={frames} minutes={minutes} sample_every={sampleSeconds}s plugins={Stage15StressKit.PluginsPerChain}");
        using var timer = new Stage15StressKit.TimerHold();
        using var chain = Stage15StressKit.StartChain(0);
        var stop = new ManualResetEventSlim();
        var error = new Exception?[1];
        var worker = new Thread(() =>
        {
            try
            {
                chain.Pinned = Stage15StressKit.PinCurrentThread(0);
                Stage15StressKit.RunPaced(chain, frames, totalSeconds);
            }
            catch (Exception ex) { error[0] = ex; }
            finally { stop.Set(); }
        }) { IsBackground = true, Name = "stage15-soak-audio" };
        using var writer = MetricsFile(Env("TABFORGE_SOAK_OUT"), "stage15-soak.csv", Stage15Metrics.CsvHeader);
        worker.Start();

        var first = Stage15StressKit.TakeSample(chain);
        var last = first;
        var started = Stopwatch.StartNew();
        var sample = 0;
        try
        {
            while (started.Elapsed.TotalSeconds < totalSeconds && !stop.IsSet)
            {
                var waitMs = Math.Min(sampleSeconds * 1000, (int)Math.Max(0, (totalSeconds - started.Elapsed.TotalSeconds) * 1000));
                if (waitMs > 0) Thread.Sleep(waitMs);
                sample++;
                var current = Stage15StressKit.TakeSample(chain);
                if (sample == 1) first = current;
                last = current;
                writer.WriteLine(Stage15Metrics.Csv(current, sample));
                _output.WriteLine($"soak minute={sample} ws={current.EngineWorkingSet / 1048576.0:0.0}MiB " +
                                  $"private={current.EnginePrivate / 1048576.0:0.0}MiB handles={current.EngineHandles} " +
                                  $"helpers_ws={current.HelperWorkingSet / 1048576.0:0.0}MiB helpers_handles={current.HelperHandles} " +
                                  $"missed={current.MissedBlocks} budget_skipped={current.BudgetSkipped}");
                if (current.LinkFailed) break;
            }
        }
        finally
        {
            stop.Set();
            worker.Join(TimeSpan.FromSeconds(15));
        }
        Assert.Null(error[0]);
        Assert.False(chain.LinkFailed, "no plug-in link may fail during the soak");
        var engineGrowth = last.EnginePrivate - first.EnginePrivate;
        var helperGrowth = last.HelperPrivate - first.HelperPrivate;
        var handleGrowth = last.EngineHandles - first.EngineHandles;
        _output.WriteLine($"soak summary samples={sample} blocks={chain.Blocks} missed={chain.MissedNow - chain.MissedAtStart} " +
                          $"engine_private_growth={engineGrowth / 1048576.0:0.0}MiB helper_private_growth={helperGrowth / 1048576.0:0.0}MiB " +
                          $"engine_handle_growth={handleGrowth} pinned={chain.Pinned}");
        Assert.InRange(engineGrowth, -64L * 1048576, 64L * 1048576);
        Assert.InRange(helperGrowth, -128L * 1048576, 128L * 1048576);
        Assert.InRange(handleGrowth, -32L, 64L);
    }

    private void RunStage(IReadOnlyList<Stage15StressKit.Chain> chains, string mode, int chainCount, int frames,
        int warmSeconds, int measureSeconds, StreamWriter writer, bool pin)
    {
        // Warm-up is a separate window so the measured counters start after the first helper spin-up.
        RunWindow(chains, frames, warmSeconds, pin);
        var blocksBefore = chains.Sum(chain => chain.Blocks);
        var missedBefore = chains.Sum(chain => chain.MissedNow);
        var skippedBefore = chains.Sum(chain => chain.BudgetSkippedNow);
        var lateBefore = chains.Sum(chain => chain.LateCallbacks);
        RunWindow(chains, frames, measureSeconds, pin);
        var blocks = chains.Sum(chain => chain.Blocks) - blocksBefore;
        var missed = chains.Sum(chain => chain.MissedNow) - missedBefore;
        var skipped = chains.Sum(chain => chain.BudgetSkippedNow) - skippedBefore;
        var late = chains.Sum(chain => chain.LateCallbacks) - lateBefore;
        var failed = chains.Any(chain => chain.LinkFailed);
        var pinned = chains.Count(chain => chain.Pinned);
        var line = $"{mode},{chainCount},{frames},{blocks},{missed},{skipped},{late},{pinned},{failed}";
        writer.WriteLine(line);
        writer.Flush();
        _output.WriteLine($"load {line}");
        // A link failure is data here (the buffer was below the load's floor), not a harness error: the next stage
        // starts fresh chains. Load failures/crashes still throw from StartChain or the process loop.
        // Regression for the Stage 15 polling-helper fix: before it, the audio thread's MMCSS registration delayed
        // its 1 ms poll wake by 10-15 ms, and an eight-plug-in chain at 1024 missed about half its blocks. It must
        // stay well under that old figure; report-only below 1024 and for every other mode. A two-logical-processor
        // runner measured 9-14 % here (the eight helpers share two cores), so 25 % still catches the old ~50 %.
        if (OperatingSystem.IsWindows() && frames == 1024 && mode == "baseline")
            Assert.True(missed <= Math.Max(5, blocks * 0.25),
                $"eight bridged plug-ins at 1024 frames missed {missed}/{blocks} blocks (the polling helper fix regressed)");
    }

    private static void RunWindow(IReadOnlyList<Stage15StressKit.Chain> chains, int frames, int seconds, bool pin)
    {
        if (seconds <= 0) return;
        var threads = chains.Select(chain => new Thread(() =>
        {
            chain.Pinned = pin && Stage15StressKit.PinCurrentThread(chain.Index);
            Stage15StressKit.RunPaced(chain, frames, seconds);
        }) { IsBackground = true, Name = $"stage15-load-{chain.Index}" }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join(TimeSpan.FromSeconds(seconds + 30));
    }

    private StreamWriter MetricsFile(string? path, string fallback, string header)
    {
        path = string.IsNullOrWhiteSpace(path) ? Path.Combine(AppContext.BaseDirectory, fallback) : path;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var writer = new StreamWriter(path, append: false) { AutoFlush = false };
        writer.WriteLine(header);
        writer.Flush();
        _output.WriteLine("metrics: " + path);
        return writer;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    private static string coreDescription() => $"{Environment.ProcessorCount} logical processors";
}

/// <summary>Opt-in: starts all-core chains of real bridged plug-ins (TABFORGE_STRESS=1).</summary>
internal sealed class Stage15LoadFactAttribute : FactAttribute
{
    public Stage15LoadFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_STRESS") != "1")
            Skip = "TABFORGE_STRESS=1 is required (the load test starts all cores of bridged plug-ins)";
        else if (!MeetsPlatform()) Skip = "the bridged test plug-ins and both helpers are required";
    }

    private static bool MeetsPlatform() =>
        OperatingSystem.IsWindows()
            ? Vst32TestAssets.HelperPath is not null && Vst32TestAssets.HelperPath64 is not null &&
              Vst32TestAssets.TestPluginPath is not null && Vst32TestAssets.TestPluginPath64 is not null
            : OperatingSystem.IsLinux() && Vst32TestAssets.WineLoader is not null &&
              Vst32TestAssets.HelperPath is not null && Vst32TestAssets.HelperPath64 is not null &&
              Vst32TestAssets.TestPluginPath is not null && Vst32TestAssets.TestPluginPath64 is not null;
}

/// <summary>Opt-in: the long soak (TABFORGE_SOAK=1); it never runs as part of a normal local test pass.</summary>
internal sealed class Stage15SoakFactAttribute : FactAttribute
{
    public Stage15SoakFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_SOAK") != "1")
            Skip = "TABFORGE_SOAK=1 is required (60 minutes by default; CI only)";
        else if (!Stage15LoadFactAttributePlatform.Meets) Skip = "the bridged test plug-ins and both helpers are required";
    }
}

internal static class Stage15LoadFactAttributePlatform
{
    public static bool Meets =>
        OperatingSystem.IsWindows()
            ? Vst32TestAssets.HelperPath is not null && Vst32TestAssets.HelperPath64 is not null &&
              Vst32TestAssets.TestPluginPath is not null && Vst32TestAssets.TestPluginPath64 is not null
            : OperatingSystem.IsLinux() && Vst32TestAssets.WineLoader is not null &&
              Vst32TestAssets.HelperPath is not null && Vst32TestAssets.HelperPath64 is not null &&
              Vst32TestAssets.TestPluginPath is not null && Vst32TestAssets.TestPluginPath64 is not null;
}
