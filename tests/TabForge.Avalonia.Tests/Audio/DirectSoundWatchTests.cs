using System;
using System.Diagnostics;
using NAudio.Wave;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Output;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the DirectSound output's buffer sizing (the user's block size is honoured, never below the floor), its refill monitor (late / dry / slow counted,
// logged at most once a second) and that wrapping the mix costs the audio thread no allocation.
// Does not own: opening the device (needs hardware) or the mix.
// Tests: this file.
public class DirectSoundWatchTests
{
    private sealed class Silence : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) => count;
    }

    private static long Ms(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(64, 100)]
    [InlineData(256, 100)]     // 6 ms block: the floor
    [InlineData(1024, 100)]    // 22 ms block: 4 blocks = 88 ms, still the floor
    [InlineData(2048, 172)]    // 43 ms block: 4 blocks
    [InlineData(4096, 344)]
    public void TheLatencyFollowsTheBufferSettingAboveTheFloor(int frames, int expected) =>
        Assert.Equal(expected, DirectSoundWatch.LatencyMs(frames, 48000));

    [Fact]
    public void ALargerBufferSettingNeverShrinksTheLatency()
    {
        var previous = 0;
        foreach (var frames in new[] { 16, 128, 512, 1024, 2048, 4096, 8192 })
        {
            var latency = DirectSoundWatch.LatencyMs(frames, 44100);
            Assert.True(latency >= DirectSoundWatch.MinLatencyMs && latency >= previous);
            previous = latency;
        }
    }

    [Fact]
    public void RefillsOnTimeAreNotCounted()
    {
        var watch = new DirectSoundWatch(new Silence(), 100, null);
        long t = Stopwatch.GetTimestamp();
        for (var i = 0; i < 40; i++, t += Ms(100 + (i % 2 == 0 ? 9 : -9))) watch.Observe(t, t + Ms(5));   // the +-10 ms jitter of the device's own cadence
        Assert.Equal((40L, 0L, 0L, 0L), watch.Counts);
    }

    [Fact]
    public void LateDrySlowRefillsAreCountedApart()
    {
        var watch = new DirectSoundWatch(new Silence(), 100, null);
        long t = 1000;
        for (var i = 0; i < 6; i++, t += Ms(100)) watch.Observe(t, t + Ms(5));
        t += Ms(60); watch.Observe(t, t + Ms(5));    // 160 ms since the last: late (1.5 halves)
        t += Ms(100); watch.Observe(t, t + Ms(5));
        t += Ms(230); watch.Observe(t, t + Ms(5));   // above 2 halves: the buffer ran dry
        t += Ms(100); watch.Observe(t, t + Ms(70));  // rendering took more than half a period
        var (calls, late, dry, slow) = watch.Counts;
        Assert.Equal((10L, 1L, 1L, 1L), (calls, late, dry, slow));
    }

    [Fact]
    public void TheLogIsPostedAtMostOncePerSecond()
    {
        var threads = new EngineThreads(new EngineLogWriter());
        var watch = new DirectSoundWatch(new Silence(), 100, threads);
        long t = Stopwatch.GetTimestamp();
        for (var i = 0; i < 6; i++, t += Ms(100)) watch.Observe(t, t + Ms(5));
        for (var i = 0; i < 8; i++, t += Ms(250)) watch.Observe(t, t + Ms(5));   // eight dry refills over two seconds
        Assert.InRange(threads.Queued, 1, 3);
        threads.RunPending();
        Assert.Equal(0, threads.Queued);
    }

    [Fact]
    public void ReadingThroughTheWatchAllocatesNothing()
    {
        var watch = new DirectSoundWatch(new Silence(), 100, null);
        var buffer = new float[9600];
        for (var i = 0; i < 100; i++) watch.Read(buffer, 0, buffer.Length);   // warm up (JIT, first-call priority step)
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 5000; i++) watch.Read(buffer, 0, buffer.Length);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(5100L, watch.Counts.Calls);
    }

    [Fact]
    public void ReadingThroughTheWatchReturnsTheSourceCountAndFormat()
    {
        var source = new Silence();
        var watch = new DirectSoundWatch(source, 100, null);
        Assert.Same(source.WaveFormat, watch.WaveFormat);
        Assert.Equal(512, watch.Read(new float[512], 0, 512));
    }
}
