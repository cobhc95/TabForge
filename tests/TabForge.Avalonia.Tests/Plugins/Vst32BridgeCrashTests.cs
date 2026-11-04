using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Transport;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the stage-6 crash and hang rules with real processes (killed helper, stalled audio thread, engine death) and the notice wording.
// Does not own: the real-fixture matrix (Vst32RealStage6Tests) or the block/link units (Vst32BridgeStage6Tests).
[Collection("Vst32Native")]
public sealed class Vst32BridgeCrashTests
{
    private const int Frames = 256;
    private const string StallVariable = "TABFORGE_TFVST32_TEST_STALL_AUDIO_MS";
    private readonly ITestOutputHelper _output;

    public Vst32BridgeCrashTests(ITestOutputHelper output) => _output = output;

    [Vst32Fact]
    public void KillingTheHelperDuringAProcessLoopIsDryReportedOnceAndDisposesQuickly()
    {
        var (plugin, threads) = Vst32TestAssets.StartWithThreads(Vst32TestAssets.TestPluginPath!, instrument: false, Frames, bufferSlots: 2);
        var crashed = 0;
        var crashSignal = new ManualResetEventSlim();
        plugin.Crashed += _ => { Interlocked.Increment(ref crashed); crashSignal.Set(); };
        var input = Enumerable.Repeat(0.25f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };
        ProcessBlock(plugin, input, output);
        var pid = plugin.HostProcessId;
        var errors = new ConcurrentQueue<Exception>();
        var stop = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            while (!stop.IsSet)
            {
                try { ProcessBlock(plugin, input, output); }
                catch (Exception ex) { errors.Enqueue(ex); }
            }
        }) { IsBackground = true, Name = "tf-vst32-kill-loop" };
        worker.Start();
        Thread.Sleep(80);
        var killedAt = Stopwatch.GetTimestamp();
        using (var process = Process.GetProcessById(pid)) process.Kill(true);
        while (Stopwatch.GetElapsedTime(killedAt).TotalMilliseconds < 2000 && !crashSignal.IsSet)
        {
            threads.RunPending();
            Thread.Sleep(2);
        }
        var noticedMs = Stopwatch.GetElapsedTime(killedAt).TotalMilliseconds;
        stop.Set();
        worker.Join(2000);
        threads.RunPending();

        Assert.True(crashSignal.IsSet, "the crash must be reported");
        // Prompt, not instant: a loaded machine (parallel dotnet/test helper runs) can delay the event by a few hundred ms.
        Assert.InRange(noticedMs, 0, 2000);
        Assert.Equal(1, crashed);
        Assert.Empty(errors);
        for (var i = 0; i < 20; i++)
        {
            ProcessBlock(plugin, input, output);
            Assert.Equal(input, output[0]);
        }
        var disposeAt = Stopwatch.GetTimestamp();
        plugin.Dispose();
        Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 2000, "Dispose must finish within 2 s");
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 2000), "the killed helper must be gone");
        Assert.DoesNotContain(pid, Process.GetProcessesByName("tfvst32").Select(p => p.Id).ToArray());
    }

    [Vst32RealFact]
    public void TheHelperExitsWhenItsEngineProcessDies()
    {
        var dummy = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
        {
            UseShellExecute = false, CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("the dummy engine process did not start");
        var id = Guid.NewGuid().ToString("N");
        using var block = PluginHostBlock.Create(id, Frames, bufferSlots: 2);
        using var server = new NamedPipeServerStream(PipeNames.PluginHost(id), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var spec = Vst32TestAssets.Spec(Vst32TestAssets.FixturePath("PurestGain32"), instrument: false);
        var launch = new PluginHostLaunch(Vst32TestAssets.HelperPath!, PluginHostLaunch.Vst32HostMode, 2);
        var start = new ProcessStartInfo(launch.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in launch.Arguments(id, dummy.Id, 48000, Frames, spec, null)) start.ArgumentList.Add(argument);
        var helper = Process.Start(start) ?? throw new InvalidOperationException("the helper did not start");
        var helperPid = helper.Id;
        try
        {
            Assert.True(server.WaitForConnectionAsync().Wait(8000), "the helper did not connect");
            // The helper sends hello + loaded before its loop: drain them so its pipe write cannot block.
            var handshake = System.Threading.Tasks.Task.Run(() =>
            {
                using var hello = TabForge.Audio.Contracts.Frames.Read(server, IsolatedFrameLimits.ForReply);
                using var loaded = TabForge.Audio.Contracts.Frames.Read(server, IsolatedFrameLimits.ForReply);
            });
            Assert.True(handshake.Wait(8000), "the helper did not finish its handshake");
            Assert.False(helper.WaitForExit(200), "the helper must stay while its engine process lives");
            using (var engine = Process.GetProcessById(dummy.Id)) engine.Kill(true);
            Assert.True(dummy.WaitForExit(2000), "the dummy engine did not exit");
            Assert.True(helper.WaitForExit(6000), $"the helper must exit when its engine process dies (exited={helper.HasExited})");
            _output.WriteLine($"engine-death helper exit code {helper.ExitCode}");
            Assert.Equal(0, helper.ExitCode);
        }
        finally
        {
            try { if (!helper.HasExited) helper.Kill(true); } catch (InvalidOperationException) { }
            try { if (!dummy.HasExited) dummy.Kill(true); } catch (InvalidOperationException) { }
            Assert.True(Vst32TestAssets.WaitsForExit(helperPid, 2000), "the helper must not be left behind");
            helper.Dispose();
            dummy.Dispose();
        }
    }

    [Vst32HookedFact]
    public void AStalledAudioThreadIsBypassedThenFailsPermanently()
    {
        Environment.SetEnvironmentVariable(StallVariable, "3000");
        RemotePlugin plugin;
        EngineThreads threads;
        try
        {
            (plugin, threads) = Vst32TestAssets.StartWithThreads(Vst32TestAssets.TestPluginPath!, instrument: false, Frames,
                bufferSlots: 2, helperPath: Vst32TestAssets.HookedHelperPath!);
        }
        finally { Environment.SetEnvironmentVariable(StallVariable, null); }
        var crashed = 0;
        var crashSignal = new ManualResetEventSlim();
        plugin.Crashed += _ => { Interlocked.Increment(ref crashed); crashSignal.Set(); };
        var pid = plugin.HostProcessId;
        var input = Enumerable.Repeat(0.2f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };

        var firstAt = Stopwatch.GetTimestamp();
        plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
        var firstMs = Stopwatch.GetElapsedTime(firstAt).TotalMilliseconds;
        Assert.True(plugin.MissedBlocks >= 1, "the missed block must be counted");
        Assert.Equal(input, output[0]);
        Assert.InRange(firstMs, 0, 60);

        long ticks = 0;
        double maxCallMs = 0;
        var loop = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(loop).TotalMilliseconds < 3000 && !crashSignal.IsSet)
        {
            var callAt = Stopwatch.GetTimestamp();
            plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(1, Frames));
            maxCallMs = Math.Max(maxCallMs, Stopwatch.GetElapsedTime(callAt).TotalMilliseconds);
            threads.RunPending();
            ticks++;
            Thread.SpinWait(20);
        }

        Assert.True(crashSignal.IsSet, "the stalled helper must fail after the grace period");
        Assert.Equal(1, crashed);
        Assert.True(plugin.LinkFailed, "the link must be permanently failed");
        Assert.True(ticks > 100, $"the UI-like thread must stay responsive (ticks {ticks})");
        Assert.InRange(maxCallMs, 0, 60);
        var misses = plugin.MissedBlocks;
        for (var i = 0; i < 5; i++) plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, default);
        Assert.Equal(input, output[0]);
        Assert.Equal(misses, plugin.MissedBlocks);   // permanent failure: no more requests, no more misses

        var disposeAt = Stopwatch.GetTimestamp();
        plugin.Dispose();
        Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 2000, "Dispose must finish within 2 s");
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 2000), "the hung helper must be gone");
    }

    [Vst32Fact]
    public void AProcessFaultIsSilentAndTheHelperKeepsAnswering()
    {
        var path = CopyTestPlugin("passthrough32.fault");
        try
        {
            var (plugin, _) = Vst32TestAssets.StartWithThreads(path, instrument: false, Frames, bufferSlots: 2);
            var pid = plugin.HostProcessId;
            using (plugin)
            {
                var input = Enumerable.Repeat(0.25f, Frames).ToArray();
                var output = new[] { new float[Frames], new float[Frames] };
                for (var block = 0; block < 6; block++)
                {
                    plugin.Process(new[] { input, input }, output, Frames,
                        ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
                    Assert.All(output[0], sample => Assert.Equal(0f, sample));
                    Assert.All(output[1], sample => Assert.Equal(0f, sample));
                    Assert.False(plugin.LinkFailed, "a plug-in fault must not fail the link");
                }
                Assert.Equal(0, plugin.MissedBlocks);
                Assert.Equal(LoadedPluginInfo.Vst2, plugin.Describe().Format);   // the helper still answers commands
                Assert.True(plugin.SetParameter(0, 0.6f));
                Assert.False(Vst32TestAssets.WaitsForExit(pid, 10), "the helper must stay alive");
            }
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the helper must exit cleanly");
        }
        finally { DeleteFolder(path); }
    }

    [Fact]
    public void TheHelperCrashNoticeNeverAccusesThePlugin()
    {
        var text = AvaloniaEngineEvents.CrashNotice(@"C:\Plugins\Crashy.dll", tooManyCrashes: false, helperStopped: true);
        Assert.Equal("Crashy crashed in its 32-bit helper and was switched off; playback continues", text);
        Assert.DoesNotContain("Crashy stopped working", text);
        var withReport = AvaloniaEngineEvents.CrashNotice(@"C:\Plugins\Crashy.dll", tooManyCrashes: false, helperStopped: true,
            reportPath: @"C:\Users\me\AppData\Local\TabForge\Diagnostics\tfvst32-42.dmp");
        Assert.Equal("Crashy crashed in its 32-bit helper and was switched off; playback continues. Report: C:\\Users\\me\\AppData\\Local\\TabForge\\Diagnostics\\tfvst32-42.dmp", withReport);
        Assert.Equal(AvaloniaEngineEvents.TooManyCrashesNotice, AvaloniaEngineEvents.CrashNotice(@"C:\Plugins\Crashy.dll", tooManyCrashes: true, helperStopped: true));
    }

    private static string CopyTestPlugin(params string[] markers)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-vst32-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var dll = Path.Combine(folder, "passthrough32.dll");
        File.Copy(Vst32TestAssets.TestPluginPath!, dll);
        foreach (var marker in markers) File.WriteAllText(Path.Combine(folder, marker), "1");
        return dll;
    }

    private static void DeleteFolder(string dll)
    {
        try { Directory.Delete(Path.GetDirectoryName(dll)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ProcessBlock(RemotePlugin plugin, float[] input, float[][] output) =>
        plugin.Process(new[] { input, input }, output, input.Length, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, input.Length));
}
