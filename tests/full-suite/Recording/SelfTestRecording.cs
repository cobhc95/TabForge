using System.IO;
using EA = TabForge.AudioEngine.Audio;
using EMix = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>Recording / monitoring pipeline: input conversion, shared monitoring input, unique take files, queued disk writes.</summary>
public static partial class SelfTest
{
    private static void TestRecordingPipeline()
    {
        // Large callbacks: every fed frame is delivered (the 8192-frame conversion buffer is flushed and conversion continues).
        var capture = new EA.InputCapture("test", 48000, 2, 5);
        long delivered = 0;
        capture.BlockCaptured = (_, frames) => delivered += frames;
        var big = new float[20000 * 2];
        for (var i = 0; i < 20000; i++) { big[i * 2] = i % 100 / 100f; big[i * 2 + 1] = -big[i * 2]; }
        capture.Feed(big);
        Check("a 20000-frame input callback delivers all 20000 frames", delivered == 20000 && capture.TotalFrames == 20000, $"{delivered} / {capture.TotalFrames}");

        // Upsampling 24 kHz -> 48 kHz: twice the frames, continuous across the chunk boundary (no duplicated / skipped phase).
        var up = new EA.InputCapture("test", 48000, 1, 5, sourceRate: 24000);
        var samples = new List<float>();
        up.BlockCaptured = (block, frames) => { for (var i = 0; i < frames; i++) samples.Add(block[i * 2]); };
        var ramp = new float[10000];
        for (var i = 0; i < ramp.Length; i++) ramp[i] = i;
        up.Feed(ramp);
        // Windowed-sinc (RT-09): half the kernel (Taps / 2 input frames) stays buffered as look-ahead, and the first outputs see the
        // silent history before the ramp; after that a ramp comes out as an exact ramp (unity DC gain, symmetric kernel).
        var lookAhead = EA.SincResampler.Taps / 2;
        var steps = samples.Zip(samples.Skip(1), (a, b) => b - a).Skip(EA.SincResampler.Taps * 2).ToList();
        Check("upsampling 24 -> 48 kHz produces 2x frames (less the resampler's look-ahead), continuous across chunk boundaries",
            Math.Abs(samples.Count - (10000 - lookAhead) * 2) <= 2 && steps.Count > 19000 && steps.All(d => Math.Abs(d - 0.5f) < 5e-3f),
            $"{samples.Count} frames, step range {steps.DefaultIfEmpty().Min()}..{steps.DefaultIfEmpty().Max()}");

        // Two armed chains in one block hear the same input (it is read once per mixer block and shared).
        using (var shared = TabForge.Audio.Contracts.SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}"))
        {
            var block = new EA.InputBlock(64) { Frames = 64 };
            for (var i = 0; i < 64; i++) { block.L[i] = (i + 1) / 100f; block.R[i] = -(i + 1) / 100f; }
            var outs = new List<float[]>();
            foreach (var (slot, monitor) in new[] { (0, false), (1, true), (2, true) })
            {
                var chain = new EMix.TrackChain(slot, -1, null, Array.Empty<EMix.TrackChain.Effect>(), 64) { ArmMode = 0, Monitor = monitor };
                var l = new float[64]; var r = new float[64];
                var transport = new EP.TransportInfo { Tempo = 120 };
                chain.Render(l, r, 0, 64, transport, shared, block);
                outs.Add(l);
            }
            Check("an unmonitored armed track does not consume the input; two monitored tracks get identical samples",
                outs[0].All(v => v == 0) && outs[1].Zip(outs[2]).All(p => p.First == p.Second) && outs[1].Any(v => v != 0));
        }

        // Take files: identical / identically-sanitised names in the same second never share a file; queued writes keep full length.
        var folder = Path.Combine(Path.GetTempPath(), $"tf-rec-{Guid.NewGuid():N}");
        try
        {
            var r1 = new EA.Recorder(folder, new[] { (0, "Guitar", 0), (1, "Guitar", 0), (2, "Gui?tar", 2) }, 0, 8000);
            var r2 = new EA.Recorder(folder, new[] { (0, "Guitar", 0) }, 0, 8000);
            var chunk = new float[1000 * 2];
            for (var i = 0; i < 50; i++) r1.Enqueue(chunk, 1000);   // 50 000 frames (> the 4 s queue at 8 kHz; the disk thread drains it)
            var takes = r1.Finish();
            r2.Finish();
            var paths = takes.Select(t => t.Take.Path).Concat(Directory.GetFiles(folder)).Distinct().ToList();
            Check("four simultaneous takes named 'Guitar' get four distinct files", Directory.GetFiles(folder).Length == 4, string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
            var lengthOk = takes.All(t => Math.Abs(t.LengthSec - 50000 / 8000.0) < 1e-9);
            using var reader = new NAudio.Wave.WaveFileReader(takes[0].Take.Path);
            Check("queued recording writes every frame (take length = input length, silence stands in for any overflow)",
                lengthOk && reader.SampleCount == 50000, $"{takes[0].LengthSec} s, {reader.SampleCount} samples, dropped {r1.DroppedFrames}");

            // Stop does not wait for the disk (the engine main thread must keep answering pings): the disk thread writes the backlog,
            // closes the files and reports the takes once; a later Finish (shutdown) returns the same takes.
            var r3 = new EA.Recorder(folder, new[] { (0, "Bg", 0) }, 0, 8000);
            for (var i = 0; i < 50; i++) r3.Enqueue(chunk, 1000);
            var calls = 0;
            List<(EA.Recorder.Take Take, double LengthSec)>? bgTakes = null;
            using var doneSignal = new ManualResetEventSlim();
            r3.FinishInBackground(t => { Interlocked.Increment(ref calls); bgTakes = t; doneSignal.Set(); });
            var signalled = doneSignal.Wait(TimeSpan.FromSeconds(10));
            var again = r3.Finish();
            long bgSamples = -1;
            if (bgTakes is { Count: 1 }) { using var bgReader = new NAudio.Wave.WaveFileReader(bgTakes[0].Take.Path); bgSamples = bgReader.SampleCount; }
            Check("stopping a take hands the backlog to the disk thread: it reports the take once, full length, files closed",
                signalled && calls == 1 && r3.Finished && bgSamples == 50000 && ReferenceEquals(again, bgTakes),
                $"signalled {signalled}, calls {calls}, samples {bgSamples}");
        }
        finally { try { Directory.Delete(folder, true); } catch { } }

        // A drop becomes silence at the position where the input was lost, after the older queued audio (stalled writer).
        var gapFolder = Path.Combine(Path.GetTempPath(), $"tf-rec-gap-{Guid.NewGuid():N}");
        try
        {
            static float[] ReadTake(string path)
            {
                using var wav = new NAudio.Wave.WaveFileReader(path);
                var samples = new float[(int)wav.SampleCount];
                var got = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(wav).Read(samples, 0, samples.Length);
                return got == samples.Length ? samples : samples[..got];
            }
            static float[] Block(int frames, params (int At, float Value)[] impulses)
            {
                var b = new float[frames * 2];
                foreach (var (at, v) in impulses) { b[at * 2] = v; b[at * 2 + 1] = v; }
                return b;
            }
            static List<int> Impulses(float[] s, float value) { var l = new List<int>(); for (var i = 0; i < s.Length; i++) if (s[i] == value) l.Add(i); return l; }

            // 8 kHz: the queue holds 32768 frames. Fill 32000 of them (an impulse at 100), then two adjacent drops (1000 + 900 frames,
            // their impulses are lost; 768 frames are free, so both are too big), then 500 frames that still fit (impulse at 20): they were queued after the drop.
            var stalled = new EA.Recorder(gapFolder, new[] { (0, "Gap", 0) }, 0, 8000, startWriter: false, queueSeconds: 4);
            stalled.Enqueue(Block(1000, (100, 0.5f)), 1000);
            for (var i = 0; i < 31; i++) stalled.Enqueue(Block(1000), 1000);
            stalled.Enqueue(Block(1000, (10, 0.7f)), 1000);
            stalled.Enqueue(Block(900, (0, 0.9f)), 900);
            stalled.Enqueue(Block(500, (20, 0.8f)), 500);
            var stalledTake = stalled.Finish();
            var s1 = ReadTake(stalledTake[0].Take.Path);
            var at500 = Impulses(s1, 0.5f); var at800 = Impulses(s1, 0.8f);
            var silent = s1.Skip(32000).Take(1900).All(v => v == 0);
            Check("a dropped block becomes silence at its own position, after the older queued audio and before the newer (impulses at exact frames, length intact)",
                s1.Length == 34400 && Math.Abs(stalledTake[0].LengthSec - 34400 / 8000.0) < 1e-9 && at500.SequenceEqual(new[] { 100 }) && at800.SequenceEqual(new[] { 33920 })
                && silent && Impulses(s1, 0.7f).Count == 0 && Impulses(s1, 0.9f).Count == 0 && stalled.DroppedFrames == 1900 && stalled.GapOverflows == 0,
                $"len {s1.Length}, 0.5 at [{string.Join(",", at500)}], 0.8 at [{string.Join(",", at800)}], silent {silent}, dropped {stalled.DroppedFrames}, overflows {stalled.GapOverflows}");

            // One marker only: a second, separated drop cannot get its own marker; it is merged into the first (reported), and the take keeps its full length.
            var tiny = new EA.Recorder(gapFolder, new[] { (0, "Gap2", 0) }, 0, 8000, startWriter: false, gapCapacity: 1, queueSeconds: 4);
            tiny.Enqueue(Block(1000, (100, 0.5f)), 1000);
            for (var i = 0; i < 31; i++) tiny.Enqueue(Block(1000), 1000);
            tiny.Enqueue(Block(1000), 1000);                          // dropped: marker 1
            tiny.Enqueue(Block(500, (20, 0.8f)), 500);                // fits
            tiny.Enqueue(Block(1000), 1000);                          // dropped, queued audio in between: needs a second marker
            var tinyTake = tiny.Finish();
            var s2 = ReadTake(tinyTake[0].Take.Path);
            Check("a full gap-marker ring merges the drop into the newest marker, reports it, and the take keeps its full length",
                s2.Length == 34500 && tiny.GapOverflows == 1 && Impulses(s2, 0.5f).SequenceEqual(new[] { 100 }) && Impulses(s2, 0.8f).Count == 1,
                $"len {s2.Length}, overflows {tiny.GapOverflows}");

            // A7 stress 1: a long queue absorbs a long writer stall. 8 kHz, 60 s queue (65.5 s of capacity); the disk thread is stalled (nothing pumps)
            // while 30 s are captured: nothing is lost, in order, with no gap.
            var longQueue = new EA.Recorder(gapFolder, new[] { (0, "Long", 0) }, 0, 8000, startWriter: false, queueSeconds: 60);
            for (var i = 0; i < 240; i++) longQueue.Enqueue(Block(1000, (i, 0.25f + i * 0.001f)), 1000);   // 30 s; impulse i at frame i of block i
            var longTake = longQueue.Finish();
            var s3 = ReadTake(longTake[0].Take.Path);
            var inOrder = true;
            for (var i = 0; i < 240 && inOrder; i++) inOrder = s3.Length == 240000 && s3[i * 1000 + i] == 0.25f + i * 0.001f;
            Check("a 30 s writer stall with a 60 s queue loses nothing: every block in order, no gap, no drop",
                inOrder && longQueue.DroppedFrames == 0 && longQueue.GapCount == 0 && longQueue.LostSeconds == 0 && longQueue.LossSummary is null,
                $"len {s3.Length}, dropped {longQueue.DroppedFrames}, gaps {longQueue.GapCount}");

            // A stall longer than the queue: the overflow is still marked as silence in place and the totals are exact.
            var over = new EA.Recorder(gapFolder, new[] { (0, "Over", 0) }, 0, 8000, startWriter: false, queueSeconds: 60);
            for (var i = 0; i < 640; i++) over.Enqueue(Block(1000, (0, i < 524 ? 0.5f : 0.9f)), 1000);   // 80 s into a 65.5 s queue
            var overTake = over.Finish();
            var s4 = ReadTake(overTake[0].Take.Path);
            var keptHead = s4.Take(524000).Where(v => v == 0.5f).Count() == 524;
            var lostSilent = s4.Skip(524000).All(v => v == 0f);
            Check("a stall longer than the queue marks the gap in place and reports the exact total (1 gap, 14.5 s, take length intact)",
                s4.Length == 640000 && keptHead && lostSilent && over.DroppedFrames == 116000 && over.GapCount == 1 && Math.Abs(over.LostSeconds - 14.5) < 1e-9
                && over.LossSummary == "Recording lost 1 gap, 14.5 s in total (disk too slow)",
                $"len {s4.Length}, dropped {over.DroppedFrames}, gaps {over.GapCount}, lost {over.LostSeconds} s, \"{over.LossSummary}\"");

            // Queue size: 120 s asked for, rounded up to a power of two of frames; a memory cap shrinks it (never under 4 s).
            using (var sized = new EA.Recorder(gapFolder, new[] { (0, "Size", 0) }, 0, 48000, startWriter: false))
                Check("the default queue holds at least 120 s at 48 kHz (67 MB shared by all armed tracks)", sized.QueueCapacityFrames >= 120L * 48000 && sized.QueueCapacityFrames * 8 <= 256L * 1024 * 1024, $"{sized.QueueCapacityFrames} frames");
            using (var capped = new EA.Recorder(gapFolder, new[] { (0, "Cap", 0) }, 0, 8000, startWriter: false, queueSeconds: 120, maxQueueBytes: 1024 * 1024))
                Check("a memory cap shrinks the queue (fallback, not a failure)", capped.QueueCapacityFrames == 131072, $"{capped.QueueCapacityFrames} frames");

            // Audio thread: Enqueue (the capture callback) allocates nothing, with room and when dropping.
            using (var rt = new EA.Recorder(gapFolder, new[] { (0, "Rt", 0) }, 0, 8000, startWriter: false, queueSeconds: 4))
            {
                var blk = Block(1000);
                for (var i = 0; i < 40; i++) rt.Enqueue(blk, 1000);   // warm up, and the queue (32768 frames) is full: the rest are drops
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) rt.Enqueue(blk, 1000);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check("recorder: the capture-thread Enqueue path allocates nothing, also while dropping", allocated == 0 && rt.DroppedFrames > 0, $"{allocated} bytes, dropped {rt.DroppedFrames}");
            }
        }
        finally { try { Directory.Delete(gapFolder, true); } catch { } }
    }

