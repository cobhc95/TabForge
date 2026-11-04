using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the stage-6 real-fixture matrix: kill/hang, 1-vs-2-slot levels and the 10-second soak with p50/p95 round-trip.
// Does not own: the deterministic crash/hang mechanics (Vst32BridgeCrashTests) or the block/link units (Vst32BridgeStage6Tests).
[Collection("Vst32Native")]
public sealed class Vst32RealStage6Tests
{
    private const int Frames = 64;
    private const int Rounds = 5;
    private const int SoakFrames = 64, SoakSeconds = 10;
    private const string StallVariable = "TABFORGE_TFVST32_TEST_STALL_AUDIO_MS";
    private readonly ITestOutputHelper _output;

    public Vst32RealStage6Tests(ITestOutputHelper output) => _output = output;

    [Vst32RealTheory]
    [InlineData("PurestGain32", false)]
    [InlineData("Density32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void KillingTheRealHelperMidProcessIsDryOrSilentEveryRound(string name, bool instrument)
    {
        var path = Fixture(name);
        var noticed = new double[Rounds];
        for (var round = 0; round < Rounds; round++)
        {
            var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument, Frames, bufferSlots: 2);
            var crashed = 0;
            var signal = new ManualResetEventSlim();
            plugin.Crashed += _ => { Interlocked.Increment(ref crashed); signal.Set(); };
            var pid = plugin.HostProcessId;
            var input = Enumerable.Repeat(instrument ? 0f : 0.25f, Frames).ToArray();
            var output = new[] { new float[Frames], new float[Frames] };
            ProcessBlock(plugin, input, output, instrument, 0);
            var errors = new ConcurrentQueue<Exception>();
            var stop = new ManualResetEventSlim();
            var worker = new Thread(() =>
            {
                var block = 1;
                while (!stop.IsSet)
                {
                    try { ProcessBlock(plugin, input, output, instrument, block++); }
                    catch (Exception ex) { errors.Enqueue(ex); }
                }
            }) { IsBackground = true, Name = "tf-vst32-real-kill" };
            worker.Start();
            Thread.Sleep(60);
            var killedAt = Stopwatch.GetTimestamp();
            using (var process = Process.GetProcessById(pid)) process.Kill(true);
            while (Stopwatch.GetElapsedTime(killedAt).TotalMilliseconds < 2000 && !signal.IsSet)
            {
                threads.RunPending();
                Thread.Sleep(2);
            }
            noticed[round] = Stopwatch.GetElapsedTime(killedAt).TotalMilliseconds;
            stop.Set();
            worker.Join(2000);
            threads.RunPending();
            try
            {
                Assert.True(signal.IsSet, $"{name} round {round}: crash not reported");
                Assert.Equal(1, crashed);
                Assert.Empty(errors);
                for (var i = 0; i < 20; i++)
                {
                    ProcessBlock(plugin, input, output, instrument, i);
                    AssertDryOrSilent(name, instrument, input, output);
                }
                var disposeAt = Stopwatch.GetTimestamp();
                plugin.Dispose();
                Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 2000, $"{name}: Dispose took too long");
                Assert.True(Vst32TestAssets.WaitsForExit(pid, 2000), $"{name}: helper left behind");
            }
            finally { plugin.Dispose(); }
        }
        _output.WriteLine($"{name}: crash noticed ms per round = {string.Join(", ", noticed.Select(x => x.ToString("0.0")))}");
        Assert.All(noticed, ms => Assert.InRange(ms, 0, 500));
    }

