using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: the opt-in engine stress and timing runs (performance audit 2026-11): many General MIDI tracks in the live callback,
//   rapid play / seek / stop loops through the shared ring, and the offline render path. They print numbers and assert only
//   that the callback allocates nothing once warm and that every block stays finite.
// Does not own: the import and compile timing (CorpusPerfTests), the callback rules themselves (MixEngine).
// Env: TABFORGE_PERF=1 to run (else each test returns at once). Needs the Windows GM bank (gm.dls).
[Collection("EngineHeadless")]
public sealed class EnginePerfStressTests
{
    private readonly ITestOutputHelper _output;
    public EnginePerfStressTests(ITestOutputHelper output) => _output = output;

    private static bool Enabled => Environment.GetEnvironmentVariable("TABFORGE_PERF") == "1"
        && OperatingSystem.IsWindows() && File.Exists(AudioEngine.Synth.DlsToSoundFont.WindowsBankPath);

    private static void LoadGmTracks(int tracks)
    {
        EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
        for (var slot = 0; slot < tracks; slot++) EH.LoadChain(slot, "t" + slot, true, new List<PluginSpec>());
    }

    /// <summary>Queues one chord per track (4 notes on, program change once) stamped now, through the shared ring as playback does.</summary>
    private static int QueueNotes(SharedBlock shared, int tracks, int step, bool off)
    {
        var now = Stopwatch.GetTimestamp();
        var written = 0;
        for (var slot = 0; slot < tracks; slot++)
        {
            var ch = (byte)(slot % 16 == 9 ? 0 : slot % 16);
            for (var k = 0; k < 4; k++)
            {
                var note = (byte)(40 + (step * 7 + slot * 3 + k * 4) % 48);
                if (shared.TryWrite(new TimedMidi { Timestamp = now, Slot = slot, Status = (byte)((off ? 0x80 : 0x90) | ch), Data1 = note, Data2 = 100 })) written++;
            }
        }
        return written;
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(64)]
    public void ManyGmTracksInTheLiveCallback(int tracks)
    {
        if (!Enabled) return;
        using var shared = SharedBlock.Create($"tf-perf-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => throw new InvalidOperationException("no plug-ins in this run"));
        try
        {
            LoadGmTracks(tracks);
            var mix = EH.Mix!;
            EH.SetPlaying(true, 0);
            var block = new float[512];   // 256 frames, stereo
            for (var i = 0; i < 200; i++) { if (i % 20 == 0) QueueNotes(shared, tracks, i, false); mix.Read(block, 0, block.Length); }   // warm-up
            const int Blocks = 3000;
            var times = new double[Blocks];
            var alloc = 0L;
            var gc0 = GC.CollectionCount(0);
            for (var i = 0; i < Blocks; i++)
            {
                if (i % 24 == 0) QueueNotes(shared, tracks, i, i % 48 == 0);   // a chord every 128 ms, released every other one
                var a = GC.GetAllocatedBytesForCurrentThread();
                var t = Stopwatch.GetTimestamp();
                mix.Read(block, 0, block.Length);
                times[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds * 1000;
                alloc += GC.GetAllocatedBytesForCurrentThread() - a;
                Assert.True(block.All(float.IsFinite));
            }
            Array.Sort(times);
            var budget = 256.0 / 48000 * 1e6;
            _output.WriteLine($"{tracks} GM tracks, {Blocks} blocks of 256 frames (budget {budget:F0} us): mean {times.Average():F1} us, p50 {times[Blocks / 2]:F1}, p99 {times[Blocks * 99 / 100]:F1}, max {times[^1]:F1}; " +
                $"allocated {alloc} B; gen0 collections {GC.CollectionCount(0) - gc0}");
            Assert.Equal(0, alloc);
        }
        finally { EH.Detach(); }
    }

    [Fact]
    public void RapidPlaySeekStopLoops()
    {
        if (!Enabled) return;
        const int Tracks = 24;
        using var shared = SharedBlock.Create($"tf-perf-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => throw new InvalidOperationException("no plug-ins in this run"));
        try
        {
            LoadGmTracks(Tracks);
            var mix = EH.Mix!;
            var block = new float[512];
            for (var i = 0; i < 100; i++) mix.Read(block, 0, block.Length);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var heap0 = GC.GetTotalMemory(true);
            var rng = new Random(7);
            var sw = Stopwatch.StartNew();
            var alloc = 0L;
            var maxBlock = 0.0;
            const int Cycles = 2000;
            for (var cycle = 0; cycle < Cycles; cycle++)
            {
                // Play from a random point, a few blocks of notes, a seek (ordered panic per slot, as the session sends), then stop.
                EH.SetPlaying(true, rng.NextDouble() * 300);
                QueueNotes(shared, Tracks, cycle, false);
                for (var b = 0; b < 3; b++) maxBlock = Math.Max(maxBlock, TimedRead(mix, block, ref alloc));
                var now = Stopwatch.GetTimestamp();
                for (var slot = 0; slot < Tracks; slot++) shared.TryWrite(new TimedMidi { Timestamp = now, Slot = slot, Flags = TimedMidi.PanicFlag });
                EH.SetPlaying(true, rng.NextDouble() * 300);
                QueueNotes(shared, Tracks, cycle + 1, false);
                for (var b = 0; b < 2; b++) maxBlock = Math.Max(maxBlock, TimedRead(mix, block, ref alloc));
                EH.SetPlaying(false, 0);
                mix.Panic();
                maxBlock = Math.Max(maxBlock, TimedRead(mix, block, ref alloc));
                Assert.True(block.All(float.IsFinite));
            }
            sw.Stop();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var heap1 = GC.GetTotalMemory(true);
            _output.WriteLine($"{Cycles} play/seek/stop cycles on {Tracks} GM tracks in {sw.ElapsedMilliseconds} ms; callback allocated {alloc} B; slowest block {maxBlock:F0} us; " +
                $"managed heap {heap0 / 1024} KB -> {heap1 / 1024} KB; ring drops {shared.DroppedWrites}");
            Assert.Equal(0, alloc);
            Assert.True(heap1 - heap0 < 4 << 20, $"the heap grew {(heap1 - heap0) / 1024} KB over {Cycles} cycles");
        }
        finally { EH.Detach(); }
    }

    private static double TimedRead(MixEngine mix, float[] block, ref long alloc)
    {
        var a = GC.GetAllocatedBytesForCurrentThread();
        var t = Stopwatch.GetTimestamp();
        mix.Read(block, 0, block.Length);
        var us = Stopwatch.GetElapsedTime(t).TotalMilliseconds * 1000;
        alloc += GC.GetAllocatedBytesForCurrentThread() - a;
        return us;
    }

    [Theory]
    [InlineData(4, RenderThreads.One)]
    [InlineData(16, RenderThreads.One)]
    [InlineData(16, RenderThreads.Auto)]
    public void OfflineRenderOfALongSong(int tracks, RenderThreads threads)
    {
        if (!Enabled) return;
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-perf-" + Guid.NewGuid().ToString("N"))).FullName;
        using var shared = SharedBlock.Create($"tf-perf-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => throw new InvalidOperationException("no plug-ins in this run"));
        try
        {
            LoadGmTracks(tracks);
            const int Seconds = 240;   // a four-minute song
            var events = new List<RenderEvent>();
            for (var slot = 0; slot < tracks; slot++)
            {
                var ch = (byte)(slot % 16);
                for (long f = 0; f < Seconds * 48000L; f += 6000)   // eighth notes at 120 bpm
                {
                    var note = (byte)(40 + (int)(f / 6000 + slot * 5) % 40);
                    events.Add(new RenderEvent { Frame = f, Slot = slot, Status = (byte)(0x90 | ch), Data1 = note, Data2 = 96 });
                    events.Add(new RenderEvent { Frame = f + 5000, Slot = slot, Status = (byte)(0x80 | ch), Data1 = note, Data2 = 0 });
                }
            }
            var eventFile = Path.Combine(dir, "events.bin");
            RenderEventFile.Write(eventFile, events.OrderBy(e => e.Frame).ToArray());
            var spec = new RenderSpec
            {
                StartFrame = 0, EndFrame = Seconds * 48000L, TailMode = RenderTailMode.None, MasterPath = Path.Combine(dir, "master.wav"),
                EventFile = eventFile, Threads = threads,
            };
            for (var slot = 0; slot < tracks; slot++) spec.Slots.Add(new RenderSlot { Slot = slot, InMaster = true });
            var gc2 = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            var renderer = new OfflineRenderer(spec, EH.Mix!, shared, EH.SampleRate, 256, EH.Threads, () => false, _ => { });
            renderer.Prepare();
            try { renderer.Run(); } finally { renderer.Restore(); }
            sw.Stop();
            var size = new FileInfo(spec.MasterPath).Length;
            _output.WriteLine($"offline render: {tracks} GM tracks, {Seconds} s song, {events.Count} events, threads {threads}: {sw.ElapsedMilliseconds} ms " +
                $"({Seconds * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):F1}x realtime); master {size / 1024} KB; gen2 collections {GC.CollectionCount(2) - gc2}");
            Assert.True(size > Seconds * 48000L * 4);
        }
        finally
        {
            EH.Detach();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
