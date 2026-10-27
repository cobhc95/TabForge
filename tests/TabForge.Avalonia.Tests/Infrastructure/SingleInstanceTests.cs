using System;
using System.IO;
using System.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the lock-file single instance (a live holder blocks a second take, a leftover file from a crash does not) and the pipe hand-off of a song to the running instance.
/// Does not own: the Windows focus call (needs a visible second window).
/// Tests: TabForge.Avalonia.Tests.SingleInstanceTests.
/// </summary>
[Collection("UserPaths")]
public class SingleInstanceTests
{
    [Fact]
    public void LiveHolderBlocksASecondTake_AndReleaseFreesIt()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "tf-lock-" + Guid.NewGuid().ToString("N"), "a.lock");
        try
        {
            Assert.True(AvaloniaSingleInstance.TryAcquire(lockPath, out var first));
            Assert.False(AvaloniaSingleInstance.TryAcquire(lockPath, out var second));
            Assert.Null(second);
            first!.Dispose();
            Assert.True(AvaloniaSingleInstance.TryAcquire(lockPath, out var third));
            third!.Dispose();
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(lockPath)!, true); } catch (IOException) { } }
    }

    [Fact]
    public void StaleLockFileFromACrashDoesNotBlock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tf-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "stale.lock");
        File.WriteAllText(lockPath, "12345"); // what a killed process leaves: the file, with no handle on it
        try
        {
            Assert.True(AvaloniaSingleInstance.TryAcquire(lockPath, out var held));
            held!.Dispose();
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void LockNamesFollowTheExePath()
    {
        Assert.Equal(AvaloniaSingleInstance.LockNameFor(@"C:\App\TabForge.exe"), AvaloniaSingleInstance.LockNameFor(@"C:\APP\tabforge.exe"));
        Assert.NotEqual(AvaloniaSingleInstance.LockNameFor(@"C:\App\TabForge.exe"), AvaloniaSingleInstance.LockNameFor(@"D:\Other\TabForge.exe"));
    }

    [Fact]
    public void SecondLaunchHandsItsSongToTheRunningInstance()
    {
        if (!OperatingSystem.IsWindows()) return; // the pipe name is derived from the Windows account today
        var dir = Path.Combine(Path.GetTempPath(), "tf-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var song = Path.Combine(dir, "song.gp5");
        File.WriteAllBytes(song, new byte[8]);
        var before = UserPaths.ProfileRoot;
        var switchBefore = Environment.GetEnvironmentVariable("TABFORGE_TEST_SINGLE_INSTANCE");
        using var cts = new CancellationTokenSource();
        try
        {
            UserPaths.SetProfile(dir); // an isolated pipe name: never reaches a real running TabForge
            Environment.SetEnvironmentVariable("TABFORGE_TEST_SINGLE_INSTANCE", "1");
            Assert.False(SingleInstanceService.TrySendToRunningInstance(song)); // nobody listens yet
            string? received = null;
            using var got = new ManualResetEventSlim();
            using var listening = new ManualResetEventSlim();
            SingleInstanceService.StartServer(p => { received = p; got.Set(); }, cts.Token, () => listening.Set());
            Assert.True(listening.Wait(5000));
            Assert.True(SingleInstanceService.TrySendToRunningInstance(song));
            Assert.True(got.Wait(5000));
            Assert.Equal(song, received);
        }
        finally
        {
            cts.Cancel();
            Environment.SetEnvironmentVariable("TABFORGE_TEST_SINGLE_INSTANCE", switchBefore);
            UserPaths.SetProfile(before);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
