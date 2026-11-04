using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Stage 15 load/soak machinery: chains of eight mixed 32/64-bit bridged test plug-ins, realtime-paced block
// loops, CPU pinning, missed-block counters and per-process samples. Test-only; never referenced by the app.
internal static class Stage15StressKit
{
    public const int Rate = 48000, MaxBlock = 1024, PluginsPerChain = 8;

    /// <summary>Holds the process's 1 ms timer, as the shipping engine does for its lifetime.</summary>
    public sealed class TimerHold : IDisposable
    {
        public TimerHold() => NativeTimer.Acquire();
        public void Dispose() => NativeTimer.Release();
    }

    public sealed class Chain : IDisposable
    {
        public required int Index { get; init; }
        public required RemotePlugin[] Plugins { get; init; }
        public required EngineThreads Threads { get; init; }
        public int MissedAtStart { get; set; }
        public int BudgetSkippedAtStart { get; set; }
        public long Blocks { get; set; }
        public long LateCallbacks { get; set; }
        public bool Pinned { get; set; }

        public long MissedNow => Plugins.Sum(plugin => plugin.MissedBlocks);
        public long BudgetSkippedNow => Plugins.Sum(plugin => plugin.BudgetSkippedBlocks);
        public long MissedSinceStart => MissedNow - MissedAtStart;
        public long BudgetSkippedSinceStart => BudgetSkippedNow - BudgetSkippedAtStart;
        public bool LinkFailed => Plugins.Any(plugin => plugin.LinkFailed);

        public void Dispose()
        {
            foreach (var plugin in Plugins) plugin.Dispose();
            Threads.RunPending();
        }
    }

    public static Chain StartSingle(int index, int maxBlock = MaxBlock)
    {
        var (plugin, threads) = OperatingSystem.IsWindows()
            ? Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: maxBlock)
            : OperatingSystem.IsLinux()
                ? Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock)
                : throw new PlatformNotSupportedException("Stage 15 stress runs on Windows or Linux");
        var chain = new Chain { Index = index, Plugins = [plugin], Threads = threads };
        chain.MissedAtStart = (int)chain.MissedNow;
        chain.BudgetSkippedAtStart = (int)chain.BudgetSkippedNow;
        return chain;
    }

    public static Chain StartChain(int index, int maxBlock = MaxBlock)
    {
        var plugins = new RemotePlugin[PluginsPerChain];
        EngineThreads threads;
        if (OperatingSystem.IsWindows())
        {
            var first = Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: maxBlock);
            threads = first.Threads;
            for (var i = 0; i < PluginsPerChain; i++)
            {
                var bit64 = i % 2 == 1;
                if (i == 0) plugins[i] = first.Plugin;
                else plugins[i] = bit64
                    ? Vst32TestAssets.StartTcp64(Vst32TestAssets.TestPluginPath64!, instrument: false, maxBlock: maxBlock).Plugin
                    : Vst32TestAssets.StartTcp(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: maxBlock).Plugin;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var first = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock);
            threads = first.Threads;
            for (var i = 0; i < PluginsPerChain; i++)
            {
                plugins[i] = i == 0
                    ? first.Plugin
                    : i % 2 == 1
                        ? Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath64!, instrument: false, maxBlock).Plugin
                        : Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock).Plugin;
            }
        }
        else throw new PlatformNotSupportedException("Stage 15 stress runs on Windows or Linux");

        var chain = new Chain { Index = index, Plugins = plugins, Threads = threads };
        chain.MissedAtStart = (int)chain.MissedNow;
        chain.BudgetSkippedAtStart = (int)chain.BudgetSkippedNow;
        return chain;
    }

    /// <summary>Runs one chain for a wall-clock window at a realtime cadence; returns false when the chain was already failed.</summary>
    public static void RunPaced(Chain chain, int frames, double seconds)
    {
        // The production audio callback runs with the audio-thread setup (MMCSS on Windows, denormals flushed), at normal
        // base priority; the old harness also set Highest, which starved the helper on the same core and reported misses
        // the shipping engine cannot have. Only the engine's device-driver fallback raises the thread priority.
        try { chain.Threads.MarkAudioThread(); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        var stop = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        var blockTicks = frames * (double)Stopwatch.Frequency / Rate;
        var next = Stopwatch.GetTimestamp();
        var signal = new float[frames];
        var output = new[] { new float[frames], new float[frames] };
        var channels = new[] { signal, signal };
        for (var i = 0; i < frames; i++) signal[i] = (float)(0.1 * Math.Sin(2 * Math.PI * 220 * i / Rate));
        var block = 0;
        // Diagnostic only (TABFORGE_LOAD_UNSHARED=1): skip the callback-wide deadline so every link gets its own
        // 75 % wait. If the chain then finishes a block in time, a shared budget that ran out early caused the skips.
        var sharedBudget = Environment.GetEnvironmentVariable("TABFORGE_LOAD_UNSHARED") != "1";
        while (Stopwatch.GetTimestamp() < stop && !chain.LinkFailed)
        {
            var callStart = Stopwatch.GetTimestamp();
            if (sharedBudget) PluginHostLink.BeginCallback(callStart, frames, Rate);
            try
            {
                for (var i = 0; i < chain.Plugins.Length; i++)
                {
                    chain.Plugins[i].Process(channels, output, frames, ReadOnlySpan<BlockMidi>.Empty, Transport(block, frames));
                    // The signal flows through the chain: this plug-in's dry output is the next one's input.
                    output[0].AsSpan(0, frames).CopyTo(signal);
                }
            }
            finally { if (sharedBudget) PluginHostLink.EndCallback(); }
            chain.Blocks++;
            block++;
            next += (long)blockTicks;
            var now = Stopwatch.GetTimestamp();
            if (now < next)
            {
                var remainingMs = (next - now) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 1.5) Thread.Sleep((int)(remainingMs - 1));
                while (Stopwatch.GetTimestamp() < next) Thread.SpinWait(20);
            }
            else if (now - next > Stopwatch.Frequency / 1000) chain.LateCallbacks++;   // started more than 1 ms after its slot
        }
    }

    public static bool PinCurrentThread(int cpu)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << (cpu % 64))) != UIntPtr.Zero;
            var mask = new byte[128];
            mask[cpu / 8] |= (byte)(1 << (cpu % 8));
            return sched_setaffinity(0, (IntPtr)mask.Length, mask) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { return false; }
    }

    public static Stage15Metrics.Sample TakeSample(Chain chain) => Stage15Metrics.Take(chain);

    private static TransportInfo Transport(int block, int frames) => new()
    {
        Tempo = 120, PpqPosition = block * frames / 2000.0, Playing = true,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
    [DllImport("libc", SetLastError = true)] private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, byte[] mask);
}

