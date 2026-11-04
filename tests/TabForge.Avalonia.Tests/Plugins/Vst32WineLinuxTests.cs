using System;
using System.Collections.Generic;
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

// Owns: the Linux-only stage-2 proof. The real engine (RemotePlugin + WineRuntime + Vst32Bridge) starts the shipped
// tfvst32.exe under Wine, uses the TCP control channel and the explicit file block, and renders the deterministic
// 32-bit test DLL and one workflow-downloaded real 32-bit VST2. State, parameters, kill containment and realtime block
// round-trip percentiles at 256 and 1024 frames are recorded here.
// Does not own: the Windows TCP transport (Vst32TcpBridgeTests) or injected-value Wine seam logic (Vst32WineSeamTests).
[Collection("Vst32Native")]
public sealed class Vst32WineLinuxTests
{
    private const int Rate = 48000;
    private static readonly object MetricsLock = new();
    private readonly ITestOutputHelper _output;

    public Vst32WineLinuxTests(ITestOutputHelper output) => _output = output;

    [Vst32WineTheory]
    [InlineData(256)]
    [InlineData(1024)]
    public void WineDeterministicDllRendersFiniteNonSilentAudio(int frames)
    {
        var (plugin, _) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, frames);
        var blockPath = plugin.BlockFilePath;
        var pid = plugin.HostProcessId;
        try
        {
            Assert.NotNull(blockPath);
            Assert.Equal(ExpectedBlockBytes(frames), new FileInfo(blockPath!).Length);
            plugin.SetOfflineMode(true);
            Assert.True(plugin.SetParameter(0, 0.25), "set the deterministic gain to 0.25 (gain 0.5)");
            var (rmsIn, rmsOut, finite) = RenderEffect(plugin, frames, blocks: 8, skip: 1);
            Assert.True(finite, "every output sample must be finite");
            Assert.True(rmsOut > 0.01f, $"the deterministic plug-in must be non-silent (RMS {rmsOut:0.00000})");
            var ratio = rmsOut / Math.Max(1e-9f, rmsIn);
            Assert.InRange(ratio, 0.45f, 0.55f);   // dry bypass would be 1.0, so this proves the DLL processed the block
            Assert.Equal(0L, plugin.MissedBlocks);
            Assert.Equal(0L, plugin.BudgetSkippedBlocks);
            Log($"wine audio deterministic frames={frames} rms_in={rmsIn:0.00000} rms_out={rmsOut:0.00000} " +
                $"ratio={ratio:0.0000} finite={finite} missed={plugin.MissedBlocks} block_bytes={ExpectedBlockBytes(frames)}");
        }
        finally
        {
            plugin.Dispose();
        }
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 10000), "the Wine helper must exit after CmdQuit");
        Assert.False(File.Exists(blockPath), "the engine must delete the explicit block file on dispose");
    }

    [Vst32WineFact]
    public void WineDeterministicParametersAndStateRoundTrip()
    {
        using var plugin = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, 1024).Plugin;
        plugin.SetOfflineMode(true);
        var parameter = Assert.Single(plugin.GetParameters());
        Assert.Contains("gain", parameter.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(plugin.SetParameter(0, 0.25));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.20, 0.30);
        Assert.True(plugin.SetParameter(0, 0.75));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        Assert.True(plugin.SetParameter(0, 0.25));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.20, 0.30);
        plugin.SetState(state!);
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);   // the state captured at 0.75
        var (rmsIn, rmsOut, finite) = RenderEffect(plugin, 1024, blocks: 6, skip: 1);
        var ratio = rmsOut / Math.Max(1e-9f, rmsIn);
        Assert.True(finite, "state-restored audio must be finite");
        Assert.InRange(ratio, 1.45f, 1.55f);   // gain 1.5 after the restore, not the 0.5 set before it
        Log($"wine state deterministic state_bytes={state!.Length} ratio_after_restore={ratio:0.0000}");
    }

    [Vst32WineRealFact]
    public void WineRealDexedRendersMidiAudioAndRoundTripsStateAndParameters()
    {
        var path = Vst32TestAssets.WineRealPluginPath!;
        var expectedSha = Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_SHA256");
        if (!string.IsNullOrWhiteSpace(expectedSha))
            Assert.Equal(expectedSha, Vst32TestAssets.Sha256(path), ignoreCase: true);
        using var plugin = Vst32TestAssets.StartWine(path, instrument: true, 1024).Plugin;
        plugin.SetOfflineMode(true);
        Assert.True(plugin.IsInstrument, "Dexed must identify as an instrument");
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        _output.WriteLine("Dexed parameters: " + string.Join(", ", parameters.Take(8).Select(p => $"{p.Id}:{p.Name}={p.Value:0.000}")));

        // Pick a parameter that actually moves, so a stepped parameter cannot make the check meaningless.
        uint id = 0;
        double before = 0, after = 0;
        var changed = false;
        foreach (var candidate in parameters.Take(24))
        {
            var target = candidate.Value > 0.5 ? 0.1 : 0.9;
            if (!plugin.SetParameter(candidate.Id, target)) continue;
            var read = plugin.GetParameters().First(p => p.Id == candidate.Id).Value;
            if (Math.Abs(read - candidate.Value) <= 0.02) continue;
            id = candidate.Id; before = candidate.Value; after = read; changed = true;
            break;
        }
        Assert.True(changed, "at least one real parameter must accept a changed value");
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);

        Assert.True(plugin.SetParameter(id, before));
        var (rms, finite) = RenderInstrument(plugin, blocks: 24, notesAt: [0, 4]);
        Assert.True(finite, "Dexed output must be finite");
        Assert.True(rms > 1e-4f, $"Dexed must make sound from MIDI (RMS {rms:0.00000})");
        plugin.SetState(state!);
        var restored = plugin.GetParameters().First(p => p.Id == id).Value;
        Assert.InRange(restored, after - 0.02, after + 0.02);
        var (restoredRms, restoredFinite) = RenderInstrument(plugin, blocks: 16, notesAt: [0, 4]);
        Assert.True(restoredFinite, "Dexed output after state restore must be finite");
        Assert.True(restoredRms > 1e-4f, $"Dexed must still make sound after the state restore (RMS {restoredRms:0.00000})");
        Log($"wine real dexed sha={Vst32TestAssets.Sha256(path)[..16]}.. params={parameters.Count} " +
            $"state_bytes={state!.Length} rms={rms:0.00000} restored_rms={restoredRms:0.00000} " +
            $"param_id={id} before={before:0.0000} after={after:0.0000} restored={restored:0.0000}");
    }

    [Vst32WineRealFact]
    public void WineProbeLoadsTheRealDllThroughTheZWDrive()
    {
        var path = Vst32TestAssets.WineRealPluginPath!;
        var helperFolder = Path.GetDirectoryName(Vst32TestAssets.HelperPath!)!;
        var runtime = WineRuntime.Locate(helperFolder: helperFolder)
            ?? throw new InvalidOperationException("WineRuntime.Locate returned null on Linux with TABFORGE_WINE set");
        runtime.EnsureReady();
        var sha = Vst32TestAssets.Sha256(path);
        var start = Vst32Bridge.ProbeStartInfo(runtime.HelperPath, path, sha, runtime);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("the Wine probe did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60000), "the Wine probe must finish within 60 s");
        var line = output.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).FirstOrDefault() ?? "";
        var problems = error.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"probe exit {process.ExitCode}: {problems}");
        Assert.StartsWith("Instrument|", line);
        Log($"wine probe real dll exit={process.ExitCode} line={line}");
    }

    [Vst32WineFact]
    public void WineHelperDeathIsContainedAndBypassesDry()
    {
        var (plugin, threads) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, 256);
        var blockPath = plugin.BlockFilePath;
        var crashed = 0;
        var crashedSignal = new ManualResetEventSlim();
        plugin.Crashed += _ => { Interlocked.Increment(ref crashed); crashedSignal.Set(); };
        var input = Enumerable.Repeat(0.25f, 256).ToArray();
        var output = new[] { new float[256], new float[256] };
        try
        {
            plugin.Process(new[] { input, input }, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, 256));
            var pid = plugin.HostProcessId;
            using (var process = Process.GetProcessById(pid)) process.Kill(true);
            var killedAt = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(killedAt).TotalMilliseconds < 5000 && !crashedSignal.IsSet)
            {
                threads.RunPending();
                Thread.Sleep(5);
            }
            threads.RunPending();
            Assert.True(crashedSignal.IsSet, "the Wine helper's death must be reported");
            Assert.Equal(1, crashed);
            for (var i = 0; i < 20; i++)
            {
                plugin.Process(new[] { input, input }, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, 256));
                Assert.Equal(input, output[0]);   // effect dry after the loss, never an exception
            }
            var disposeAt = Stopwatch.GetTimestamp();
            plugin.Dispose();
            Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 5000, "Dispose must stay bounded");
            Log("wine crash containment crash=1 dry_blocks=20 dispose_ms=" +
                $"{(int)Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds}");
        }
        finally
        {
            plugin.Dispose();
        }
        Assert.False(File.Exists(blockPath), "the block file must be cleaned up after a Wine helper kill");
    }

    [Vst32WineTheory]
    [InlineData(256)]
    [InlineData(1024)]
    public void WineLogsBlockRoundTripLatency(int frames)
    {
        var (plugin, _) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, frames);
        var input = Enumerable.Repeat(0.1f, frames).ToArray();
        var output = new[] { new float[frames], new float[frames] };
        try
        {
            for (var i = 0; i < 20; i++)
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
            var all = new List<double>(200);
            var processed = new List<double>(200);
            var missed = 0;
            for (var i = 0; i < 200; i++)
            {
                var beforeMiss = plugin.MissedBlocks;
                var beforeBudget = plugin.BudgetSkippedBlocks;
                var started = Stopwatch.GetTimestamp();
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
                var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                all.Add(ms);
                if (plugin.MissedBlocks != beforeMiss || plugin.BudgetSkippedBlocks != beforeBudget) missed++;
                else processed.Add(ms);
            }
            var p50 = Percentile(all, 50); var p95 = Percentile(all, 95); var p99 = Percentile(all, 99);
            var q50 = processed.Count == 0 ? double.NaN : Percentile(processed, 50);
            var q95 = processed.Count == 0 ? double.NaN : Percentile(processed, 95);
            var q99 = processed.Count == 0 ? double.NaN : Percentile(processed, 99);
            var blockMs = frames * 1000.0 / Rate;
            Log($"wine roundtrip frames={frames} block_ms={blockMs:0.000} calls=200 processed={processed.Count} missed={missed} " +
                $"p50={p50:0.000} p95={p95:0.000} p99={p99:0.000} ms; processed_p50={q50:0.000} processed_p95={q95:0.000} " +
                $"processed_p99={q99:0.000} ms; missed_total={plugin.MissedBlocks} budget_skipped={plugin.BudgetSkippedBlocks} " +
                $"link_failed={plugin.LinkFailed}");
            Assert.Equal(200, all.Count);
            Assert.True(processed.Count > 0, "at least one realtime block must be processed end to end");
            Assert.False(plugin.LinkFailed, "the Wine link must not fail during the 200-block measurement");
            Assert.True(p99 < 2000, $"a block round trip p99 of {p99:0.0} ms is not a working bridge");
        }
        finally
        {
            plugin.Dispose();
        }
    }

    private static (float RmsIn, float RmsOut, bool Finite) RenderEffect(RemotePlugin plugin, int frames, int blocks, int skip)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        double inSum = 0, outSum = 0;
        var count = 0;
        var finite = true;
        var phase = 0.0;
        for (var block = 0; block < blocks; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / Rate;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block < skip) continue;
            for (var i = 0; i < frames; i++)
            {
                inSum += input[0][i] * (double)input[0][i];
                outSum += output[0][i] * (double)output[0][i];
                count++;
            }
        }
        return count == 0 ? (0, 0, finite) : ((float)Math.Sqrt(inSum / count), (float)Math.Sqrt(outSum / count), finite);
    }

    private static (float Rms, bool Finite) RenderInstrument(RemotePlugin plugin, int blocks, int[] notesAt)
    {
        const int frames = 1024;
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        double sum = 0;
        var count = 0;
        var finite = true;
        for (var block = 0; block < blocks; block++)
        {
            var midi = notesAt.Contains(block)
                ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
                : Array.Empty<BlockMidi>();
            plugin.Process(input, output, frames, midi, Vst32TestAssets.Transport(block, frames));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block < 2) continue;
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
            foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
        }
        return count == 0 ? (0, finite) : ((float)Math.Sqrt(sum / count), finite);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static long ExpectedBlockBytes(int frames) => 64 + 512 * 8 + 2L * 4 * frames * sizeof(float);

    private void Log(string line)
    {
        _output.WriteLine(line);
        var path = Environment.GetEnvironmentVariable("TABFORGE_WINE_METRICS");
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (MetricsLock) File.AppendAllText(path, line + Environment.NewLine);
    }
}
