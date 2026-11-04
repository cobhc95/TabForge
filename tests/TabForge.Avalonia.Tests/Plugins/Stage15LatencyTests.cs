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

// Owns: Stage 15 Windows TCP/file-block round-trip latency at the buffer sizes used by the load test. The Wine variant is
// in Vst32WineLinuxTests (WineLogsBlockRoundTripLatency); the load test uses this data to explain its miss counts.
[Collection("Vst32Native")]
public sealed class Stage15LatencyTests
{
    private readonly ITestOutputHelper _output;

    public Stage15LatencyTests(ITestOutputHelper output) => _output = output;

    [Stage15WindowsTheory]
    [InlineData(64)]
    [InlineData(128)]
    public void SmallFrameFileBlockSettlesAfterTheHelperCatchesUp(int frames)
    {
        using var timer = new Stage15StressKit.TimerHold();
        var (plugin, _) = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 1024);
        var input = Enumerable.Repeat(0.1f, frames).ToArray();
        var output = new[] { new float[frames], new float[frames] };
        try
        {
            DumpHeader("after start", plugin.BlockFilePath!);
            plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, frames));
            Thread.Sleep(100);
            DumpHeader("after first block + 100 ms", plugin.BlockFilePath!);
            var before = plugin.MissedBlocks;
            plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(1, frames));
            var secondMissed = plugin.MissedBlocks - before;
            Thread.Sleep(100);
            DumpHeader("after second block + 100 ms", plugin.BlockFilePath!);
            _output.WriteLine($"settle frames={frames} first_missed={before} second_missed={secondMissed} " +
                              $"budget_skipped={plugin.BudgetSkippedBlocks} link_failed={plugin.LinkFailed}");
            Assert.True(secondMissed <= 1, "the same block must not miss for ever once the helper had 100 ms to catch up");
        }
        finally { plugin.Dispose(); }
    }

    private void DumpHeader(string when, string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[64];
        stream.ReadExactly(bytes);
        _output.WriteLine($"{when}: frames={BitConverter.ToInt32(bytes, 0)} reqGen={BitConverter.ToInt32(bytes, 32)} " +
                          $"doneGen={BitConverter.ToInt32(bytes, 36)} reqFlag={BitConverter.ToInt32(bytes, 56)} " +
                          $"doneFlag={BitConverter.ToInt32(bytes, 60)}");
    }

    [Stage15WindowsTheory]
    [InlineData(256)]
    [InlineData(1024)]
    public void PacedSinglePluginRoundTripLatency(int frames)
    {
        using var timer = new Stage15StressKit.TimerHold();
        var (plugin, threads) = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 1024);
        threads.MarkAudioThread();
        var input = Enumerable.Repeat(0.1f, frames).ToArray();
        var output = new[] { new float[frames], new float[frames] };
        try
        {
            for (var i = 0; i < 10; i++)
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
            var durations = new List<double>(300);
            var misses = new List<double>();
            var next = Stopwatch.GetTimestamp();
            var blockTicks = frames * (double)Stopwatch.Frequency / 48000;
            for (var i = 0; i < 300; i++)
            {
                var before = plugin.MissedBlocks;
                var started = Stopwatch.GetTimestamp();
                PluginHostLink.BeginCallback(started, frames, 48000);
                try
                {
                    plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
                }
                finally { PluginHostLink.EndCallback(); }
                durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (plugin.MissedBlocks != before) misses.Add(durations[^1]);
                next += (long)blockTicks;
                var now = Stopwatch.GetTimestamp();
                if (now < next)
                {
                    var remainingMs = (next - now) * 1000.0 / Stopwatch.Frequency;
                    if (remainingMs > 1.5) Thread.Sleep((int)(remainingMs - 1));
                    while (Stopwatch.GetTimestamp() < next) Thread.SpinWait(20);
                }
            }
            var sorted = durations.OrderBy(value => value).ToArray();
            _output.WriteLine($"paced single frames={frames} p50={sorted[150]:0.000} p99={sorted[296]:0.000} " +
                              $"max={sorted[^1]:0.000} missed={misses.Count}/300 miss_wait_p50={(misses.Count == 0 ? 0 : misses.OrderBy(v => v).ElementAt(misses.Count / 2)):0.000} " +
                              $"total_missed={plugin.MissedBlocks} budget_skipped={plugin.BudgetSkippedBlocks}");
        }
        finally { plugin.Dispose(); }
    }

    [Stage15WindowsTheory]
    [InlineData(64)]
    [InlineData(128)]
    public void NamedPipeRoundTripLatencyAtSmallBuffers(int frames)
    {
        using var timer = new Stage15StressKit.TimerHold();
        var (plugin, _) = Vst32TestAssets.StartWithThreads(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 1024);
        var input = Enumerable.Repeat(0.1f, frames).ToArray();
        var output = new[] { new float[frames], new float[frames] };
        try
        {
            for (var i = 0; i < 20; i++)
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
            var durations = new List<double>(100);
            var missed = 0;
            for (var i = 0; i < 100; i++)
            {
                var before = plugin.MissedBlocks;
                var started = Stopwatch.GetTimestamp();
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
                durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (plugin.MissedBlocks != before) missed++;
            }
            var sorted = durations.OrderBy(value => value).ToArray();
            _output.WriteLine($"pipe latency frames={frames} p50={sorted[50]:0.000} p99={sorted[99]:0.000} " +
                              $"missed={missed}/100 total_missed={plugin.MissedBlocks} budget_skipped={plugin.BudgetSkippedBlocks} " +
                              $"link_failed={plugin.LinkFailed}");
        }
        finally { plugin.Dispose(); }
    }

    [Stage15WindowsTheory]
    [InlineData(64, 64)]
    [InlineData(128, 128)]
    [InlineData(256, 256)]
    [InlineData(512, 512)]
    [InlineData(1024, 1024)]
    [InlineData(64, 1024)]
    [InlineData(128, 1024)]
    [InlineData(256, 1024)]
    public void TcpFileBlockRoundTripLatencyAtEachBufferSize(int frames, int maxBlock)
    {
        using var timer = new Stage15StressKit.TimerHold();
        var (plugin, _) = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: maxBlock);
        var input = Enumerable.Repeat(0.1f, frames).ToArray();
        var output = new[] { new float[frames], new float[frames] };
        try
        {
            for (var i = 0; i < 20; i++)
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
            var durations = new List<double>(300);
            var missed = 0;
            for (var i = 0; i < 300; i++)
            {
                var before = plugin.MissedBlocks;
                var started = Stopwatch.GetTimestamp();
                plugin.Process(new[] { input, input }, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, frames));
                durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (plugin.MissedBlocks != before)
                {
                    missed++;
                    if (missed == 1 && plugin.BlockFilePath is { } path && frames <= 128)
                    {
                        using var view = PluginHostBlock.OpenFile(path, maxBlock, 2);
                        _output.WriteLine($"header after first miss: frames={view.Frames} requestGen={view.RequestGeneration} " +
                                          $"doneGen={view.DoneGeneration} offline={view.Offline}");
                    }
                }
            }
            var sorted = durations.OrderBy(value => value).ToArray();
            var p50 = sorted[(int)(0.50 * (sorted.Length - 1))];
            var p95 = sorted[(int)(0.95 * (sorted.Length - 1))];
            var p99 = sorted[(int)(0.99 * (sorted.Length - 1))];
            _output.WriteLine($"tcp latency frames={frames} max_block={maxBlock} block_ms={frames * 1000.0 / 48000:0.000} p50={p50:0.000} " +
                              $"p95={p95:0.000} p99={p99:0.000} max={sorted[^1]:0.000} missed={missed}/300 total_missed={plugin.MissedBlocks} " +
                              $"budget_skipped={plugin.BudgetSkippedBlocks} link_failed={plugin.LinkFailed}");
            Assert.True(p99 < 2000, "the transport must not hang");
        }
        finally { plugin.Dispose(); }
    }
}

internal sealed class Stage15WindowsTheoryAttribute : TheoryAttribute
{
    public Stage15WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The 32-bit VST2 bridge is Windows-only";
        else if (Vst32TestAssets.HelperPath is null) Skip = "tfvst32.exe is not built";
        else if (Vst32TestAssets.TestPluginPath is null) Skip = "passthrough32.dll is not built";
    }
}
