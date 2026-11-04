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
using TabForge.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the Linux-only proof of the 64-bit Wine route. The real engine (RemotePlugin + WineRuntime + Vst32Bridge) starts the
// shipped tfvst64.exe under Wine, uses the TCP control channel and the explicit file block, and renders the deterministic
// 64-bit test DLL and one workflow-downloaded real 64-bit VST2. Scan labels, state, parameters, probe, kill containment and
// block round-trip percentiles are recorded here.
// Does not own: the Windows x64 helper behaviour (Vst32Native64Tests) or the 32-bit Wine route (Vst32WineLinuxTests).
[Collection("Vst32Native")]
public sealed class Vst32WineLinux64Tests
{
    private const int Rate = 48000;
    private static readonly object MetricsLock = new();
    private readonly ITestOutputHelper _output;

    public Vst32WineLinux64Tests(ITestOutputHelper output) => _output = output;

    [Vst64WineTheory]
    [InlineData(256)]
    [InlineData(1024)]
    public void Wine64DeterministicDllRendersFiniteNonSilentAudio(int frames)
    {
        var (plugin, _) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath64!, instrument: false, frames);
        var blockPath = plugin.BlockFilePath;
        var pid = plugin.HostProcessId;
        try
        {
            Assert.NotNull(blockPath);
            Assert.Equal(ExpectedBlockBytes(frames), new FileInfo(blockPath!).Length);
            plugin.SetOfflineMode(true);
            Assert.True(plugin.SetParameter(0, 0.25), "set the deterministic gain to 0.25 (gain 0.5)");
            var (rmsIn, rmsOut, finite) = RenderEffect(plugin, frames, blocks: 8, skip: 1);
            Assert.True(finite, "every x64 output sample must be finite");
            Assert.True(rmsOut > 0.01f, $"the x64 deterministic plug-in must be non-silent (RMS {rmsOut:0.00000})");
            var ratio = rmsOut / Math.Max(1e-9f, rmsIn);
            Assert.InRange(ratio, 0.45f, 0.55f);   // dry bypass would be 1.0, so the DLL processed the block
            Assert.Equal(0L, plugin.MissedBlocks);
            Assert.Equal(0L, plugin.BudgetSkippedBlocks);
            Log($"wine64 audio deterministic frames={frames} rms_in={rmsIn:0.00000} rms_out={rmsOut:0.00000} ratio={ratio:0.0000} " +
                $"finite={finite} missed={plugin.MissedBlocks} block_bytes={ExpectedBlockBytes(frames)}");
        }
        finally { plugin.Dispose(); }
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 10000), "the Wine 64-bit helper must exit after CmdQuit");
        Assert.False(File.Exists(blockPath), "the engine must delete the explicit block file on dispose");
    }

    [Vst64WineFact]
    public void Wine64DeterministicParametersAndStateRoundTrip()
    {
        using var plugin = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath64!, instrument: false, 1024).Plugin;
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
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);
        var (rmsIn, rmsOut, finite) = RenderEffect(plugin, 1024, blocks: 6, skip: 1);
        Assert.True(finite, "state-restored x64 audio must be finite");
        Assert.InRange(rmsOut / Math.Max(1e-9f, rmsIn), 1.45f, 1.55f);
        Log($"wine64 state deterministic state_bytes={state!.Length} ratio_after_restore={rmsOut / Math.Max(1e-9f, rmsIn):0.0000}");
    }

    [Vst64WineRealFact]
    public void Wine64RealDexedRendersMidiAudioAndRoundTripsStateAndParameters()
    {
        var path = Vst32TestAssets.WineRealPluginPath64!;
        var expectedSha = Environment.GetEnvironmentVariable("TABFORGE_WINE_REAL_VST64_SHA256");
        if (!string.IsNullOrWhiteSpace(expectedSha))
            Assert.Equal(expectedSha, Vst32TestAssets.Sha256(path), ignoreCase: true);
        using var plugin = Vst32TestAssets.StartWine(path, instrument: true, 1024).Plugin;
        plugin.SetOfflineMode(true);
        Assert.True(plugin.IsInstrument, "the 64-bit Dexed must identify as an instrument");
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);

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
        Assert.True(changed, "at least one real x64 parameter must accept a changed value");
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        Assert.True(plugin.SetParameter(id, before));
        var (rms, finite) = RenderInstrument(plugin, blocks: 24, notesAt: [0, 4]);
        Assert.True(finite, "64-bit Dexed output must be finite");
        Assert.True(rms > 1e-4f, $"64-bit Dexed must make sound from MIDI (RMS {rms:0.00000})");
        plugin.SetState(state!);
        var restored = plugin.GetParameters().First(p => p.Id == id).Value;
        Assert.InRange(restored, after - 0.02, after + 0.02);
        var (restoredRms, restoredFinite) = RenderInstrument(plugin, blocks: 16, notesAt: [0, 4]);
        Assert.True(restoredFinite, "64-bit Dexed output after state restore must be finite");
        Assert.True(restoredRms > 1e-4f, $"64-bit Dexed must still make sound after the state restore (RMS {restoredRms:0.00000})");
        Log($"wine64 real dexed sha={Vst32TestAssets.Sha256(path)[..16]}.. params={parameters.Count} state_bytes={state!.Length} " +
            $"rms={rms:0.00000} restored_rms={restoredRms:0.00000} param_id={id} before={before:0.0000} after={after:0.0000} restored={restored:0.0000}");
    }

    [Vst64WineRealFact]
    public void Wine64ProbeLoadsTheRealDllThroughTheZWDrive()
    {
        var path = Vst32TestAssets.WineRealPluginPath64!;
        var helperFolder = Path.GetDirectoryName(Vst32TestAssets.HelperPath64!)!;
        var runtime = WineRuntime.Locate(helperFolder: helperFolder)
            ?? throw new InvalidOperationException("WineRuntime.Locate returned null on Linux with TABFORGE_WINE set");
        runtime.EnsureReady();
        var sha = Vst32TestAssets.Sha256(path);
        var start = Vst32Bridge.ProbeStartInfo(runtime.HelperPath64, path, sha, runtime);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("the Wine 64-bit probe did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60000), "the Wine 64-bit probe must finish within 60 s");
        var line = output.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).FirstOrDefault() ?? "";
        var problems = error.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"probe exit {process.ExitCode}: {problems}");
        Assert.StartsWith("Instrument|", line);
        Log($"wine64 probe real dll exit={process.ExitCode} line={line}");
    }

    [Vst64WineFact]
    public void Wine64HelperDeathIsContainedAndBypassesDry()
    {
        var (plugin, threads) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath64!, instrument: false, 256);
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
            Assert.True(crashedSignal.IsSet, "the Wine 64-bit helper's death must be reported");
            Assert.Equal(1, crashed);
            for (var i = 0; i < 20; i++)
            {
                plugin.Process(new[] { input, input }, output, 256, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, 256));
                Assert.Equal(input, output[0]);
            }
            var disposeAt = Stopwatch.GetTimestamp();
            plugin.Dispose();
            Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 5000, "Dispose must stay bounded");
            Log($"wine64 crash containment crash=1 dry_blocks=20 dispose_ms={(int)Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds}");
        }
        finally { plugin.Dispose(); }
        Assert.False(File.Exists(blockPath), "the block file must be cleaned up after a Wine 64-bit helper kill");
    }

    [Vst64WineTheory]
    [InlineData(256)]
    [InlineData(1024)]
    public void Wine64LogsBlockRoundTripLatency(int frames)
    {
        var (plugin, _) = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath64!, instrument: false, frames);
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
            Log($"wine64 roundtrip frames={frames} block_ms={blockMs:0.000} calls=200 processed={processed.Count} missed={missed} " +
                $"p50={p50:0.000} p95={p95:0.000} p99={p99:0.000} ms; processed_p50={q50:0.000} processed_p95={q95:0.000} " +
                $"processed_p99={q99:0.000} ms; missed_total={plugin.MissedBlocks} budget_skipped={plugin.BudgetSkippedBlocks} " +
                $"link_failed={plugin.LinkFailed}");
            Assert.Equal(200, all.Count);
            Assert.True(processed.Count > 0, "at least one realtime x64 block must be processed end to end");
            Assert.False(plugin.LinkFailed, "the Wine 64-bit link must not fail during the 200-block measurement");
            Assert.True(p99 < 2000, $"a block round trip p99 of {p99:0.0} ms is not a working bridge");
        }
        finally { plugin.Dispose(); }
    }

    [Vst64WineFact]
    public void TheLinuxScannerAndHandPickLabelA64BitDllBridged()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = Path.Combine(folder.Path, "Bridge64.dll");
        File.Copy(Vst32TestAssets.TestPluginPath64!, file);
        var trust = new PluginTrust();

        var found = VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, trust, new Vst32TestAssets.NoFiles(), forceRefresh: true);
        var row = Assert.Single(found);
        Assert.Equal(PluginFormats.Bridged64, row.Format);
        Assert.True(row.IsBridged64);
        var handPicked = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(file, out var problem);
        Assert.Null(problem);
        Assert.Equal(PluginFormats.Bridged64, handPicked!.Format);
        Log($"wine64 scan label={row.Format} hand_pick={handPicked.Format}");
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
