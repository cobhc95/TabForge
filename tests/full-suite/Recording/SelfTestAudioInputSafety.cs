using System.Diagnostics;
using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using EA = TabForge.AudioEngine.Audio;
using EMix = TabForge.AudioEngine.Mixing;
using EO = TabForge.AudioEngine.Output;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// The input path's safety nets (two-channel switch, faults that stop the input and nothing else, bounds) and its cost per
/// callback on a fake feed: no allocation, and the two-channel case inside a fixed budget. The measured table goes to
/// tf-inputs-bench.txt in the temp folder.
/// </summary>
public static partial class SelfTest
{
    private static void TestAudioInputSafety()
    {
        // The two-channel switch: in the config (absent from an older sender = on), at the ASIO open and in the reported list.
        var config = new EngineConfig("WASAPI", "out", 48000, 256, false, "in");
        static EngineConfig Trip(EngineConfig c, int cut = 0)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) w.Write(c);
            using var r = new BinaryReader(new MemoryStream(ms.ToArray(), 0, (int)ms.Length - cut));
            return r.ReadConfig();
        }
        Check("the all-inputs switch travels in the config; a config without it (an older sender) means on",
            Trip(config).AllInputs && !Trip(config with { AllInputs = false }).AllInputs && Trip(config with { AllInputs = false }, cut: 2).AllInputs && Trip(config with { AllInputs = false }, cut: 2) == config);
        var two = config with { AllInputs = false };
        Check("all inputs off: two ASIO input channels at most and a two-input list, as before",
            EO.AudioOutputFactory.AsioInputCount(two with { AsioInput = 0, AsioInputLast = 1 }, 8) == 2 && EO.AudioOutputFactory.AsioInputCount(two with { AsioInput = 2, AsioInputLast = 63 }, 8) == 2
            && EO.AudioOutputFactory.AsioInputCount(two with { AsioInput = 3, AsioInputLast = 3 }, 8) == 1 && EO.AudioOutputFactory.AsioInputCount(two with { AsioInput = 7, AsioInputLast = 63 }, 8) == 1
            && EO.AudioOutputFactory.RecordableInputs(two, _ => 8).Length == 2);
        Check("the settings default to all inputs", new TabForge.Services.PluginSettings().UseAllInputs);

        // A fault in the capture callback stops the input, is reported once and never reaches the caller (the driver's thread).
        var faulty = new EA.InputCapture("fake 8", 48000, 8, 5);
        var calls = 0;
        faulty.BlockCaptured = (_, _) => { calls++; throw new InvalidOperationException("boom"); };
        var feed = new float[256 * 8];
        Array.Fill(feed, 0.5f);
        var escaped = false;
        try { faulty.Feed(feed); faulty.Feed(feed); } catch (Exception) { escaped = true; }
        var first = faulty.TakeFault(); var second = faulty.TakeFault();
        Check("a fault in the input callback stops the input and is reported once; nothing escapes", !escaped && calls == 1 && first == "boom" && second is null, $"escaped {escaped}, calls {calls}, {first} / {second}");
        Check("a healthy input reports no fault", new EA.InputCapture("ok", 48000, 2, 5).TakeFault() is null);

        // Bounds: a block that claims more than it holds, a read longer than the block, a recorder block narrower than its capture.
        using (var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}"))
        {
            var lying = new EA.InputBlock(64, 8) { Frames = 64, Channels = 99 };
            for (var c = 0; c < 8; c++) Array.Fill(lying.Ch[c], 1f);
            var chain = new EMix.TrackChain(0, -1, null, Array.Empty<EMix.TrackChain.Effect>(), 64) { ArmMode = AudioInputAddress.Encode(20, true), Monitor = true };
            var l = new float[64]; var r = new float[64];
            var threw = false;
            try { chain.Render(l, r, 0, 64, new EP.TransportInfo { Tempo = 120 }, shared, lying); } catch (Exception) { threw = true; }
            Check("a channel beyond the block's buffers is silence whatever the block claims", !threw && l.All(v => v == 0) && r.All(v => v == 0));
        }
        var capture = new EA.InputCapture("fake 8", 48000, 8, 5);
        capture.Feed(new float[4096 * 8]);
        var small = new EA.InputBlock(64, 4);
        var overran = false;
        try { capture.Read(small, 256); } catch (Exception) { overran = true; }
        Check("a read longer or wider than the block is clipped to it", !overran && small.Frames == 64 && small.Channels == 4);
        var folder = Path.Combine(Path.GetTempPath(), $"tf-inputs-{Guid.NewGuid():N}");
        try
        {
            var recorder = new EA.Recorder(folder, new[] { (0, "Eight", AudioInputs.WireMode("Input 8")) }, 0, 8000, startWriter: false, queueSeconds: 4, inputChannels: 8);
            var narrow = false;
            try { recorder.Enqueue(new float[100 * 2], 100); recorder.Enqueue(new float[100 * 8], 100); } catch (Exception) { narrow = true; }
            var take = recorder.Finish().Single();
            Check("a block narrower than the recorder's capture is not read (no overrun); a full one is recorded", !narrow && Math.Abs(take.LengthSec - 100 / 8000.0) < 1e-9, $"{take.LengthSec}");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }

        // Cost per callback (48 kHz, fake feed): capture conversion + ring + recorder queue ("feed"), the mixer's read, the armed chains' input mix.
        // Warm-up first (every path, then a pause for the runtime's optimised code), so the table is steady-state time, not first-call time.
        for (var pass = 0; pass < 2; pass++)
        {
            InputCost(8, 64, 8, true, 0, warm: 4000); InputCost(2, 64, 1, false, 0, warm: 4000); InputCost(1, 64, 1, false, 0, warm: 4000); InputCost(8, 64, 1, false, 44100, warm: 1500);
            LegacyTwoChannelCost(64);
            Thread.Sleep(250);
        }
        var table = new List<string> { "channels  frames  scenario                 feed+read us  chains us  % of period  bytes (least of 5 rounds)" };
        long allocated = 0; double twoChannel = 0;
        for (var pass = 0; pass < 2; pass++)   // the first pass finishes the warm-up for every size; the second is the table
        {
        if (pass == 1) { table.RemoveRange(1, table.Count - 1); allocated = 0; }
        foreach (var channels in new[] { 2, 8, 16, 64 })
            foreach (var frames in new[] { 64, 256, 1024 })
                foreach (var (name, armed, record) in new[] { ("1 mono armed", 1, false), ("8 armed, recording", 8, true) })
                {
                    var m = InputCost(channels, frames, armed, record, 0);
                    table.Add($"{channels,8}  {frames,6}  {name,-23}  {m.FeedUs,12:0.00}  {m.ChainUs,9:0.00}  {(m.FeedUs + m.ChainUs) / (frames / 48000.0 * 1e6) * 100,11:0.000}  {m.Bytes,14}");
                    allocated += m.Bytes;
                    if (channels == 2 && frames == 256 && armed == 1) twoChannel = m.FeedUs;
                }
        foreach (var channels in new[] { 8, 16 })
        {
            var m = InputCost(channels, 256, 1, false, 44100);
            table.Add($"{channels,8}  {256,6}  {"1 mono, 44.1 -> 48 kHz",-23}  {m.FeedUs,12:0.00}  {m.ChainUs,9:0.00}  {(m.FeedUs + m.ChainUs) / (256 / 48000.0 * 1e6) * 100,11:0.000}  {m.Bytes,14}");
            allocated += m.Bytes;
        }
        }
        var reference = LegacyTwoChannelCost(256);
        table.Add($"reference: the two-channel loops as they were before every channel was kept (a copy in this test, not the old binary), 256 frames: {reference:0.00} us");
        try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "tf-inputs-bench.txt"), table); } catch (IOException) { }
        Check("the input path allocates nothing per callback (2 to 64 channels, monitoring and recording, resampled)", allocated == 0, $"{allocated} bytes");
        const double BudgetUs = 20;   // feed + read of a two-channel 256-frame block: a few microseconds on the build machine
        Check("the two-channel input costs at most 1.5x its budget per 256-frame callback", twoChannel <= BudgetUs * 1.5, $"{twoChannel:0.00} us (budget {BudgetUs} us)");
    }

    /// <summary>The best of five timed batches: microseconds per callback for the capture side and for the armed chains, and bytes allocated per callback.</summary>
    private static (double FeedUs, double ChainUs, long Bytes) InputCost(int channels, int frames, int armed, bool record, int sourceRate, int warm = 0)
    {
        var capture = new EA.InputCapture("bench", 48000, channels, 5, sourceRate);
        var inFrames = sourceRate > 0 ? frames * sourceRate / 48000 : frames;
        var fed = new float[inFrames * channels];
        for (var i = 0; i < fed.Length; i++) fed[i] = (i % 7 - 3) * 0.01f;
        var block = new EA.InputBlock(1024, EA.InputCapture.MaxChannels);
        var folder = Path.Combine(Path.GetTempPath(), $"tf-inputs-{Guid.NewGuid():N}");
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EA.Recorder? recorder = null;
        try
        {
            var chains = new EMix.TrackChain[armed];
            for (var i = 0; i < armed; i++)
                chains[i] = new EMix.TrackChain(i, -1, null, Array.Empty<EMix.TrackChain.Effect>(), 1024) { ArmMode = AudioInputAddress.Encode(i % capture.Channels, false), Monitor = true };
            if (record)
            {
                recorder = new EA.Recorder(folder, Enumerable.Range(0, armed).Select(i => (i, $"T{i}", AudioInputAddress.Encode(i % capture.Channels, false))), 0, 48000, startWriter: false, queueSeconds: 4, inputChannels: capture.Channels);
                capture.BlockCaptured = recorder.Enqueue;
            }
            var l = new float[1024]; var r = new float[1024];
            var transport = new EP.TransportInfo { Tempo = 120 };
            var batch = warm > 0 ? warm : Math.Clamp(200000 / frames, 100, 1000);
            double bestFeed = double.MaxValue, bestChain = double.MaxValue; var bytes = long.MaxValue;
            for (var round = 0; round < (warm > 0 ? 1 : 6); round++)   // round 0 is not counted
            {
                long feedTicks = 0, chainTicks = 0;
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < batch; i++)
                {
                    var t0 = Stopwatch.GetTimestamp();
                    capture.Feed(fed);
                    capture.Read(block, frames);
                    var t1 = Stopwatch.GetTimestamp();
                    for (var c = 0; c < chains.Length; c++) chains[c].Render(l, r, 0, frames, transport, shared, block);
                    feedTicks += t1 - t0; chainTicks += Stopwatch.GetTimestamp() - t1;
                }
                if (round == 0) continue;
                bytes = Math.Min(bytes, GC.GetAllocatedBytesForCurrentThread() - before);   // an allocation of the path itself shows in every round; a stray one of the runtime does not
                bestFeed = Math.Min(bestFeed, feedTicks * 1e6 / Stopwatch.Frequency / batch);
                bestChain = Math.Min(bestChain, chainTicks * 1e6 / Stopwatch.Frequency / batch);
            }
            return (bestFeed, bestChain, bytes == long.MaxValue ? 0 : bytes);   // the least any counted round allocated
        }
        finally
        {
            capture.BlockCaptured = null;
            recorder?.Finish();
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    /// <summary>The capture's two-channel convert, ring write and read loops as they were with a stereo-only ring: the yardstick for the two-channel case.</summary>
    private static double LegacyTwoChannelCost(int frames)
    {
        const int Ring = 1 << 15;
        var ring = new float[Ring * 2]; var converted = new float[8192 * 2]; var left = new float[1024]; var right = new float[1024];
        var fed = new float[frames * 2];
        for (var i = 0; i < fed.Length; i++) fed[i] = (i % 7 - 3) * 0.01f;
        long write = 0, read = 0; var best = double.MaxValue;
        for (var round = 0; round < 6; round++)
        {
            var t0 = Stopwatch.GetTimestamp();
            for (var n = 0; n < 100; n++)
            {
                for (var i = 0; i < frames; i++) { converted[i * 2] = fed[i * 2]; converted[i * 2 + 1] = fed[i * 2 + 1]; }
                for (var i = 0; i < frames; i++) { var at = (int)((write + i) & (Ring - 1)) * 2; ring[at] = converted[i * 2]; ring[at + 1] = converted[i * 2 + 1]; }
                write += frames;
                var margin = Math.Max(frames * 2, 240);
                if (write - read > frames + margin * 2) read = write - frames - margin;
                if (write - read < frames) continue;
                for (var i = 0; i < frames; i++) { var at = (int)((read + i) & (Ring - 1)) * 2; left[i] = ring[at]; right[i] = ring[at + 1]; }
                read += frames;
            }
            if (round > 0) best = Math.Min(best, (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / 100);
        }
        return best + left[0] * 0 + right[0] * 0;
    }
}
