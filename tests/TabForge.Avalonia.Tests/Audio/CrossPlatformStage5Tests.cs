using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Isolation;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: cross-platform Stage 5 leftovers: the plug-in host and import-worker pipe names per OS, the plug-in host block with polled flags (run on Windows too), and the Unix child-process guard, the polled memory/CPU limits of the portable import-worker guard, and the parent watchdog.
// Does not own: the Windows named-event block (EngineSessionIsolationTests) or the Job Object (SelfTestChildProcessGuardPort).
// Tests: itself.
public sealed class CrossPlatformStage5Tests
{
    [Fact]
    public void ThePluginHostPipeKeepsItsWindowsNameAndIsShortElsewhere()
    {
        var name = PipeNames.PluginHost("0123456789abcdef0123456789abcdef");
        if (OperatingSystem.IsWindows()) Assert.Equal("TabForge.PluginHost.0123456789abcdef0123456789abcdef", name);
        else Assert.Equal(PipeNames.Short("TabForge.PluginHost.0123456789abcdef0123456789abcdef"), name);
        Assert.StartsWith("tf.", PipeNames.Short("x"));
        Assert.True(PipeNames.Short("TabForge.PluginHost.0123456789abcdef0123456789abcdef").Length < 30);
    }

    [Fact]
    public void TheImportWorkerPipeIsRecognisedByTheWorkerOnEveryOs()
    {
        const string prefix = "TabForge.ImportWorker.";
        var name = PipeNames.ImportWorker(prefix, Guid.NewGuid().ToString("N"));
        Assert.True(TabForge.Services.ImportWorker.IsWorkerPipeName(name));
        Assert.True(TabForge.Services.ImportWorker.IsWorkerPipeName(prefix + "abc"));
        Assert.False(TabForge.Services.ImportWorker.IsWorkerPipeName("TabForge.Other.abc"));
        Assert.Equal(!OperatingSystem.IsWindows(), TabForge.Services.ImportWorker.IsWorkerPipeName(PipeNames.Short("x")));
    }

    [Fact]
    public async Task ThePortablePluginHostBlockCarriesAudioAndHandsOverByGeneration()
    {
        var id = Guid.NewGuid().ToString("N");
        using var engine = PluginHostBlock.Create(id, 256, portable: true);
        using var host = PluginHostBlock.Open(id, 256, portable: true);
        var served = Task.Run(() =>
        {
            Assert.True(host.TakeRequest(5000, out var generation));
            Assert.Equal(7, generation);
            Assert.Equal(128, host.Frames);
            host.Channel(2)[0] = host.Channel(0)[0] * 2f;
            host.Complete(generation);
        });
        engine.Frames = 128;
        engine.Channel(0)[0] = 0.25f;
        engine.Send(7);
        Assert.True(engine.WaitDone(7, 5000));
        await served;
        Assert.Equal(0.5f, engine.Channel(2)[0]);
    }

    [Fact]
    public void ALateAnswerToAnEarlierBlockIsNotTakenForTheCurrentOne()
    {
        var id = Guid.NewGuid().ToString("N");
        using var engine = PluginHostBlock.Create(id, 64, portable: true);
        using var host = PluginHostBlock.Open(id, 64, portable: true);
        host.Complete(3);
        Assert.False(engine.WaitDone(4, 50));
        host.Complete(4);
        Assert.True(engine.WaitDone(4, 1000));
    }

    [Fact]
    public void TheFlagSignalIsAutoReset()
    {
        var id = Guid.NewGuid().ToString("N");
        using var block = PluginHostBlock.Create(id, 64, portable: true);
        Assert.False(block.TakeRequest(20, out _));
        block.Send(1);
        Assert.True(block.TakeRequest(1000, out var generation));
        Assert.Equal(1, generation);
        Assert.False(block.TakeRequest(20, out _));
    }

