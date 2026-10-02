using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using TabForge.Audio.Contracts;
using EH = TabForge.AudioEngine.EngineHost.Headless;
using EM = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// WP-5 real-time polish (audit 2026-09-29 RT-01..RT-08, M-05, D10/RT-10, C6, C11/M-01): the audio-thread paths are allocation-free and
/// lock-free, bad plug-in audio never reaches the mix, and the transport carries the song's meter. Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    private static void TestRealtimePolish()
    {
        TestPinModes();
        TestDenormalsFlushed();
        TestNonFiniteAudio();
        TestTransportMap();
        TestVst2ChannelsAndFailure();
        TestBreadcrumbPathCache();
        TestMidiOverflowCounted();
        TestNativeTimerRefCount();
        TestGmPolyphony();
        TestEngineLogRotation();
        TestHeadlessRealtime();
        TestSafetyLimiter();
        TestAuditClickDetector();
    }

    /// <summary>A7-A03: the audio audit's click detector ignores steep edges that repeat at a steady period (a saw) and still flags an isolated step.</summary>
    private static void TestAuditClickDetector()
    {
        const int rate = 48000;
        var empty = Array.Empty<bool>();
        var saw = new float[rate * 2];
        for (var i = 0; i < saw.Length; i++) saw[i] = (float)(0.3 * (2 * ((i * 55.0 / rate) % 1.0) - 1));   // 55 Hz saw: a 0.6 reset every 18.2 ms
        var sawFlags = TabForge.Diagnostics.AudioAudit.FindClicks(saw, rate, empty).Count;
        var sine = new float[rate * 2];
        for (var i = 0; i < sine.Length; i++) sine[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 220 * i / rate)) + (i >= 40000 ? 0.1f : 0f);   // one injected step
        var sineFlags = TabForge.Diagnostics.AudioAudit.FindClicks(sine, rate, empty).Count;
        Check("audio audit: a 55 Hz saw (periodic steep edges) gives no click flags", sawFlags == 0, $"{sawFlags} flags");
        Check("audio audit: a sine with one injected step gives exactly one click flag", sineFlags == 1, $"{sineFlags} flags");
        var pair = new float[rate * 2];
        for (var i = 0; i < pair.Length; i++) pair[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 220 * i / rate)) + (i >= 40000 ? 0.1f : 0f) + (i >= 42400 ? 0.1f : 0f);   // two steps 50 ms apart
        var pairFlags = TabForge.Diagnostics.AudioAudit.FindClicks(pair, rate, empty).Count;
        Check("audio audit: two similar clicks 50 ms apart are both still flagged (one neighbour is not a waveform)", pairFlags == 2, $"{pairFlags} flags");
    }

    /// <summary>A7-A01: the master safety limiter holds the -0.3 dBFS ceiling, leaves quiet audio untouched, allocates nothing, and is on for renders / off for live playback by default.</summary>
    private static void TestSafetyLimiter()
    {
        const int rate = 48000, block = 256;
        var ceiling = EM.SafetyLimiter.DefaultCeiling;
        var limiter = new EM.SafetyLimiter(rate);
        // A loud signal: a +6 dB sine with +12 dB bursts and a single full-scale-times-ten spike.
        var frames = rate * 2;
        var inL = new float[frames]; var inR = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var burst = (i / 4800) % 5 == 0 ? 2f : 1f;
            inL[i] = (float)(2.0 * burst * Math.Sin(2 * Math.PI * 110 * i / rate));
            inR[i] = (float)(1.5 * burst * Math.Sin(2 * Math.PI * 165 * i / rate));
        }
        inL[70000] = 10f;
        var outL = (float[])inL.Clone(); var outR = (float[])inR.Clone();
        var bl = new float[block]; var br = new float[block];
        for (var o = 0; o < frames; o += block)
        {
            var n = Math.Min(block, frames - o);
            Array.Copy(outL, o, bl, 0, n); Array.Copy(outR, o, br, 0, n);
            limiter.Process(bl, br, n);
            Array.Copy(bl, 0, outL, o, n); Array.Copy(br, 0, outR, o, n);
        }
        var peak = 0f;
        for (var i = 0; i < frames; i++) peak = Math.Max(peak, Math.Max(Math.Abs(outL[i]), Math.Abs(outR[i])));
        Check("safety limiter: a clipping signal comes out at or below -0.3 dBFS", peak <= ceiling + 1e-6f && peak > ceiling * 0.97f, $"peak {peak:0.0000} (ceiling {ceiling:0.0000})");
        // Stereo-linked: the image does not shift (the L/R ratio of a sample is kept, away from the clamp).
        var ratioKept = true;
        for (var i = limiter.Latency; i < frames && ratioKept; i += 97)
        {
            var a = inL[i - limiter.Latency]; var b = inR[i - limiter.Latency];
            if (Math.Abs(a) < 0.2f || Math.Abs(b) < 0.2f) continue;
            ratioKept = Math.Abs(outL[i] / a - outR[i] / b) < 1e-3f;
        }
        Check("safety limiter: stereo-linked (both channels get the same gain)", ratioKept);

        // A quiet signal: bit-identical (only delayed), also long after the loud part (the release has finished).
        limiter = new EM.SafetyLimiter(rate);
        var quiet = new float[rate]; var quietR = new float[rate];
        for (var i = 0; i < rate; i++) { quiet[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 220 * i / rate)); quietR[i] = -quiet[i] * 0.5f; }
        var qL = (float[])quiet.Clone(); var qR = (float[])quietR.Clone();
        for (var o = 0; o < rate; o += block)
        {
            var n = Math.Min(block, rate - o);
            Array.Copy(qL, o, bl, 0, n); Array.Copy(qR, o, br, 0, n);
            limiter.Process(bl, br, n);
            Array.Copy(bl, 0, qL, o, n); Array.Copy(br, 0, qR, o, n);
        }
        var worst = 0f;
        for (var i = limiter.Latency; i < rate; i++) worst = Math.Max(worst, Math.Max(Math.Abs(qL[i] - quiet[i - limiter.Latency]), Math.Abs(qR[i] - quietR[i - limiter.Latency])));
        Check("safety limiter: quiet audio is unchanged (within 1e-6), delayed by the lookahead only", worst <= 1e-6f, $"worst difference {worst}");

        // Release: after a loud burst, a quiet tone is back at unity gain within half a second.
        limiter = new EM.SafetyLimiter(rate);
        var rl = new float[2 * rate]; var rr = new float[2 * rate];
        for (var i = 0; i < 2400; i++) { rl[i] = 4f * (float)Math.Sin(2 * Math.PI * 200 * i / rate); rr[i] = rl[i]; }
        for (var i = 2400; i < 2 * rate; i++) { rl[i] = 0.25f; rr[i] = 0.25f; }
        var releaseIn = (float[])rl.Clone();
        for (var o = 0; o < 2 * rate; o += block)
        {
            var n = Math.Min(block, 2 * rate - o);
            Array.Copy(rl, o, bl, 0, n); Array.Copy(rr, o, br, 0, n);
            limiter.Process(bl, br, n);
            Array.Copy(bl, 0, rl, o, n);
        }
        Check("safety limiter: gain recovers after the burst (smooth release, no stuck reduction)", rl[2 * rate - 1000] == releaseIn[2 * rate - 1000 - limiter.Latency] && rl[2600] < 0.1f && rl[rate / 4] > 0.22f, $"after burst {rl[2600]:0.0000}, 250 ms {rl[rate / 4]:0.0000}, end {rl[2 * rate - 1000]:0.0000}");

        // Audio thread: no allocation once built.
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var o = 0; o + block <= frames; o += block)
        {
            Array.Copy(inL, o, bl, 0, block); Array.Copy(inR, o, br, 0, block);
            limiter.Process(bl, br, block);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("safety limiter: Process allocates nothing", allocated == 0, $"{allocated} bytes");

        // Non-finite input is silenced, never poisons the gain.
        limiter = new EM.SafetyLimiter(rate);
        bl[0] = float.NaN; br[0] = float.PositiveInfinity; for (var i = 1; i < block; i++) { bl[i] = 0.1f; br[i] = 0.1f; }
        limiter.Process(bl, br, block);
        var finite = true; for (var i = 0; i < block; i++) finite &= float.IsFinite(bl[i]) && float.IsFinite(br[i]);
        Check("safety limiter: a NaN / infinite sample does not reach the output", finite);

        // Defaults: render on, live off; the render switch survives the engine protocol.
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var mix = new EM.MixEngine(shared, rate, block);
        var spec = new RenderSpec { StartFrame = 0, EndFrame = 1000 };
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) { spec.Write(w); spec.SafetyLimiter = false; spec.Write(w); }
        ms.Position = 0;
        using var rd = new BinaryReader(ms);
        var on = RenderSpec.Read(rd).SafetyLimiter; var off = RenderSpec.Read(rd).SafetyLimiter;
        Check("safety limiter defaults: renders ON, live playback OFF (settings and engine)",
            new TabForge.Rendering.RenderSettings().SafetyLimiter && new RenderSpec().SafetyLimiter && !new TabForge.Services.PluginSettings().LiveLimiter && !mix.LiveLimiter);
        Check("safety limiter: the render spec carries the switch through the engine protocol", on && !off, $"on {on}, off {off}");

        // Render path: the lookahead is compensated, also when the auto tail trims the held silence (the last audible frames
        // must not be lost) and on the fixed-tail / hard-cap path. Quiet material renders byte-identical with the limiter on or off.
        foreach (var mode in new[] { RenderTailMode.Auto, RenderTailMode.Fixed })
        {
            var plain = LimiterRender(false, mode, 0.25f);
            var limited = LimiterRender(true, mode, 0.25f);
            Check($"safety limiter render ({mode} tail): quiet master is byte-identical to the unlimited one and as long as the stem",
                plain.Master.Length > 1000 && plain.Master.AsSpan().SequenceEqual(limited.Master) && limited.Master.Length == limited.Stem.Length,
                $"plain {plain.Master.Length}, limited {limited.Master.Length}, stem {limited.Stem.Length} bytes");
        }
        var loud = LimiterRender(true, RenderTailMode.Auto, 2f);
        var loudPeak = 0f;
        var dataAt = loud.Master.AsSpan().IndexOf("data"u8) + 8;
        for (var i = dataAt; dataAt >= 8 && i + 4 <= loud.Master.Length; i += 4) loudPeak = MathF.Max(loudPeak, MathF.Abs(BitConverter.ToSingle(loud.Master, i)));
        Check("safety limiter render: a +6 dB master is held at -0.3 dBFS and keeps the stem's length",
            loudPeak <= ceiling + 1e-6f && loudPeak > ceiling * 0.97f && loud.Master.Length == loud.Stem.Length, $"peak {loudPeak:0.0000}, {loud.Master.Length} / {loud.Stem.Length} bytes");
    }

    /// <summary>Constant level until an absolute frame, then silence (the auto tail trims after it).</summary>
    private sealed class GatedConstInstrument(float level, long until) : EP.IPluginInstance
    {
        private long _frame;
        public string Path => "selftest-gated";
        public bool IsInstrument => true;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<EP.BlockMidi> midi, in EP.TransportInfo transport)
        {
            for (var i = 0; i < frames; i++, _frame++) { var v = _frame < until ? level : 0f; output[0][i] = v; output[1][i] = v; }
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }

    private static (byte[] Master, byte[] Stem) LimiterRender(bool limiter, RenderTailMode mode, float level)
    {
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var mix = new EM.MixEngine(shared, 48000, 64);
        // The level ends 1000 frames into the tail (not on a block edge), so the auto tail's last audible block is in the tail.
        mix.SetChain(0, new EM.TrackChain(0, 0, null, new[] { new EM.TrackChain.Effect(new GatedConstInstrument(level, 5800), 1f, 0, IsInstrument: true) }, 64));
        mix.SetGraph(new EM.MixEngine.RenderGraph(new[] { 0 }, EM.MixEngine.RenderGraph.NewDest(), Array.Empty<int>()));
        var dir = Directory.CreateTempSubdirectory("tf-limiter-").FullName;
        try
        {
            var events = Path.Combine(dir, "e.events");
            RenderEventFile.Write(events, new List<RenderEvent>());
            var master = Path.Combine(dir, "m.wav"); var stem = Path.Combine(dir, "s.wav");
            var spec = new RenderSpec
            {
                StartFrame = 0, EndFrame = 4800, TailMode = mode, TailMs = 2000, Channels = 2, SafetyLimiter = limiter,
                Format = RenderFormat.Float32, MasterPath = master, EventFile = events, Threads = RenderThreads.One,
                Slots = { new RenderSlot { Slot = 0, StemPath = stem } },
                Tempo = { new RenderTempoPoint(0, 120, 0) },
            };
            var renderer = new EM.OfflineRenderer(spec, mix, shared, 48000, 64, () => false, _ => { });
            renderer.Prepare();
            try { renderer.Run(); } finally { renderer.Restore(); }
            return (File.ReadAllBytes(master), File.ReadAllBytes(stem));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static float[][] Stereo(int frames) => new[] { new float[frames], new float[frames] };

    /// <summary>A bus chain (unity level) whose input is <paramref name="busL"/> / <paramref name="busR"/>, rendered once into a fresh mix.</summary>
    private static (float[] L, float[] R) RenderBus(EM.TrackChain chain, SharedBlock shared, float[] busL, float[] busR, int frames)
    {
        var mixL = new float[frames]; var mixR = new float[frames];
        chain.Render(mixL, mixR, 0, frames, new EP.TransportInfo { Tempo = 120 }, shared, null, null, busL, busR);
        return (mixL, mixR);
    }

    /// <summary>RT-01: pin labels map once to an enum; the wiring is right and the chain renders without allocating.</summary>
    private static void TestPinModes()
    {
        const int frames = 256;
        var parsed = EM.PinModes.Parse("Stereo") == EM.PinMode.Stereo && EM.PinModes.Parse("Mono (L+R)") == EM.PinMode.Mono
            && EM.PinModes.Parse("Left only") == EM.PinMode.LeftOnly && EM.PinModes.Parse("Right only") == EM.PinMode.RightOnly
            && EM.PinModes.Parse("Swap L/R") == EM.PinMode.Swap && EM.PinModes.Parse(null) == EM.PinMode.Stereo && EM.PinModes.Parse("??") == EM.PinMode.Stereo;
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var busL = new float[frames]; var busR = new float[frames];
        for (var i = 0; i < frames; i++) { busL[i] = 0.25f; busR[i] = -0.5f; }
        var results = new Dictionary<string, (float L, float R)>();
        long allocated = 0;
        foreach (var pins in new[] { "Swap L/R", "Mono (L+R)", "Left only", "Right only" })
        {
            var plugin = EP.Vst2Plugin.TestEffect.Create(48000, frames);
            var effect = new EM.TrackChain.Effect(plugin, 1f, 0, pins);
            using var chain = new EM.TrackChain(EM.MixEngine.BusBase, -1, null, new[] { effect }, frames);
            RenderBus(chain, shared, busL, busR, frames);   // warm-up (JIT)
            var mixL = new float[frames]; var mixR = new float[frames];
            var transport = new EP.TransportInfo { Tempo = 120 };
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var n = 0; n < 50; n++) chain.Render(mixL, mixR, 0, frames, transport, shared, null, null, busL, busR);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            results[pins] = (mixL[frames / 2] / 50, mixR[frames / 2] / 50);
        }
        bool Near((float L, float R) got, float l, float r) => Math.Abs(got.L - l) < 1e-5f && Math.Abs(got.R - r) < 1e-5f;
        Check("RT-01: pin labels map once to PinMode (unknown / null = stereo)", parsed);
        Check("RT-01: swap, mono, left-only and right-only wiring reach the effect as labelled",
            Near(results["Swap L/R"], -0.5f, 0.25f) && Near(results["Mono (L+R)"], -0.125f, -0.125f)
            && Near(results["Left only"], 0.25f, 0.25f) && Near(results["Right only"], -0.5f, -0.5f),
            string.Join(", ", results.Select(kv => $"{kv.Key} {kv.Value.L:0.###}/{kv.Value.R:0.###}")));
        Check("RT-01: 200 chain renders through wired effects allocate nothing (no string switch, no lazy buffers)", allocated == 0, $"{allocated} B");
    }

    private static volatile float _tiny = 1e-30f, _small = 1e-10f;

    /// <summary>RT-02: the audio thread flushes denormals (FTZ and DAZ); an unmarked thread does not.</summary>
    private static void TestDenormalsFlushed()
    {
        float marked = -1, unmarked = -1; var flagged = false;
        var t1 = new Thread(() => { TabForge.AudioEngine.EngineThreads.MarkAudioThread(); flagged = TabForge.AudioEngine.EngineThreads.DenormalsFlushed; marked = _tiny * _small; });
        var t2 = new Thread(() => unmarked = _tiny * _small);
        t1.Start(); t1.Join(5000); t2.Start(); t2.Join(5000);
        Check("RT-02: MarkAudioThread sets flush-to-zero / denormals-are-zero (1e-30 x 1e-10 is 0 there, a denormal elsewhere)",
            flagged && marked == 0f && unmarked != 0f, $"flag {flagged}, marked {marked:E2}, unmarked {unmarked:E2}");
    }

    /// <summary>RT-02: a NaN plug-in is skipped (its input passes on), reported once, and gets another chance when switched on again.</summary>
    private static void TestNonFiniteAudio()
    {
        const int frames = 128;
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var busL = Enumerable.Repeat(0.1f, frames).ToArray(); var busR = Enumerable.Repeat(0.2f, frames).ToArray();
        var signal = Volatile.Read(ref EM.TrackChain.MisbehaveSignal);
        var plugin = EP.Vst2Plugin.TestEffect.Create(48000, frames);
        var effect = new EM.TrackChain.Effect(plugin, 1f, 0);
        using var chain = new EM.TrackChain(EM.MixEngine.BusBase, -1, null, new[] { effect }, frames);
        EP.Vst2Plugin.TestEffect.OutputNaN = true;
        (float[] L, float[] R) out1, out2;
        var reports = new List<(int Index, string Path)>();
        var again = new List<(int Index, string Path)>();
        try
        {
            out1 = RenderBus(chain, shared, busL, busR, frames);
            out2 = RenderBus(chain, shared, busL, busR, frames);
            chain.CollectMisbehaved(reports);
            chain.CollectMisbehaved(again);
        }
        finally { EP.Vst2Plugin.TestEffect.OutputNaN = false; }
        var finite = out1.L.Concat(out1.R).Concat(out2.L).Concat(out2.R).All(float.IsFinite);
        var dry = Math.Abs(out1.L[5] - 0.1f) < 1e-6f && Math.Abs(out2.R[5] - 0.2f) < 1e-6f;
        Check("RT-02: a plug-in's NaN output never reaches the mix; the plug-in is skipped and its input passes on",
            finite && dry && effect.Misbehaved, $"finite {finite}, dry {dry} ({out1.L[5]}), misbehaved {effect.Misbehaved}");
        Check("RT-02: the misbehaving plug-in is reported once (signal raised, one report, none on the next look)",
            Volatile.Read(ref EM.TrackChain.MisbehaveSignal) != signal && reports.Count == 1 && reports[0].Index == 0
            && reports[0].Path == EP.Vst2Plugin.TestEffect.PathName && again.Count == 0, $"reports {reports.Count}, again {again.Count}");
        chain.SetPluginBypass(0, false);
        var out3 = RenderBus(chain, shared, busL, busR, frames);
        Check("RT-02: switched on again, the plug-in processes again", !effect.Misbehaved && out3.L.All(float.IsFinite), $"misbehaved {effect.Misbehaved}");

        // Non-finite audio with no plug-in to blame (here a NaN bus input): the chain's block is silenced and reported once as index -1.
        using var bare = new EM.TrackChain(EM.MixEngine.BusBase + 1, -1, null, Array.Empty<EM.TrackChain.Effect>(), frames);
        var poisonL = (float[])busL.Clone(); poisonL[7] = float.PositiveInfinity;
        var out4 = RenderBus(bare, shared, poisonL, busR, frames);
        var bareReports = new List<(int Index, string Path)>();
        bare.CollectMisbehaved(bareReports);
        Check("RT-02: a non-finite chain output (no plug-in at fault) is silenced, not summed, and reported once (index -1)",
            out4.L.All(v => v == 0) && out4.R.All(v => v == 0) && bareReports.Count == 1 && bareReports[0].Index == -1,
            $"reports {bareReports.Count}, first sample {out4.L[0]}");
    }

    /// <summary>RT-04: the bar map locates ppq, bar start and meter (odd meters, past the end), round-trips and rejects bad maps.</summary>
    private static void TestTransportMap()
    {
        var bars = new[]
        {
            new TransportBar(0, 0, 120, 4, 4),     // 2 s
            new TransportBar(2, 4, 120, 3, 4),     // 1.5 s
            new TransportBar(3.5, 7, 120, 6, 8),   // 1.5 s, then the map ends
        };
        var cursor = 0;
        var inBar = TransportMap.Locate(bars, 2.75, ref cursor, out var ppq, out var tempo, out var meter);
        var ok1 = inBar && Math.Abs(ppq - 5.5) < 1e-9 && tempo == 120 && meter.Numerator == 3 && meter.Denominator == 4 && meter.BarStartPpq == 4 && cursor == 1;
        TransportMap.Locate(bars, 10, ref cursor, out var ppqEnd, out _, out var meterEnd);   // 7 + 6.5 s x 2 = 20 quarters; 6/8 bars of 3
        var ok2 = Math.Abs(ppqEnd - 20) < 1e-9 && meterEnd.Numerator == 6 && meterEnd.Denominator == 8 && Math.Abs(meterEnd.BarStartPpq - 19) < 1e-9;
        TransportMap.Locate(bars, 0.5, ref cursor, out var ppqJump, out _, out var meterJump);   // a jump back: binary search
        var ok3 = Math.Abs(ppqJump - 1) < 1e-9 && meterJump.BarStartPpq == 0 && cursor == 0;
        var empty = !TransportMap.Locate(Array.Empty<TransportBar>(), 1, ref cursor, out _, out _, out var none) && !none.IsValid;
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true)) TransportMap.Write(w, bars);
        body.Position = 0;
        var back = TransportMap.Read(new BinaryReader(body));
        bool Rejects(TransportBar bad)
        {
            using var s = new MemoryStream();
            using (var w = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true)) TransportMap.Write(w, new[] { bad });
            s.Position = 0;
            try { TransportMap.Read(new BinaryReader(s)); return false; } catch (InvalidDataException) { return true; }
        }
        Check("RT-04: bar map: 3/4 bar -> ppq 5.5, bar start 4; past the end whole 6/8 bars continue (bar start 19); jumps back",
            ok1 && ok2 && ok3 && empty, $"ppq {ppq}, meter {meter.Numerator}/{meter.Denominator} @ {meter.BarStartPpq}; end {ppqEnd} @ {meterEnd.BarStartPpq}; jump {ppqJump}");
        Check("RT-04: bar map round-trips on the wire; a non power-of-two denominator or NaN tempo is refused",
            back.SequenceEqual(bars) && Rejects(new TransportBar(0, 0, 120, 4, 3)) && Rejects(new TransportBar(0, 0, double.NaN, 4, 4)));
    }

    /// <summary>RT-05: channel pointer arrays as declared; more than 128 refused with effClose after effOpen.</summary>
    private static void TestVst2ChannelsAndFailure()
    {
        var wide = EP.Vst2Plugin.TestEffect.Create(48000, 256, inputs: 40, outputs: 6);
        var channels = wide.InputChannels;
        var input = Enumerable.Range(0, 40).Select(_ => new float[256]).ToArray();
        wide.Process(input, Stereo(256), 256, default, new EP.TransportInfo { Tempo = 120 });
        wide.Dispose();
        EP.Vst2Plugin.TestEffect.ResetLog(256);
        string? refused = null;
        try { EP.Vst2Plugin.TestEffect.Create(48000, 256, inputs: 200); }
        catch (InvalidOperationException ex) { refused = ex.Message; }
        var ops = EP.Vst2Plugin.TestEffect.Opcodes;
        var open = Array.IndexOf(ops, 0); var close = Array.IndexOf(ops, 1);
        Check("RT-05: a plug-in declaring 40 inputs gets all 40 channel pointers (was clamped to 32: it read past the array)", channels == 40, $"{channels}");
        Check("RT-05: 200 declared inputs are refused, and the failure path closes what effOpen opened",
            refused is not null && open >= 0 && close > open, $"refused '{refused}', opcodes [{string.Join(",", ops)}]");
    }

    /// <summary>RT-07: the breadcrumb copies the path only when the path instance changes.</summary>
    private static void TestBreadcrumbPathCache()
    {
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var path = new string('a', 40);
        shared.EnterPlugin(3, 1, path);
        var first = shared.AudioPluginCall()?.Path;
        // Mutate the (private, never interned) string in place: a second call with the same instance must not copy it again.
        Unsafe.AsRef(in path.GetPinnableReference()) = 'Z';
        shared.EnterPlugin(3, 1, path);
        var second = shared.AudioPluginCall()?.Path;
        shared.EnterPlugin(3, 2, "other.dll");
        var third = shared.AudioPluginCall();
        shared.LeavePlugin();
        Check("RT-07: the breadcrumb path is copied once per path instance (same instance: no copy; a new path: copied)",
            first == new string('a', 40) && second == first && third is (3, 2, "other.dll"), $"second '{second}', third '{third?.Path}'");
    }

    /// <summary>M-05: MIDI dropped by a full chain buffer is counted and reaches the metrics snapshot.</summary>
    private static void TestMidiOverflowCounted()
    {
        using var chain = new EM.TrackChain(0, -1, null, Array.Empty<EM.TrackChain.Effect>(), 256);
        new EM.CallbackMetrics().TakeAndReset();   // start from zero
        for (var i = 0; i < 1100; i++) chain.AddEvent(0, 0x90, 60, 100);
        var snapshot = new EM.CallbackMetrics().TakeAndReset();
        // 1024-event chain buffer and 1024-event instrument buffer (the track's own MIDI goes to both): 76 dropped from each.
        Check("M-05: a full chain MIDI buffer counts its drops (1,100 events -> 152 dropped) in the callback metrics", snapshot.MidiDropped == 152,
            $"dropped {snapshot.MidiDropped}");
    }

    /// <summary>D10 / RT-10: one reference-counted owner of the 1 ms timer resolution.</summary>
    private static void TestNativeTimerRefCount()
    {
        var base0 = NativeTimer.Holds;
        NativeTimer.Acquire(); NativeTimer.Acquire();
        var two = NativeTimer.Holds == base0 + 2 && NativeTimer.Raised;
        NativeTimer.Release();
        var one = NativeTimer.Holds == base0 + 1 && NativeTimer.Raised;
        NativeTimer.Release();
        var back = NativeTimer.Holds == base0 && NativeTimer.Raised == (base0 > 0);
        var hold = new NativeTimer.Hold();
        hold.Set(true); hold.Set(true);
        var once = NativeTimer.Holds == base0 + 1 && hold.IsHeld;
        hold.Set(false); hold.Set(false);
        var released = NativeTimer.Holds == base0 && !hold.IsHeld;
        NativeTimer.Release();   // an extra release is ignored
        Check("D10/RT-10: NativeTimer is reference-counted (raised while any holder, released by the last); a Hold is idempotent",
            two && one && back && once && released && NativeTimer.Holds == base0, $"base {base0}, now {NativeTimer.Holds}");
    }

    /// <summary>C6: the per-track GM synth is capped at 32 voices with reverb / chorus off.</summary>
    private static void TestGmPolyphony()
    {
        TabForge.AudioEngine.Synth.GmSynth synth;
        try { synth = new TabForge.AudioEngine.Synth.GmSynth(48000, 256); }
        catch (Exception ex)   // no gm.dls / bank conversion failed on this machine: the cap cannot be observed here
        {
            Skip("C6: GM synth polyphony cap", $"the General MIDI synth could not be created here ({ex.GetType().Name})");
            return;
        }
        Check("C6: each track's GM synth plays at most 32 voices, reverb / chorus off",
            synth.Polyphony == TabForge.AudioEngine.Synth.GmSynth.MaxVoices && synth.Polyphony == 32 && !TabForge.AudioEngine.Synth.GmSynthTuning.ReverbAndChorus,
            $"polyphony {synth.Polyphony}");
        synth.Dispose();
    }

    /// <summary>M-01 / C11: queued PID-prefixed lines written by the background writer; the log rotates instead of being deleted.</summary>
    private static void TestEngineLogRotation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-selftest-log-{Guid.NewGuid():N}.log");
        var rotated = Path.ChangeExtension(path, ".1.log");
        TabForge.AudioEngine.EngineLog.UseFileForTests(path, 1 << 20);
        try
        {
            var sw = Stopwatch.StartNew();
            var writers = Enumerable.Range(0, 4).Select(t => new Thread(() => { for (var i = 0; i < 50; i++) TabForge.AudioEngine.EngineLog.Write($"selftest {t}-{i} {new string('x', 40)}"); })).ToList();
            writers.ForEach(t => t.Start()); writers.ForEach(t => t.Join(5000));
            var queuedMs = sw.Elapsed.TotalMilliseconds;
            TabForge.AudioEngine.EngineLog.Flush();
            var firstLines = File.ReadAllLines(path);
            TabForge.AudioEngine.EngineLog.UseFileForTests(path, 4096);   // the ~15 KB file is now past the limit: the next batch rotates
            TabForge.AudioEngine.EngineLog.Write("selftest after rotation");
            TabForge.AudioEngine.EngineLog.Flush();
            var pid = $"[{Environment.ProcessId}]";
            var lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
            var history = File.Exists(rotated) ? File.ReadAllLines(rotated) : Array.Empty<string>();
            Check("M-01: 200 lines from 4 threads are all written by the background writer, each with the process id",
                firstLines.Count(l => l.Contains("selftest ")) == 200 && firstLines.All(l => l.Contains(pid)), $"{firstLines.Length} lines, queued in {queuedMs:0.0} ms");
            Check("M-01: past the size limit the log rotates to .1.log (history kept, not deleted)",
                history.Count(l => l.Contains("selftest ")) == 200 && lines.Count(l => l.Contains("selftest ")) == 1 && lines.Any(l => l.Contains("selftest after rotation")),
                $"current {lines.Length} lines, history {history.Length}");
        }
        finally
        {
            TabForge.AudioEngine.EngineLog.UseFileForTests(null);
            try { File.Delete(path); File.Delete(rotated); } catch (IOException) { }
        }
    }

    /// <summary>RT-03 / RT-04 / RT-08 / RT-02 through the real engine (headless harness, manual null output, hosted VST2 test effect).</summary>
    private static void TestHeadlessRealtime()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, rate, block) => spec.Path == EP.Vst2Plugin.TestEffect.PathName
            ? EP.Vst2Plugin.TestEffect.Create(rate, block) : throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        using var callback = new CallbackThread();
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: true));
            EH.LoadChain(0, "selftest", false, new List<PluginSpec> { new(EP.Vst2Plugin.TestEffect.PathName, "VST2", false, true, 100, null, Id: "fx") });
            var plugin = EH.ChainAt(0)?.Plugins.FirstOrDefault() as EP.Vst2Plugin;
            var edits = 0;
            if (plugin is not null) plugin.Edited += () => edits++;

            // RT-03: automation from processReplacing allocates nothing on the audio thread and raises Edited once on the main loop.
            callback.Pump(8);   // warm-up
            EH.Collect();
            edits = 0;
            EH.Mix?.Metrics.TakeAndReset();
            EP.Vst2Plugin.TestEffect.AutomateInProcess = true;
            int ran;
            try { ran = callback.Pump(20); }
            finally { EP.Vst2Plugin.TestEffect.AutomateInProcess = false; }
            var metrics = EH.Mix?.Metrics.TakeAndReset();
            var beforeCollect = edits;
            EH.Collect();
            Check("RT-03: 20 blocks of audioMasterAutomate from processReplacing: 0 bytes allocated on the audio thread",
                plugin is not null && ran == 20 && metrics is { AllocatedBytes: 0 }, $"ran {ran}, allocated {metrics?.AllocatedBytes} B");
            Check("RT-03: the edits are raised on the main loop, once (not per callback, not on the audio thread)",
                beforeCollect == 0 && edits == 1, $"before main loop {beforeCollect}, after {edits}");

            // RT-04: SetTransport with the bar map (reader-thread parser) -> the VST2 time info carries 3/4 and the bar start.
            var bars = new[] { new TransportBar(0, 0, 120, 4, 4), new TransportBar(2, 4, 120, 3, 4), new TransportBar(3.5, 7, 90, 6, 8) };
            var parsed = EH.Command(EngineCommand.SetTransport, w => { w.Write(120.0); w.Write(true); TransportMap.Write(w, bars); });
            var legacy = EH.Command(EngineCommand.SetTransport, w => { w.Write(120.0); w.Write(false); });   // an older sender: no map, map kept
            EH.Command(EngineCommand.SetPosition, w => { w.Write(true); w.Write(2.75); w.Write(Stopwatch.GetTimestamp()); });
            callback.Pump(2);
            var flags = EP.Vst2Plugin.TestEffect.TimeFlags;
            const int timeSigValid = 1 << 13, barsValid = 1 << 11;
            Check("RT-04: a 3/4 bar reaches the hosted VST2 as timeSig 3/4, barStartPos 4, ppqPos ~5.5, with kVstTimeSigValid | kVstBarsValid",
                parsed && legacy && EP.Vst2Plugin.TestEffect.TimeSigNumerator == 3 && EP.Vst2Plugin.TestEffect.TimeSigDenominator == 4
                && EP.Vst2Plugin.TestEffect.BarStartPpq == 4 && EP.Vst2Plugin.TestEffect.PpqPosition is > 5.2 and < 5.6
                && (flags & timeSigValid) != 0 && (flags & barsValid) != 0 && (flags & 2) != 0,
                $"sig {EP.Vst2Plugin.TestEffect.TimeSigNumerator}/{EP.Vst2Plugin.TestEffect.TimeSigDenominator}, bar {EP.Vst2Plugin.TestEffect.BarStartPpq}, ppq {EP.Vst2Plugin.TestEffect.PpqPosition:0.###}, flags 0x{flags:X}");

            // RT-08: the position lives outside the mixer: a device change (new MixEngine) keeps the last position the reader thread set.
            var oldMix = EH.Mix;
            EH.Command(EngineCommand.SetPosition, w => { w.Write(true); w.Write(42.0); w.Write(Stopwatch.GetTimestamp()); });
            EH.Configure(NullConfig(44100, 512, manual: true));
            var newMix = EH.Mix;
            var sec = newMix?.SongSecNow ?? -1;
            Check("RT-08: a position set before a device change is kept by the new mixer (one immutable record, not a torn pair on the old mixer)",
                newMix is not null && !ReferenceEquals(oldMix, newMix) && newMix.SongPlaying && sec is >= 42 and < 45, $"song sec {sec:0.###}");
            callback.Pump(2);
            Check("RT-04: the bar map survives the device change (the 6/8 tempo-90 bar is found after it)",
                EP.Vst2Plugin.TestEffect.TimeSigNumerator == 6 && EP.Vst2Plugin.TestEffect.TimeSigDenominator == 8 && EP.Vst2Plugin.TestEffect.Tempo == 90,
                $"sig {EP.Vst2Plugin.TestEffect.TimeSigNumerator}/{EP.Vst2Plugin.TestEffect.TimeSigDenominator}, tempo {EP.Vst2Plugin.TestEffect.Tempo}");

            // A chain follows the transport of the song that owns it: owner 2 plays a 5/4 bar at 100 while owner 0 keeps its own map.
            var otherBars = new[] { new TransportBar(0, 0, 100, 5, 4) };
            EH.Command(EngineCommand.SetTransport, w => { w.Write(100.0); w.Write(false); TransportMap.Write(w, otherBars); w.Write(2); });
            EH.Command(EngineCommand.SetPosition, w => { w.Write(true); w.Write(1.0); w.Write(Stopwatch.GetTimestamp()); w.Write(2); });
            EH.SetClips(0, new List<ClipSpec>(), owner: 2);
            callback.Pump(2);
            Check("owners: a plug-in chain sees the transport (playing, tempo, time signature) of the song that owns it, not owner 0's",
                EP.Vst2Plugin.TestEffect.TimeSigNumerator == 5 && EP.Vst2Plugin.TestEffect.TimeSigDenominator == 4 && EP.Vst2Plugin.TestEffect.Tempo == 100,
                $"sig {EP.Vst2Plugin.TestEffect.TimeSigNumerator}/{EP.Vst2Plugin.TestEffect.TimeSigDenominator}, tempo {EP.Vst2Plugin.TestEffect.Tempo}");
            var secOwner2 = EH.Mix?.SongSecFor(2) ?? -1; var secOwner0 = EH.Mix?.SongSecFor(0) ?? -1;
            Check("owners: a recording aligns to its own song's position (owner 2 near 1 s, owner 0 near 42 s)", secOwner2 is >= 1 and < 4 && secOwner0 >= 42, $"owner 2 {secOwner2:0.###}, owner 0 {secOwner0:0.###}");
            EH.SetClips(0, new List<ClipSpec>(), owner: 0);
            callback.Pump(2);
            Check("owners: back on owner 0 the chain sees owner 0's transport again",
                EP.Vst2Plugin.TestEffect.TimeSigNumerator == 6 && EP.Vst2Plugin.TestEffect.Tempo == 90, $"sig {EP.Vst2Plugin.TestEffect.TimeSigNumerator}, tempo {EP.Vst2Plugin.TestEffect.Tempo}");

            // RT-02 end to end: a NaN plug-in in a live chain is reported by the main loop exactly once.
            EP.Vst2Plugin.TestEffect.OutputNaN = true;
            try { callback.Pump(4); }
            finally { EP.Vst2Plugin.TestEffect.OutputNaN = false; }
            var first = EH.ReportMisbehaving();
            callback.Pump(4);
            var second = EH.ReportMisbehaving();
            Check("RT-02: the engine main loop reports a NaN plug-in once (PluginMisbehaved), then stays quiet",
                first == 1 && second == 0 && EH.ChainAt(0)?.Effects[0].Misbehaved == true, $"first {first}, second {second}");
        }
        finally
        {
            EP.Vst2Plugin.TestEffect.AutomateInProcess = false; EP.Vst2Plugin.TestEffect.OutputNaN = false;
            EH.Detach();
            SettleDisk();
            shared.Dispose();
        }
    }
}
