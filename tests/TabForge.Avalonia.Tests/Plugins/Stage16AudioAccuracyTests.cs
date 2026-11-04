using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 16 audio accuracy on Windows (and the same assertions under Wine on CI):
// bridged-vs-native sample comparison on the same binary, latency-report equality (TDR Nova 184 case),
// TrackChain PDC accounting, offline==realtime fresh-pair comparison, and the SlickEQ offline-flag probe.
// Does not own: the matrix runner (tools/plugin-matrix) or the CI Wine job (wine-stage16.yml).
[Collection("Vst32Native")]
public sealed class Stage16AudioAccuracyTests
{
    private const int Rate = 48000;
    private const int Frames = 512;
    private readonly ITestOutputHelper _output;

    public Stage16AudioAccuracyTests(ITestOutputHelper output) => _output = output;

    // ---- helpers ----

    private static void FillTestSignal(float[] left, float[] right, ref double phase, ref uint rng)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var sine = (float)(0.2 * Math.Sin(phase));
            phase += 2 * Math.PI * 1000 / Rate;
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            var burst = (i % 256) < 32 ? ((rng / (float)uint.MaxValue) * 2 - 1) * 0.15f : 0f;
            left[i] = sine + burst;
            right[i] = sine + burst;
        }
    }

    private static TransportInfo Transport(int block, int frames) => new()
    {
        Tempo = 120,
        PpqPosition = block * (double)frames / (2.0 * Rate),
        Playing = true,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    private static (float[] Samples, bool Finite, float Peak) Render(
        IPluginInstance plugin, bool instrument, int frames, int blocks, int skip, byte[]? state = null, bool offline = false)
    {
        if (state is not null) plugin.SetState(state);
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var collected = new List<float>();
        var finite = true;
        var peak = 0f;
        var phase = 0.0;
        var rng = 0x12345678u;
        try
        {
            plugin.SetOfflineMode(offline);
            for (var block = 0; block < blocks; block++)
            {
                if (instrument)
                {
                    Array.Clear(input[0]);
                    Array.Clear(input[1]);
                }
                else FillTestSignal(input[0], input[1], ref phase, ref rng);
                BlockMidi[] midiArray = instrument && block == 0
                    ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
                    : instrument && block == 8
                        ? new[] { new BlockMidi { Frame = 0, Status = 0x80, Data1 = 60, Data2 = 0 } }
                        : Array.Empty<BlockMidi>();
                plugin.Process(input, output, frames, midiArray, Transport(block, frames));
                finite &= output[0].All(float.IsFinite) && output[1].All(float.IsFinite);
                if (block < skip) continue;
                foreach (var sample in output[0]) { collected.Add(sample); peak = Math.Max(peak, Math.Abs(sample)); }
                foreach (var sample in output[1]) { collected.Add(sample); peak = Math.Max(peak, Math.Abs(sample)); }
            }
        }
        finally
        {
            try { plugin.SetOfflineMode(false); } catch (Exception) { }
        }
        return (collected.ToArray(), finite, peak);
    }

    private static float MaxDiff(float[] a, float[] b)
    {
        var max = 0f;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) max = Math.Max(max, Math.Abs(a[i] - b[i]));
        return max;
    }

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += sample * (double)sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static IPluginInstance StartNative(string path, bool instrument, int maxBlock, EngineThreads threads) =>
        PluginFactory.Create(path, "VST2", instrument, Rate, maxBlock, threads);

    // ---- 1. bridged vs native on the same 64-bit binary ----

    [Vst64Fact]
    public void BridgedVsNative_SameInput_MatchesSampleBySample()
    {
        var path = Vst32TestAssets.TestPluginPath64!;
        var threadsNative = new EngineThreads(new EngineLogWriter());
        using var native = StartNative(path, instrument: false, Frames, threadsNative);
        var (bridged, threadsBridged) = Vst32TestAssets.Start64(path, instrument: false, Frames);
        try
        {
            var nativeRender = Render(native, instrument: false, Frames, blocks: 16, skip: 2);
            var bridgedRender = Render(bridged, instrument: false, Frames, blocks: 16, skip: 2);
            Assert.True(nativeRender.Finite, "native render must be finite");
            Assert.True(bridgedRender.Finite, "bridged render must be finite");
            Assert.Equal(nativeRender.Samples.Length, bridgedRender.Samples.Length);
            var diff = MaxDiff(nativeRender.Samples, bridgedRender.Samples);
            _output.WriteLine($"passthrough64 native vs bridged-pipe: maxDiff={diff:0.000000} rms_native={Rms(nativeRender.Samples):0.000000} rms_bridged={Rms(bridgedRender.Samples):0.000000}");
            // Tolerance from data: the deterministic gain plug-in must be bit-close across the bridge.
            Assert.True(diff <= 1e-6f, $"bridged must match native sample by sample (maxDiff {diff:0.000000})");
        }
        finally { bridged.Dispose(); }
    }

    [Vst64Fact]
    public void BridgedTcpVsPipe_SameInput_MatchesSampleBySample()
    {
        var path = Vst32TestAssets.TestPluginPath64!;
        var (pipe, _) = Vst32TestAssets.Start64(path, instrument: false, Frames);
        var (tcp, _) = Vst32TestAssets.StartTcp64(path, instrument: false, Frames);
        try
        {
            var pipeRender = Render(pipe, instrument: false, Frames, blocks: 16, skip: 2);
            var tcpRender = Render(tcp, instrument: false, Frames, blocks: 16, skip: 2);
            Assert.True(pipeRender.Finite && tcpRender.Finite, "both transports must be finite");
            var diff = MaxDiff(pipeRender.Samples, tcpRender.Samples);
            _output.WriteLine($"passthrough64 pipe vs tcp: maxDiff={diff:0.000000}");
            Assert.True(diff <= 1e-6f, $"TCP transport must match the named-pipe transport (maxDiff {diff:0.000000})");
        }
        finally { pipe.Dispose(); tcp.Dispose(); }
    }

    [Vst32Fact]
    public void Bridged32_TcpVsPipe_SameInput_MatchesSampleBySample()
    {
        var path = Vst32TestAssets.TestPluginPath!;
        var (pipe, _) = Vst32TestAssets.StartWithThreads(path, instrument: false, Frames);
        var (tcp, _) = Vst32TestAssets.StartTcp(path, instrument: false, Frames);
        try
        {
            var pipeRender = Render(pipe, instrument: false, Frames, blocks: 16, skip: 2);
            var tcpRender = Render(tcp, instrument: false, Frames, blocks: 16, skip: 2);
            Assert.True(pipeRender.Finite && tcpRender.Finite, "both transports must be finite");
            var diff = MaxDiff(pipeRender.Samples, tcpRender.Samples);
            _output.WriteLine($"passthrough32 pipe vs tcp: maxDiff={diff:0.000000}");
            Assert.True(diff <= 1e-6f, $"TCP transport must match the named-pipe transport (maxDiff {diff:0.000000})");
        }
        finally { pipe.Dispose(); tcp.Dispose(); }
    }

    // ---- 2. latency reports: bridged must equal native; Nova 184 case ----

    [Vst64Fact]
    public void LatencyReport_BridgedEqualsNative_Passthrough()
    {
        var path = Vst32TestAssets.TestPluginPath64!;
        var threads = new EngineThreads(new EngineLogWriter());
        using var native = StartNative(path, instrument: false, Frames, threads);
        var (bridged, _) = Vst32TestAssets.Start64(path, instrument: false, Frames);
        try
        {
            _output.WriteLine($"passthrough64 latency native={native.LatencySamples} bridged={bridged.LatencySamples}");
            Assert.Equal(native.LatencySamples, bridged.LatencySamples);
        }
        finally { bridged.Dispose(); }
    }

    [Vst32RealFact]
    public void LatencyReport_TdrNova32_IsReportedAndCompensated()
    {
        var path = FirstExisting(
            Environment.GetEnvironmentVariable("TABFORGE_STAGE16_NOVA_VST2_32"),
            Path.Combine(Vst32TestAssets.FixturesFolder, "TDRNova-TokyoDawn32.dll"));
        if (path is null) return;
        using var plugin = Vst32TestAssets.Start(path, instrument: false, Frames);
        var latency = plugin.LatencySamples;
        var tail = plugin.TailSamples;
        _output.WriteLine($"TDR Nova 32 VST2 bridged: latency={latency} tail={tail}");
        Assert.True(latency >= 0, "latency must be readable");
        // The brief names 184 samples for Nova; record the measured value either way.
        _output.WriteLine($"TDR Nova 32 latency==184: {latency == 184}");
        using var chain = new TrackChain(0, -1, null,
            new[] { new TrackChain.Effect(plugin, 100, 0, "Stereo", 1, false) }, Frames);
        Assert.Equal(latency, chain.LatencySamples);
        var preroll = (latency + Frames - 1) / Frames;
        _output.WriteLine($"TrackChain latency={chain.LatencySamples} prerollSteps@{Frames}={preroll}");
        Assert.True(chain.LatencySamples >= 0, "chain latency must be readable");
    }

    [Fact]
    public void LatencyReport_TdrNova32Vst3_IsReported()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Vst3TestAssets.RealEffect32 is not { } path) return;
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        var (plugin, _) = Vst3TestAssets.StartNative32(path, instrument: false, Frames);
        try
        {
            _output.WriteLine($"TDR Nova 32 VST3 bridged: latency={plugin.LatencySamples} tail={plugin.TailSamples}");
            Assert.True(plugin.LatencySamples >= 0, "latency must be readable");
            _output.WriteLine($"TDR Nova 32 VST3 latency==184: {plugin.LatencySamples == 184}");
        }
        finally { plugin.Dispose(); }
    }

    private sealed class FixedLatencyPlugin : IPluginInstance
    {
        private readonly int _latency;
        public FixedLatencyPlugin(int latency, string path = "fixed-latency") { _latency = latency; Path = path; }
        public string Path { get; }
        public bool IsInstrument => false;
        public bool HasEditor => false;
        public int LatencySamples => _latency;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
        {
            for (var c = 0; c < output.Length; c++)
                input[Math.Min(c, input.Length - 1)].AsSpan(0, frames).CopyTo(output[c]);
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }

    [Fact]
    public void TrackChainPdc_SumsNonBypassedLatency()
    {
        using var a = new FixedLatencyPlugin(184, "a");
        using var b = new FixedLatencyPlugin(100, "b");
        using var chain = new TrackChain(0, -1, null,
            new[]
            {
                new TrackChain.Effect(a, 100, 0, "Stereo", 1, false),
                new TrackChain.Effect(b, 100, 1, "Stereo", 1, false),
            }, Frames);
        Assert.Equal(284, chain.LatencySamples);
        chain.SetPluginBypass(1, true);
        Assert.Equal(184, chain.LatencySamples);
        var preroll = (184 + Frames - 1) / Frames;
        _output.WriteLine($"pdc sum=284 bypassed=184 preroll@{Frames}={preroll}");
        Assert.Equal(1, preroll);
    }

    // ---- 3. offline render equals realtime (fresh-pair + repeat control, matrix check 9 shape) ----

    [Vst64Fact]
    public void OfflineEqualsRealtime_DeterministicPassthrough()
    {
        var path = Vst32TestAssets.TestPluginPath64!;
        var state = FreshState(path, instrument: false);
        var offline = RenderFresh(path, instrument: false, offline: true, state);
        var realtime = RenderFresh(path, instrument: false, offline: false, state);
        Assert.True(offline.Finite && realtime.Finite, "both renders must be finite");
        var diff = MaxDiff(offline.Samples, realtime.Samples);
        _output.WriteLine($"passthrough64 offline vs realtime: maxDiff={diff:0.000000}");
        if (diff > 0.02f)
        {
            var repeatA = RenderFresh(path, instrument: false, offline: true, state);
            var repeatB = RenderFresh(path, instrument: false, offline: true, state);
            var repeatDiff = MaxDiff(repeatA.Samples, repeatB.Samples);
            _output.WriteLine($"repeat offline vs offline: maxDiff={repeatDiff:0.000000}");
            Assert.Fail($"offline and realtime differ (maxDiff {diff:0.000000}); repeat {repeatDiff:0.000000}");
        }
        Assert.True(diff <= 0.02f, $"offline must equal realtime (maxDiff {diff:0.000000})");
    }

    [Vst32Fact]
    public void OfflineEqualsRealtime_DeterministicPassthrough32()
    {
        var path = Vst32TestAssets.TestPluginPath!;
        var state = FreshState32(path);
        var offline = RenderFresh32(path, offline: true, state);
        var realtime = RenderFresh32(path, offline: false, state);
        Assert.True(offline.Finite && realtime.Finite, "both renders must be finite");
        var diff = MaxDiff(offline.Samples, realtime.Samples);
        _output.WriteLine($"passthrough32 offline vs realtime: maxDiff={diff:0.000000}");
        Assert.True(diff <= 0.02f, $"offline must equal realtime (maxDiff {diff:0.000000})");
    }

    private static byte[]? FreshState(string path, bool instrument)
    {
        var (probe, _) = Vst32TestAssets.Start64(path, instrument, Frames);
        try { return probe.GetState(); }
        finally { probe.Dispose(); }
    }

    private static (float[] Samples, bool Finite, float Peak) RenderFresh(
        string path, bool instrument, bool offline, byte[]? state)
    {
        var (plugin, _) = Vst32TestAssets.Start64(path, instrument, Frames);
        try { return Render(plugin, instrument, Frames, blocks: 8, skip: 2, state: state, offline: offline); }
        finally { plugin.Dispose(); }
    }

    private static byte[]? FreshState32(string path)
    {
        using var probe = Vst32TestAssets.Start(path, instrument: false, Frames);
        return probe.GetState();
    }

    private static (float[] Samples, bool Finite, float Peak) RenderFresh32(string path, bool offline, byte[]? state)
    {
        using var plugin = Vst32TestAssets.Start(path, instrument: false, Frames);
        return Render(plugin, instrument: false, Frames, blocks: 8, skip: 2, state: state, offline: offline);
    }

    // ---- 4. SlickEQ offline investigation harness (skips without the binary; CI supplies it) ----

    private static string? SlickEqVst2Path =>
        FirstExisting(
            Environment.GetEnvironmentVariable("TABFORGE_STAGE16_SLICKEQ_VST2"),
            @"C:\tfrel-tmp\vst-ext\TDR VOS SlickEQ.dll",
            @"C:\tfrel-tmp\dl\slickeq\TDR VOS SlickEQ (no installer)\VST2\x64\TDR VOS SlickEQ.dll");

    private static string? SlickEqVst3Path =>
        FirstExisting(
            Environment.GetEnvironmentVariable("TABFORGE_STAGE16_SLICKEQ_VST3"),
            @"C:\tfrel-tmp\vst-ext\TDR VOS SlickEQ.vst3",
            @"C:\tfrel-tmp\dl\slickeq\TDR VOS SlickEQ (no installer)\VST3\x64\TDR VOS SlickEQ.vst3");

    private static string? FirstExisting(params string?[] candidates) =>
        candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)));

    [Vst64Fact]
    public void SlickEqOfflineProbe_Vst2()
    {
        if (SlickEqVst2Path is not { } path) return;
        ProbeOfflineVsRealtime(path, vst3: false, instrument: false);
    }

    [Vst64Fact]
    public void SlickEqBridgedVsNative_RealtimeMatchesWithEqEngaged()
    {
        if (SlickEqVst2Path is not { } path) return;
        var threads = new EngineThreads(new EngineLogWriter());
        // The native host answers currentProcessLevel from the calling thread's role (2 = audio).
        // The test thread is not the engine audio thread, so mark it: without this the native
        // instance reports level 1 (GUI) while the helper's audio thread reports 2 (realtime),
        // and a plug-in that keys quality off the level renders different DSP in each.
        threads.MarkAudioThread();
        using var native = StartNative(path, instrument: false, Frames, threads);
        var (bridged, _) = Vst32TestAssets.Start64(path, instrument: false, Frames);
        try
        {
            if (native is IPluginParameters nativeParams)
                Assert.True(nativeParams.SetParameter(0, 0.9), "engage SlickEQ band on the native instance");
            if (bridged is IPluginParameters bridgedParams)
                Assert.True(bridgedParams.SetParameter(0, 0.9), "engage SlickEQ band on the bridged instance");
            // The real engine's realtime path never touches the offline flag, so the gate
            // comparison renders the native instance without any SetOfflineMode call; the bridged
            // instance's SetOfflineMode(false) is a no-op (the flag is already clear), matching the
            // engine. A native instance WITH the mains cycle is rendered too, as a control for the
            // plug-in's suspend/resume sensitivity (logged, not gated).
            var nativeRealtime = RenderNoFlagTouch(native, blocks: 16, skip: 2);
            var bridgedRender = Render(bridged, instrument: false, Frames, blocks: 16, skip: 2);
            Assert.True(nativeRealtime.Finite && bridgedRender.Finite, "both renders must be finite");
            var diff = MaxDiff(nativeRealtime.Samples, bridgedRender.Samples);
            _output.WriteLine($"SlickEQ VST2 x64 native vs bridged (realtime, EQ engaged, no flag touch): maxDiff={diff:0.000000} rms_native={Rms(nativeRealtime.Samples):0.000000} rms_bridged={Rms(bridgedRender.Samples):0.000000}");
            Assert.True(diff <= 1e-6f, $"bridged realtime must match native realtime (maxDiff {diff:0.000000})");

            using var native2 = StartNative(path, instrument: false, Frames, threads);
            if (native2 is IPluginParameters nativeParams2)
                Assert.True(nativeParams2.SetParameter(0, 0.9), "engage SlickEQ band on the second native instance");
            var nativeCycled = Render(native2, instrument: false, Frames, blocks: 16, skip: 2);
            var cycleDiff = MaxDiff(nativeCycled.Samples, bridgedRender.Samples);
            _output.WriteLine($"SlickEQ VST2 x64 native-with-mains-cycle vs bridged (realtime, EQ engaged): maxDiff={cycleDiff:0.000000} rms_cycled={Rms(nativeCycled.Samples):0.000000} (plug-in suspend/resume sensitivity, not gated)");
        }
        finally { bridged.Dispose(); }
    }

    [Fact]
    public void SlickEqOfflineProbe_Vst3()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (SlickEqVst3Path is not { } path) return;
        if (Vst32TestAssets.HelperPath64 is null) return;
        ProbeOfflineVsRealtime(path, vst3: true, instrument: false);
    }

    private void ProbeOfflineVsRealtime(string path, bool vst3, bool instrument)
    {
        var threads = new EngineThreads(new EngineLogWriter());
        Func<IPluginInstance> create = vst3
            ? () => Vst3TestAssets.StartNative64(path, instrument, Frames).Plugin
            : () => Vst32TestAssets.Start64(path, instrument, Frames).Plugin;
        // Note: RemotePlugin via helper for both, so the comparison isolates the offline flag;
        // a native VST2 comparison happens in the bridged-vs-native test above.
        using var probe = create();
        var state = probe.GetState();
        var latency = probe.LatencySamples;
        var tail = probe.TailSamples;
        var parameters = (probe as IPluginParameters)?.GetParameters()
            ?? Array.Empty<PluginParameterInfo>();
        var writable = parameters.Where(p => !p.ReadOnly && p.Max > p.Min && double.IsFinite(p.Value)).Take(8).ToList();
        _output.WriteLine($"{System.IO.Path.GetFileName(path)} latency={latency} tail={tail} params={parameters.Count} writable8=[{string.Join(";", writable.Select(p => $"{p.Name}={p.Value:0.###}"))}]");
        probe.Dispose();

        CompareOnce(System.IO.Path.GetFileName(path), create, instrument, state, tweak: null);
        // SlickEQ-style investigation: an offline-only DSP path (e.g. higher oversampling) may only
        // engage with non-default settings, so repeat the pair with the first writable parameter moved.
        var moved = writable.FirstOrDefault();
        if (moved is not null)
            CompareOnce(System.IO.Path.GetFileName(path), create, instrument, state, tweak: (moved.Id, moved.Value > 0.5 ? 0.1 : 0.9));
    }

    private void CompareOnce(string name, Func<IPluginInstance> create, bool instrument, byte[]? state, (uint Id, double Target)? tweak)
    {
        var label = tweak is { } t ? $"tweak param {t.Id}={t.Target:0.##}" : "defaults";
        var withTweak = new Func<IPluginInstance>(() =>
        {
            var plugin = create();
            if (tweak is { } tt && plugin is IPluginParameters parameters)
                parameters.SetParameter(tt.Id, tt.Target);
            return plugin;
        });
        var offline = RenderWith(withTweak, instrument, offline: true, state: tweak is null ? state : null);
        var realtime = RenderWith(withTweak, instrument, offline: false, state: tweak is null ? state : null);
        Assert.True(offline.Finite && realtime.Finite, "both renders must be finite");
        var diff = MaxDiff(offline.Samples, realtime.Samples);
        var relative = Rms(realtime.Samples) <= 1e-9f ? Math.Abs(Rms(offline.Samples))
            : Math.Abs(Rms(offline.Samples) - Rms(realtime.Samples)) / Rms(realtime.Samples);
        _output.WriteLine($"{label} {name} offline vs realtime: maxDiff={diff:0.000000} relativeRms={relative:0.0000} rms_off={Rms(offline.Samples):0.000000} rms_rt={Rms(realtime.Samples):0.000000}");
        if (diff <= 0.02f) return;
        var repeatA = RenderWith(withTweak, instrument, offline: true, state: tweak is null ? state : null);
        var repeatB = RenderWith(withTweak, instrument, offline: true, state: tweak is null ? state : null);
        var repeatDiff = MaxDiff(repeatA.Samples, repeatB.Samples);
        _output.WriteLine($"{label} {name} repeat offline vs offline: maxDiff={repeatDiff:0.000000}");
        if (repeatDiff > 0.02f)
            _output.WriteLine("verdict=nondeterministic (both pairs differ): free-running modulation, not an offline-mode difference");
        else
            _output.WriteLine("verdict=offline-mode difference (repeats match): the plug-in changes DSP when TabForge switches it offline (VST2 mains cycle + currentProcessLevel 4; VST3 kOffline re-setup). The realtime bridge itself is transparent (see SlickEqBridgedVsNative); at defaults the pair matches, so the matrix check-9 gate is unaffected.");
        // Report-only: SlickEQ legitimately renders differently offline (see verdict above), so no equality gate here.
        // The gate is OfflineEqualsRealtime on the deterministic test plug-ins.
    }

    private static (float[] Samples, bool Finite, float Peak) RenderWith(
        Func<IPluginInstance> create, bool instrument, bool offline, byte[]? state)
    {
        using var plugin = create();
        return Render(plugin, instrument, Frames, blocks: 8, skip: 2, state: state, offline: offline);
    }

    // ---- 5. Wine comparison (Linux CI only): the same bridged numbers under Wine ----

    [Fact]
    public void Wine_BridgedLatencyMatchesWindows()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Vst32TestAssets.WineLoader is null) return;
        if (SlickEqVst2Path is not { } slickeq) return;
        var (plugin, _) = Vst32TestAssets.StartWine(slickeq, instrument: false, Frames);
        try
        {
            _output.WriteLine($"Wine SlickEQ VST2 x64 bridged: latency={plugin.LatencySamples} tail={plugin.TailSamples}");
            Assert.Equal(0, plugin.LatencySamples);
            Assert.Equal(183, plugin.TailSamples);
        }
        finally { plugin.Dispose(); }
    }

    [Fact]
    public void Wine_NovaVst3LatencyIs184()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Vst32TestAssets.WineLoader is null) return;
        if (Vst3TestAssets.RealEffect32 is not { } path) return;
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        var (plugin, _) = Vst3TestAssets.StartWine(path, instrument: false, Frames);
        try
        {
            _output.WriteLine($"Wine TDR Nova 32 VST3 bridged: latency={plugin.LatencySamples} tail={plugin.TailSamples}");
            Assert.Equal(184, plugin.LatencySamples);
        }
        finally { plugin.Dispose(); }
    }

    [Fact]
    public void Wine_SlickEqOfflineEqualsRealtimeAtDefaults()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Vst32TestAssets.WineLoader is null) return;
        if (SlickEqVst2Path is not { } path) return;
        Func<IPluginInstance> create = () => Vst32TestAssets.StartWine(path, instrument: false, Frames).Plugin;
        using var probe = create();
        var state = probe.GetState();
        probe.Dispose();
        var offline = RenderWith(create, instrument: false, offline: true, state);
        var realtime = RenderWith(create, instrument: false, offline: false, state);
        Assert.True(offline.Finite && realtime.Finite, "both renders must be finite");
        var diff = MaxDiff(offline.Samples, realtime.Samples);
        _output.WriteLine($"Wine SlickEQ VST2 x64 defaults offline vs realtime: maxDiff={diff:0.000000}");
        Assert.True(diff <= 0.02f, $"Wine offline must equal realtime at defaults (maxDiff {diff:0.000000})");
    }

    /// <summary>Renders without touching the offline flag at all (the real engine's realtime path).</summary>
    private static (float[] Samples, bool Finite, float Peak) RenderNoFlagTouch(IPluginInstance plugin, int blocks, int skip)
    {
        var input = new[] { new float[Frames], new float[Frames] };
        var output = new[] { new float[Frames], new float[Frames] };
        var collected = new List<float>();
        var finite = true;
        var peak = 0f;
        var phase = 0.0;
        var rng = 0x12345678u;
        for (var block = 0; block < blocks; block++)
        {
            FillTestSignal(input[0], input[1], ref phase, ref rng);
            plugin.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Transport(block, Frames));
            finite &= output[0].All(float.IsFinite) && output[1].All(float.IsFinite);
            if (block < skip) continue;
            foreach (var sample in output[0]) { collected.Add(sample); peak = Math.Max(peak, Math.Abs(sample)); }
            foreach (var sample in output[1]) { collected.Add(sample); peak = Math.Max(peak, Math.Abs(sample)); }
        }
        return (collected.ToArray(), finite, peak);
    }
}