    private static void TestRoutingCycles()
    {
        TabForge.Models.TrackModel Track(string name) => new() { Name = name, Rig = new TabForge.Plugins.RigPreset { Plugins = { new TabForge.Plugins.PluginSlot() } } };
        var a = Track("A"); var b = Track("B");
        var aId = a.Id.ToString("N"); var bId = b.Id.ToString("N");
        // A's plug-in already takes its sidechain from B (edge B -> A). Forwarding A's MIDI to B (A -> B) must be refused:
        // only the edge being edited (A's MIDI forward) may be left out of the check, not A's sidechain.
        a.Rig.Plugins[0].SidechainTrackId = bId;
        Check("MIDI forward A->B is refused while A's own sidechain comes from B", TabForge.Models.RoutingLinks.WouldCycle(new[] { a, b }, aId, bId));
        // A loop saved in a file (hand-edited or older build) is broken when the project is validated.
        a.Rig.Plugins[0].MidiOutTrackId = bId;
        var tracks = new List<TabForge.Models.TrackModel> { a, b };
        var cleared = TabForge.Models.RoutingLinks.BreakCycles(tracks);
        var edges = TabForge.Models.RoutingLinks.Edges(tracks);
        Check("a looping link loaded from disk is cleared (one link removed, the rest kept)", cleared.Count == 1 && edges.Count == 1, $"{string.Join(",", cleared)} / {string.Join(",", edges)}");
    }