internal static class Stage15Metrics
{
    public sealed record Sample(int Minute, long EngineWorkingSet, long EnginePrivate, int EngineHandles,
        long HelperWorkingSet, long HelperPrivate, int HelperHandles, long MissedBlocks, long BudgetSkipped, bool LinkFailed);

    public static Sample Take(Stage15StressKit.Chain chain)
    {
        var engine = Process.GetCurrentProcess();
        engine.Refresh();
        long helperWs = 0, helperPrivate = 0;
        var helperHandles = 0;
        foreach (var plugin in chain.Plugins)
        {
            try
            {
                using var process = Process.GetProcessById(plugin.HostProcessId);
                process.Refresh();
                helperWs += process.WorkingSet64;
                helperPrivate += process.PrivateMemorySize64;
                helperHandles += Handles(process);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return new Sample(0, engine.WorkingSet64, engine.PrivateMemorySize64, Handles(engine),
            helperWs, helperPrivate, helperHandles, chain.MissedNow, chain.BudgetSkippedNow, chain.LinkFailed);
    }

    public static int Handles(Process process)
    {
        if (OperatingSystem.IsWindows()) return process.HandleCount;
        try { return Directory.GetFiles($"/proc/{process.Id}/fd").Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return -1; }
    }

    public static string CsvHeader =>
        "minute,engine_working_set,engine_private,engine_handles,helper_working_set,helper_private,helper_handles,missed_blocks,budget_skipped,link_failed";

    public static string Csv(Sample sample, int minute) =>
        $"{minute},{sample.EngineWorkingSet},{sample.EnginePrivate},{sample.EngineHandles}," +
        $"{sample.HelperWorkingSet},{sample.HelperPrivate},{sample.HelperHandles},{sample.MissedBlocks},{sample.BudgetSkipped},{sample.LinkFailed}";
}