    private static Process StartSleeper()
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 60 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
            : new ProcessStartInfo("sleep", "60") { UseShellExecute = false };
        return Process.Start(info) ?? throw new InvalidOperationException("no sleeper");
    }

    [Fact]
    public void TheUnixGuardEndsItsChildrenWhenDisposed()
    {
        var guard = new UnixChildProcessGuard();
        using var child = StartSleeper();
        Assert.True(guard.IsActive);
        Assert.True(guard.Add(child));
        guard.Dispose();
        Assert.True(child.WaitForExit(10000), "the child was still running after the guard closed");
        Assert.False(guard.IsActive);
        using var late = StartSleeper();
        try { Assert.False(guard.Add(late)); }
        finally { late.Kill(true); }
    }

    [Fact]
    public void TheWatchdogFiresOnceWhenTheParentIsGone()
    {
        var alive = true;
        var fired = 0;
        using var gone = new ManualResetEventSlim();
        using var watchdog = new ParentWatchdog(Environment.ProcessId, () => { Interlocked.Increment(ref fired); gone.Set(); }, TimeSpan.FromMilliseconds(20), (_, _) => Volatile.Read(ref alive));
        Assert.False(gone.Wait(150));
        Volatile.Write(ref alive, false);
        Assert.True(gone.Wait(3000));
        Thread.Sleep(100);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void TheWatchdogSeesARunningAndAnEndedProcess()
    {
        Assert.True(ParentWatchdog.IsRunning(Environment.ProcessId, null));
        using var child = StartSleeper();
        var started = child.StartTime;
        Assert.True(ParentWatchdog.IsRunning(child.Id, started));
        Assert.False(ParentWatchdog.IsRunning(child.Id, started.AddMinutes(-5)), "a reused id with another start time is not the parent");
        child.Kill(true);
        child.WaitForExit(10000);
        Assert.False(ParentWatchdog.IsRunning(child.Id, started));
    }

    [Fact]
    public void ThePolledGuardKillsAChildOverItsMemoryLimit()
    {
        var resident = 10L;
        using var guard = new PolledLimitChildGuard(100, TimeSpan.Zero, TimeSpan.FromMilliseconds(20), _ => (Volatile.Read(ref resident), TimeSpan.Zero));
        using var child = StartSleeper();
        Assert.True(guard.IsActive);
        Assert.True(guard.Add(child));
        Assert.False(child.WaitForExit(300), "a child under its limit was ended");
        Volatile.Write(ref resident, 101);
        Assert.True(child.WaitForExit(10000), "a child over its memory limit was still running");
    }

    [Fact]
    public void ThePolledGuardKillsAChildOverItsCpuTimeAndEndsItsChildrenWhenDisposed()
    {
        using (var guard = new PolledLimitChildGuard(0, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), _ => (long.MaxValue, TimeSpan.FromSeconds(2))))
        {
            using var child = StartSleeper();
            Assert.True(guard.Add(child));
            Assert.True(child.WaitForExit(10000), "a child over its CPU time was still running");
        }
        var closing = new PolledLimitChildGuard(0, TimeSpan.Zero, TimeSpan.FromMilliseconds(20), _ => (0, TimeSpan.Zero));
        using var held = StartSleeper();
        Assert.True(closing.Add(held));
        closing.Dispose();
        Assert.True(held.WaitForExit(10000), "the child was still running after the guard closed");
        Assert.False(closing.IsActive);
    }

    [Theory]
    [InlineData(101L, 0, 100L, 0, true)]
    [InlineData(100L, 0, 100L, 0, false)]
    [InlineData(long.MaxValue, 0, 0L, 0, false)]
    [InlineData(0L, 3, 0L, 2, true)]
    [InlineData(0L, 2, 0L, 0, false)]
    public void ThePolledLimitsCompareUsageWithZeroMeaningOff(long resident, int cpuSeconds, long memoryLimit, int cpuLimitSeconds, bool over) =>
        Assert.Equal(over, PolledLimitChildGuard.IsOverLimit(resident, TimeSpan.FromSeconds(cpuSeconds), memoryLimit, TimeSpan.FromSeconds(cpuLimitSeconds)));

    [Fact]
    public void ThePlatformGuardIsTheJobObjectOnWindows()
    {
        if (OperatingSystem.IsWindows()) Assert.IsNotType<UnixChildProcessGuard>(ChildProcessGuards.ForThisProcess);
        else Assert.IsType<UnixChildProcessGuard>(ChildProcessGuards.ForThisProcess);
    }
}