    [Vst32HookedRealTheory]
    [InlineData("PurestGain32", false)]
    [InlineData("Density32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void AStalledRealHelperBypassesThenFailsEveryRound(string name, bool instrument)
    {
        var path = Fixture(name);
        var noticed = new double[Rounds];
        for (var round = 0; round < Rounds; round++)
        {
            Environment.SetEnvironmentVariable(StallVariable, "3000");
            RemotePlugin plugin;
            EngineThreads threads;
            try
            {
                (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument, Frames,
                    bufferSlots: 2, helperPath: Vst32TestAssets.HookedHelperPath!);
            }
            finally { Environment.SetEnvironmentVariable(StallVariable, null); }
            var crashed = 0;
            var signal = new ManualResetEventSlim();
            plugin.Crashed += _ => { Interlocked.Increment(ref crashed); signal.Set(); };
            var pid = plugin.HostProcessId;
            var input = Enumerable.Repeat(instrument ? 0f : 0.2f, Frames).ToArray();
            var output = new[] { new float[Frames], new float[Frames] };
            try
            {
                var firstAt = Stopwatch.GetTimestamp();
                ProcessBlock(plugin, input, output, instrument, 0);
                var firstMs = Stopwatch.GetElapsedTime(firstAt).TotalMilliseconds;
                AssertDryOrSilent(name, instrument, input, output);
                Assert.True(plugin.MissedBlocks >= 1, $"{name}: miss not counted");
                Assert.InRange(firstMs, 0, 60);

                long ticks = 0;
                double maxCallMs = 0;
                var loop = Stopwatch.GetTimestamp();
                while (Stopwatch.GetElapsedTime(loop).TotalMilliseconds < 3000 && !signal.IsSet)
                {
                    var callAt = Stopwatch.GetTimestamp();
                    ProcessBlock(plugin, input, output, instrument, (int)(ticks + 1));
                    maxCallMs = Math.Max(maxCallMs, Stopwatch.GetElapsedTime(callAt).TotalMilliseconds);
                    threads.RunPending();
                    ticks++;
                    Thread.SpinWait(20);
                }
                noticed[round] = Stopwatch.GetElapsedTime(loop).TotalMilliseconds;
                Assert.True(signal.IsSet, $"{name} round {round}: stalled helper did not fail");
                Assert.Equal(1, crashed);
                Assert.True(plugin.LinkFailed);
                Assert.True(ticks > 100, $"{name}: UI-like loop starved ({ticks} ticks)");
                Assert.InRange(maxCallMs, 0, 60);
                var misses = plugin.MissedBlocks;
                for (var i = 0; i < 5; i++)
                {
                    ProcessBlock(plugin, input, output, instrument, i);
                    AssertDryOrSilent(name, instrument, input, output);
                }
                Assert.Equal(misses, plugin.MissedBlocks);
                var disposeAt = Stopwatch.GetTimestamp();
                plugin.Dispose();
                Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 2000, $"{name}: Dispose took too long");
                Assert.True(Vst32TestAssets.WaitsForExit(pid, 2000), $"{name}: hung helper left behind");
            }
            finally { plugin.Dispose(); }
        }
        _output.WriteLine($"{name}: hang failure ms per round = {string.Join(", ", noticed.Select(x => x.ToString("0")))}");
    }

    [Vst32RealTheory]
    [InlineData("PurestGain32", false)]
    [InlineData("Density32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void TwoSlotAudioMatchesOneSlotAndReportsLatency(string name, bool instrument)
    {
        var path = Fixture(name);
        const int blocks = 400;
        var one = Run(path, instrument, Frames, blocks, measure: false);
        var two = Run(path, instrument, Frames, blocks, measure: true);
        var oneDb = Vst32TestAssets.Db((float)one.TotalRms);
        var twoDb = Vst32TestAssets.Db((float)two.TotalRms);
        _output.WriteLine($"{name}: 1 slot {oneDb:0.00} dBFS, 2 slots {twoDb:0.00} dBFS; latency p50 {two.P50:0.000} ms, p95 {two.P95:0.000} ms, max {two.Max:0.000} ms; failed={two.Failed} misses={two.Misses}");
        Assert.Equal(one.Rms.Length, two.Rms.Length);
        Assert.False(two.Failed, $"{name}: the 2-slot link failed during the run");
        Assert.InRange(Math.Abs(twoDb - oneDb), 0, 2.0);
        Assert.True(one.SoundingBlocks + 2 >= two.SoundingBlocks && two.SoundingBlocks + 2 >= one.SoundingBlocks,
            $"{name}: block count drifted ({one.SoundingBlocks} vs {two.SoundingBlocks})");
        Assert.True(two.TotalRms > 1e-6, $"{name}: 2-slot output is silent");
        Assert.InRange(two.P95, 0, 40);
    }

    [Vst32RealTheory]
    [InlineData("Acceleration32", false)]
    [InlineData("ADClip932", false)]
    [InlineData("Console4Channel32", false)]
    [InlineData("Density32", false)]
    [InlineData("PurestGain32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void TenSecondSoakMatchesOneSlotLevelsAndMeasuresRoundTrip(string name, bool instrument)
    {
        var path = Fixture(name);
        var blocks = SoakSeconds * 48000 / SoakFrames;
        var one = Run(path, instrument, SoakFrames, blocks, measure: false);
        var two = Run(path, instrument, SoakFrames, blocks, measure: true);
        var oneDb = Vst32TestAssets.Db((float)one.TotalRms);
        var twoDb = Vst32TestAssets.Db((float)two.TotalRms);
        _output.WriteLine($"{name}: soak {blocks} blocks; 1 slot {oneDb:0.00} dBFS, 2 slots {twoDb:0.00} dBFS; " +
                          $"p50 {two.P50:0.000} ms, p95 {two.P95:0.000} ms, max {two.Max:0.000} ms; failed={two.Failed} misses={two.Misses}");
        Assert.Equal(blocks, one.Rms.Length);
        Assert.Equal(blocks, two.Rms.Length);
        Assert.False(two.Failed, $"{name}: the 2-slot link failed during the soak");
        Assert.Equal(one.SoundingBlocks, two.SoundingBlocks);
        Assert.InRange(Math.Abs(twoDb - oneDb), 0, 2.0);
        Assert.True(two.TotalRms > 1e-6, $"{name}: soak output is silent");
        Assert.All(two.Rms, rms => Assert.True(double.IsFinite(rms)));
        Assert.InRange(two.P95, 0, 40);
    }

    private static RemotePlugin Start(string path, bool instrument) =>
        Vst32TestAssets.Start(path, instrument, Frames, bufferSlots: 2);

    private static RunResult Run(string path, bool instrument, int frames, int blocks, bool measure)
    {
        var plugin = Start(path, instrument);
        var rms = new double[blocks];
        var latencies = measure ? new double[blocks] : Array.Empty<double>();
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        double total = 0;
        var sounding = 0;
        var phase = 0.0;
        var failed = false;
        long misses = 0, missesAtStart = 0;
        try
        {
            // Warm up until the helper has answered several blocks in a row. The first requests race its audio-thread start and are
            // bypassed (the link counts them); sending the measured clip during that window lost the note (TAL-NoiseMaker flake).
            WarmUp(plugin, input, output, frames);
            missesAtStart = plugin.MissedBlocks;
            for (var block = 0; block < blocks; block++)
            {
                if (!instrument)
                {
                    for (var i = 0; i < frames; i++)
                    {
                        var sample = (float)(0.25 * Math.Sin(phase));
                        phase += 2 * Math.PI * 1000 / 48000;
                        input[0][i] = sample; input[1][i] = sample;
                    }
                }
                var midi = instrument ? MidiFor(block) : ReadOnlySpan<BlockMidi>.Empty;
                var at = measure ? Stopwatch.GetTimestamp() : 0;
                plugin.Process(input, output, frames, midi, Vst32TestAssets.Transport(block, frames));
                if (measure) latencies[block] = Stopwatch.GetElapsedTime(at).TotalMilliseconds;
                var blockSum = 0.0;
                for (var i = 0; i < frames; i++) blockSum += output[0][i] * (double)output[0][i] + output[1][i] * (double)output[1][i];
                rms[block] = Math.Sqrt(blockSum / (2.0 * frames));
                if (block >= 8)
                {
                    total += blockSum;
                    if (rms[block] > 1e-6) sounding++;
                }
            }
        }
        finally
        {
            failed = plugin.LinkFailed;
            misses = plugin.MissedBlocks - missesAtStart;
            plugin.Dispose();
        }
        var ordered = latencies.OrderBy(x => x).ToArray();
        double Percentile(double p) => ordered.Length == 0 ? 0 : ordered[Math.Clamp((int)(p * (ordered.Length - 1)), 0, ordered.Length - 1)];
        return new RunResult(rms, Math.Sqrt(total / Math.Max(1, (blocks - 8) * 2.0 * frames)), sounding,
            Percentile(0.50), Percentile(0.95), ordered.Length == 0 ? 0 : ordered[^1], failed, misses);
    }

    /// <summary>Runs silent blocks until eight in a row are neither timed out nor budget-skipped (no sleeps: the loop is bounded).</summary>
    private static void WarmUp(RemotePlugin plugin, float[][] input, float[][] output, int frames)
    {
        // This runs outside a mixer callback: clear any callback deadline left on a reused test thread, or every block
        // would be budget-skipped and the warm-up could never see eight clean blocks (xUnit thread reuse under load).
        PluginHostLink.EndCallback();
        var misses = plugin.MissedBlocks;
        var skipped = plugin.BudgetSkippedBlocks;
        var clean = 0;
        for (var block = 0; block < 2000 && clean < 8; block++)
        {
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            var nowMisses = plugin.MissedBlocks;
            var nowSkipped = plugin.BudgetSkippedBlocks;
            if (nowMisses == misses && nowSkipped == skipped) clean++;
            else { clean = 0; misses = nowMisses; skipped = nowSkipped; }
        }
        Assert.True(clean >= 8, "the helper never became responsive during warm-up");
    }

    private static BlockMidi[] MidiFor(int block)
    {
        var cycle = block % 400;
        return cycle == 16 ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
            : cycle == 216 ? new[] { new BlockMidi { Frame = 0, Status = 0x80, Data1 = 60, Data2 = 0 } }
            : Array.Empty<BlockMidi>();
    }

    private static void ProcessBlock(RemotePlugin plugin, float[] input, float[][] output, bool instrument, int block) =>
        plugin.Process(new[] { input, input }, output, input.Length,
            instrument ? MidiFor(block) : ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, input.Length));

    private static void AssertDryOrSilent(string name, bool instrument, float[] input, float[][] output)
    {
        if (instrument)
        {
            Assert.Equal(0f, output[0].Max(Math.Abs));
            Assert.Equal(0f, output[1].Max(Math.Abs));
        }
        else Assert.Equal(input, output[0]);
    }

    private static string Fixture(string name)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        return path;
    }

    private sealed record RunResult(double[] Rms, double TotalRms, int SoundingBlocks, double P50, double P95, double Max, bool Failed, long Misses);
}