    private static void TestReaperChainImport()
    {
        // A synthetic .RfxChain: header (id, magic, pins, state length, kind, 4 bytes), state, footer (two names, 4 bytes), each line its own base64.
        static string Lines(uint id, uint magic, int ins, int outs, int kind, byte[] state, string preset, int? claimLength = null)
        {
            var d = new List<byte>();
            d.AddRange(BitConverter.GetBytes(id)); d.AddRange(BitConverter.GetBytes(magic));
            d.AddRange(BitConverter.GetBytes(ins)); for (var i = 0; i < ins; i++) d.AddRange(BitConverter.GetBytes(1L << i));
            d.AddRange(BitConverter.GetBytes(outs)); for (var i = 0; i < outs; i++) d.AddRange(BitConverter.GetBytes(1L << i));
            d.AddRange(BitConverter.GetBytes(claimLength ?? state.Length)); d.AddRange(BitConverter.GetBytes(kind)); d.AddRange(BitConverter.GetBytes(0x100000));
            var header = d.Count;
            d.AddRange(state); d.Add(0); d.AddRange(System.Text.Encoding.UTF8.GetBytes(preset)); d.Add(0); d.AddRange(BitConverter.GetBytes(0x10));
            var all = d.ToArray(); var sb = new System.Text.StringBuilder();
            // The header on its own lines of 96 bytes (a long one wraps), then the rest.
            for (var o = 0; o < all.Length;) { var n = Math.Min(96, (o < header ? header : all.Length) - o); sb.Append("  ").Append(Convert.ToBase64String(all, o, n)).Append('\n'); o += n; }
            return sb.ToString();
        }
        var chunk = Enumerable.Range(0, 200).Select(i => (byte)(i * 3 + 1)).ToArray();
        var list = new byte[] { 0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE }.Concat(BitConverter.GetBytes(0.25f)).Concat(BitConverter.GetBytes(1f)).ToArray();
        byte[] comp = { 9, 8, 7, 6, 5 }, ctrl = { 1, 2, 3 };
        var vst3 = BitConverter.GetBytes(comp.Length).Concat(BitConverter.GetBytes(1)).Concat(comp).Concat(BitConverter.GetBytes(ctrl.Length)).Concat(BitConverter.GetBytes(0)).Concat(ctrl).ToArray();
        var text = "BYPASS 0 0\n<VST \"VST: Test EQ (Acme)\" TEST.DLL 0 \"\" 1<00> \"\"\n" + Lines(0x74736574, 0xFEED5EEE, 2, 2, 1, chunk, "Bright") + ">\nPRESETNAME Bright\nWAK 0 0\n"
            + "BYPASS 1 0\n<VST \"VST: Comp (Acme)\" \"Test Comp.dll\" 0 \"\" 2<00> \"\"\n" + Lines(2, 0xFEED5EEF, 4, 2, 0, list, "") + ">\nWET 0.25 0\nWAK 0 0\n"
            + "BYPASS 0 0\n<CONTAINER Container \"\"\n  BYPASS 0 0 0\n  <VST \"VST3i: Synth (Acme)\" Synth.vst3 0 \"\" 3{00} \"\"\n" + Lines(3, 0xFEED5EEE, 2, 14, 1, vst3, "Pad") + "  >\n  WAK 0 0\n>\nWET 0.5 0\n"
            + "<JS midi/foo \"\"\n  0 1\n>\nWET 0.1 0\n"
            + "<VST \"VST: Bad64 (Acme)\" bad.dll 0 \"\" 4<00> \"\"\n  !!!not base64!!!\n>\n"
            + "<VST \"VST: Short (Acme)\" short.dll 0 \"\" 5<00> \"\"\n" + Lines(5, 0xFEED5EEE, 2, 2, 1, chunk, "", claimLength: 100000) + ">\n"
            + "<VST \"VST: Pins (Acme)\" pins.dll 0 \"\" 6<00> \"\"\n  " + Convert.ToBase64String(BitConverter.GetBytes(6).Concat(BitConverter.GetBytes(0xFEED5EEE)).Concat(BitConverter.GetBytes(int.MaxValue)).Concat(new byte[32]).ToArray()) + "\n>\n"
            + "<VST \"VST: Elsewhere (Acme)\" elsewhere.dll 0 \"\" 7<00> \"\"\n>\n";
        var p = TabForge.Plugins.ReaperChainImporter.Parse(text);
        var e = p.Entries;
        Check("chain exchange file: every VST block is an entry, the script effect is counted", e.Count == 7 && p.Notes.Any(n => n.Contains("1 script effect")), $"{e.Count} / {string.Join(" | ", p.Notes)}");
        if (e.Count != 7) return;
        var block = TabForge.Plugins.ReaperChainImporter.DecodeBlock(Lines(0x74736574, 0xFEED5EEE, 2, 14, 1, chunk, "Bright").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList(), out var blockProblem);
        Check("chain exchange file: header fields (a header longer than one line), state length and preset name",
            block is { Id: 0x74736574, Inputs: 2, Outputs: 14, IsChunk: true, PresetName: "Bright" } && block.State.SequenceEqual(chunk), blockProblem ?? "");
        Check("chain exchange file: VST2 chunk is the state, byte for byte", e[0] is { Name: "Test EQ", Vendor: "Acme", Format: "VST2", Enabled: true, Wet: 100 } && e[0].State!.SequenceEqual(chunk));
        Check("chain exchange file: VST2 parameter list is kept whole; bypass and mix are read", e[1] is { Enabled: false, Wet: 25 } && e[1].State!.SequenceEqual(list), $"{e[1].Enabled} {e[1].Wet} {e[1].StateProblem}");
        Check("chain exchange file: VST3 state becomes length + component + length + controller; a container's mix is not the plug-in's",
            e[2] is { Format: "VST3", IsInstrument: true, Wet: 100 } && e[2].State!.SequenceEqual(BitConverter.GetBytes(comp.Length).Concat(comp).Concat(BitConverter.GetBytes(ctrl.Length)).Concat(ctrl)), e[2].StateProblem ?? "");
        Check("chain exchange file: bad base64, a length past the data and an absurd pin count drop the state with a reason, never throw",
            e.Skip(3).Take(3).All(x => x.State is null && x.StateProblem is { Length: > 0 }), string.Join(" | ", e.Skip(3).Take(3).Select(x => x.StateProblem ?? "none")));
        var catalog = new[] { "test.dll", "TEST COMP.DLL", "synth.vst3", "Bad.dll", "short-standalone.dll", "pins.dll" }
            .Select(f => new TabForge.Plugins.VstPluginInfo(Path.GetFileNameWithoutExtension(f), @"C:\Plugins\" + f, f.EndsWith("3") ? "VST3" : "VST2")).ToList();
        var built = TabForge.Plugins.ReaperChainImporter.Build(p, catalog);
        var report = built.Report.Split("\n\n");
        Check("chain exchange file: plug-ins are found by file name in any case; the unknown one is counted",
            built.Chain.Count == 6 && built.Missing == 1 && built.Chain[0].Path == @"C:\Plugins\test.dll" && built.Chain[0].State == Convert.ToBase64String(chunk) && built.Chain[1].Wet == 25);
        Check("chain exchange file: a plug-in's preset name stays on its slot, and a slot without one writes none",
            built.Chain[0].PresetName == "Bright" && built.Chain[1].PresetName is null && !System.Text.Json.JsonSerializer.Serialize(built.Chain[1]).Contains("PresetName", StringComparison.Ordinal), built.Chain[0].PresetName ?? "");
        Check("chain exchange file: one grouped summary (count, one line for unknown plug-ins, one for dropped settings)",
            report[0] == "Imported 6 of 7 plug-ins." && report.Count(l => l.StartsWith("Not in your plug-in list: Elsewhere.")) == 1 && report.Count(l => l.StartsWith("Loaded with default settings: Bad64 (")) == 1
            && !built.Report.Contains(".dll"), built.Report);
        // The file name matches but the maker does not: the plug-in is kept, another plug-in's settings are not applied.
        var other = TabForge.Plugins.ReaperChainImporter.Build(new(e.Take(2).ToList(), new()), new[] { new TabForge.Plugins.VstPluginInfo("test", @"C:\Plugins\test.dll", "VST2", "Other Maker"), new TabForge.Plugins.VstPluginInfo("Test Comp", @"C:\Plugins\Test Comp.dll", "VST2", "ACME Audio Ltd.") });
        Check("chain exchange file: a different maker's plug-in under the same file name loads with default settings, in one summary line",
            other.Chain.Count == 2 && other.Chain[0].State is null && other.Chain[1].State is not null && other.Report.Split("\n\n").Count(l => l.Contains("belong to a different plug-in")) == 1, other.Report);
        Check("chain exchange file: a text with no plug-in block is no chain file; one with only skipped effects is",
            TabForge.Plugins.ReaperChainImporter.Recognised(TabForge.Plugins.ReaperChainImporter.Parse("hello\nBYPASS 0 0\nWET 0.5 0\n")) is null
            && TabForge.Plugins.ReaperChainImporter.Recognised(TabForge.Plugins.ReaperChainImporter.Parse("<JS midi/foo \"\"\n>\n")) is not null);
        Check("chain exchange file: a chain that comes in whole says nothing",
            TabForge.Plugins.ReaperChainImporter.Build(new(e.Take(2).ToList(), new()), catalog) is { Report: "", Missing: 0, Chain.Count: 2 });
    }

    private static void TestSettingsWithInlinePluginStates()
    {
        // Regression: an auto-load chain saved with a 47 KB plug-in state made settings.json fail its own validation
        // ("overlong text"). Such a file must load, the state must move to a ChainStates file, and applying the chain restores it.
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-chainstates-{Guid.NewGuid():N}");
        TabForge.Plugins.ChainStateStore.FolderOverride = scratch;
        try
        {
            var state = Convert.ToBase64String(Enumerable.Range(0, 36_000).Select(i => (byte)(i * 7)).ToArray());
            var settings = new TabForge.Services.AppSettings();
            settings.Plugins.AutoChains.Add(new TabForge.Plugins.AutoChain { Key = "drums", Plugins = { new TabForge.Plugins.PluginSlot { Name = "Kit", Path = @"C:\x\kit.dll", State = state } } });
            var file = Path.Combine(scratch, "settings.json");
            Directory.CreateDirectory(scratch);
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(settings));
            var loaded = TabForge.Services.SettingsFileService.Load(file);
            var stored = loaded.Plugins.AutoChains[0].Plugins[0].State;
            Check("settings with a large inline plug-in state load, and the state moves to a ChainStates file",
                TabForge.Plugins.ChainStateStore.IsReference(stored) && Directory.GetFiles(scratch, "*.state").Length == 1, stored?.Length.ToString() ?? "null");
            var track = new TabForge.Models.TrackModel { Kind = TabForge.Models.TrackKind.Drums, MidiChannel = 9 };
            TabForge.Plugins.AutoChains.Apply(loaded.Plugins, track);
            Check("applying the auto-load chain restores the full plug-in state", track.Rig.Plugins.Count == 1 && track.Rig.Plugins[0].State == state);
        }
        finally
        {
            TabForge.Plugins.ChainStateStore.FolderOverride = null;
            try { Directory.Delete(scratch, true); } catch { }
        }
    }

    private static void TestCallbackMetrics()
    {
        // 100 callbacks of 128 frames at 48 kHz (2.67 ms deadline): 98 take 1 ms, one 2.5 ms, one 5 ms (a deadline miss).
        var m = new EMix.CallbackMetrics();
        var f = System.Diagnostics.Stopwatch.Frequency;
        long t = 1000;
        long Ms(double ms) => (long)(ms * f / 1000.0);
        for (var i = 0; i < 100; i++)
        {
            var d = i == 50 ? 2.5 : i == 99 ? 5.0 : 1.0;
            m.Record(t, t + Ms(d), 128, 48000, 0);
            t += Ms(2.667);
        }
        var s = m.TakeAndReset();
        Check("audio callback metrics: count, p95, max and deadline misses",
            s.Calls == 100 && Math.Abs(s.P95Ms - 1.1) < 0.11 && Math.Abs(s.MaxMs - 5.0) < 0.05 && s.DeadlineMisses == 1 && s.LateCalls == 0,
            $"{s.Calls} calls, p95 {s.P95Ms}, p99 {s.P99Ms}, max {s.MaxMs:0.00}, misses {s.DeadlineMisses}, late {s.LateCalls}");
        Check("audio callback metrics reset after reading", m.TakeAndReset().Calls == 0);
    }
}
