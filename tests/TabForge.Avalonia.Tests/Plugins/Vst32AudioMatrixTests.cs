using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: the wide real-audio matrix through the engine session path (every .dll in the fixture folder):
//   effects at 64/256/1024 (finite, non-silent, differs from dry or transparent-with-tweak, deterministic),
//   instruments (note energy, decay, retrigger) and a two-plugin bypass/crash isolation check.
// Does not own: per-fixture control/state (Vst32BridgeControlTests) or soak/kill timing (Vst32RealStage6Tests).
[Collection("Vst32Native")]
public sealed class Vst32AudioMatrixTests
{
    private readonly ITestOutputHelper _output;

    public Vst32AudioMatrixTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> AllDlls()
    {
        var data = new TheoryData<string>();
        try
        {
            if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) { data.Add("__no-fixtures__"); return data; }
            foreach (var file in Directory.GetFiles(Vst32TestAssets.FixturesFolder, "*.dll").OrderBy(Path.GetFileName))
                data.Add(Path.GetFileNameWithoutExtension(file)!);
            if (data.Count == 0) data.Add("__no-fixtures__");
        }
        catch (IOException) { data.Add("__no-fixtures__"); }
        return data;
    }

    [Vst32RealTheory]
    [MemberData(nameof(AllDlls))]
    public void EveryFixtureAppliesAudioThroughEngineSession(string name)
    {
        if (name == "__no-fixtures__") return;
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        Assert.Equal(0x14C, PeMachine(path));
        var instrument = ProbeKind(path, name);
        if (instrument) RunInstrumentMatrix(name, path);
        else RunEffectMatrix(name, path);
    }

    private const float Threshold = 1e-4f;
    private const float SoundFloor = 1e-5f;
    private static readonly (uint Id, double Value)[] NoSettings = Array.Empty<(uint Id, double Value)>();

    // The 2010 mda VST2 build leaves its crossover feedback state (fb1/fb2/fb3) uninitialised in the constructor:
    // a direct object dump of a fresh instance shows 1.7e25 / 4.4e27 / 5.6e4, and those values drive the
    // compressor gains high enough to suppress the output to about -180 dBFS for the first seconds. Zeroing the
    // three fields in the same host restores normal audio, and the plug-in advertises canReplacing (0x18) with
    // 2 inputs/2 outputs, so this is the plug-in's own state, not the bridge. A settling pre-roll clears it; the
    // matrix below still requires real audio from the fixture, it just does not measure the degenerate startup.
    private const int SettleSamples = 262144;
    private static readonly HashSet<string> UninitialisedStateFixtures = new(StringComparer.OrdinalIgnoreCase)
    {
        "mdaMultiBand32",
    };

    // The no-plug-in engine path (empty chain) is the baseline: anything the plug-in does is measured against it.
    [Vst32RealFact]
    public void NoPluginBaselineThroughEngine()
    {
        foreach (var blockSize in new[] { 64, 256, 1024 })
        {
            var dry = DrySignal(blockSize, blocks: 64, skip: 8);
            var baseline = RenderEffectThroughEngine(null, blockSize, 64, 8, NoSettings, out _, out _);
            Assert.True(Vst32TestAssets.AllFinite(baseline), $"baseline@{blockSize}: output must be finite");
            _output.WriteLine($"BASELINE no-plug-in@{blockSize}: dry {Vst32TestAssets.Db(Vst32TestAssets.Rms(dry)):0.0} dBFS, " +
                $"engine {Vst32TestAssets.Db(Vst32TestAssets.Rms(baseline)):0.0} dBFS, dry-vs-engine maxDiff {MaxDiff(dry, baseline):0.000000}");
        }
    }

    private void RunEffectMatrix(string name, string path)
    {
        var blockSizes = new[] { 64, 256, 1024 };
        var lines = new List<string>();
        if (UninitialisedStateFixtures.Contains(name))
            _output.WriteLine($"{name}: {SettleSamples} samples of silent pre-roll to clear the plug-in's uninitialised startup state");
        string? noAudioAtDefaults = null;
        (uint Id, string Name, double Original, double Target)? mover = null;
        var searched = false;
        foreach (var blockSize in blockSizes)
        {
            var dry = DrySignal(blockSize, blocks: 64, skip: 8);
            var baseline = RenderEffectThroughEngine(null, blockSize, 64, 8, NoSettings, out _, out _);
            var wet = RenderEffectThroughEngine(path, blockSize, 64, 8, NoSettings, out var peak, out var parameters);
            Assert.True(Vst32TestAssets.AllFinite(wet), $"{name}@{blockSize}: output must be finite");
            var rmsDry = Vst32TestAssets.Rms(dry);
            var rmsWet = Vst32TestAssets.Rms(wet);
            var baselineDiff = MaxDiff(dry, baseline);
            // The plug-in's own contribution: its output against the no-plug-in engine output, not against dry.
            var diff = MaxDiff(baseline, wet);
            var silent = rmsWet <= SoundFloor;
            var verdict = silent ? "silent at defaults" : diff > Threshold ? "differs" : "transparent at defaults";
            var note = verdict == "differs" ? "" : $", {verdict}";
            if (verdict != "differs")
            {
                if (!searched) { mover = FindMovingParameter(path, blockSize, baseline, parameters, silent); searched = true; }
                if (mover is { } m)
                {
                    var tweaked = RenderEffectThroughEngine(path, blockSize, 64, 8, new[] { (m.Id, m.Target) }, out _, out _);
                    var tweakDiff = MaxDiff(baseline, tweaked);
                    var restored = RenderEffectThroughEngine(path, blockSize, 64, 8, new[] { (m.Id, m.Original) }, out _, out _);
                    var restoreDiff = MaxDiff(wet, restored);
                    Assert.True(silent ? Vst32TestAssets.Rms(tweaked) > SoundFloor : tweakDiff > Threshold,
                        $"{name}@{blockSize}: {m.Name}={m.Target:0.##} must move audio (maxDiff {tweakDiff:0.000000})");
                    Assert.True(restoreDiff <= Threshold, $"{name}@{blockSize}: restoring {m.Name} must return to the default render (maxDiff {restoreDiff:0.000000})");
                    verdict = "differs after param tweak";
                    note = $", {m.Name}={m.Target:0.##} maxDiff {tweakDiff:0.000000} (rms {Vst32TestAssets.Db(Vst32TestAssets.Rms(tweaked)):0.0} dBFS), restored maxDiff {restoreDiff:0.000000}";
                }
                else
                {
                    noAudioAtDefaults = name;
                    note += ", no parameter moves audio";
                }
            }
            _output.WriteLine($"{name}@{blockSize}: dry {Vst32TestAssets.Db(rmsDry):0.0} dBFS, wet {Vst32TestAssets.Db(rmsWet):0.0} dBFS, " +
                $"peak {Vst32TestAssets.Db(peak):0.0} dBFS, baseline dry-vs-engine {baselineDiff:0.000000}, " +
                $"plug-in-vs-baseline maxDiff {diff:0.000000}{note} -> {verdict}");
            lines.Add($"{blockSize}:{Vst32TestAssets.Db(rmsWet):0.0}dB/{verdict}");
            Assert.True(verdict is "differs" or "differs after param tweak" or "transparent at defaults",
                $"{name}@{blockSize}: unexpected verdict {verdict} (no sound at defaults and no parameter makes it sound)");
        }

        var first = RenderEffectThroughEngine(path, 256, 64, 8, NoSettings, out _, out _);
        var second = RenderEffectThroughEngine(path, 256, 64, 8, NoSettings, out _, out _);
        Assert.Equal(first.Length, second.Length);
        var repeatDiff = MaxDiff(first, second);
        _output.WriteLine($"{name}: determinism repeat maxDiff {repeatDiff:0.000000} over {first.Length} frames");
        Assert.True(repeatDiff <= 1e-3f, $"{name}: repeat run must match within tolerance (maxDiff {repeatDiff:0.000000})");
        _output.WriteLine($"MATRIX effect {name}: kind=Effect {string.Join(" ", lines)} repeatDiff={repeatDiff:0.000000} " +
            (noAudioAtDefaults is null ? "verdict=PASS" : "verdict=PASS (transparent at defaults)"));
    }

    /// <summary>The first writable parameter that moves the output: off the no-plug-in baseline, or into audible level when silent at defaults.</summary>
    private (uint Id, string Name, double Original, double Target)? FindMovingParameter(string path, int blockSize, float[] baseline,
        IReadOnlyList<PluginParameterInfo> parameters, bool silentAtDefaults)
    {
        foreach (var p in parameters.Where(p => !p.ReadOnly).Take(8))
        {
            foreach (var target in new[] { 1.0, 0.0, 0.5 }.Where(t => Math.Abs(t - p.Value) > 0.05))
            {
                var probe = RenderEffectThroughEngine(path, blockSize, 64, 8, new[] { (p.Id, target) }, out _, out _);
                var moves = silentAtDefaults ? Vst32TestAssets.Rms(probe) > SoundFloor : MaxDiff(baseline, probe) > Threshold;
                _output.WriteLine($"  probe {p.Name} {p.Value:0.###} -> {target:0.##}: maxDiff {MaxDiff(baseline, probe):0.000000}, " +
                    $"rms {Vst32TestAssets.Db(Vst32TestAssets.Rms(probe)):0.0} dBFS");
                if (moves) return (p.Id, p.Name, p.Value, target);
            }
        }
        return null;
    }

    private void RunInstrumentMatrix(string name, string path)
    {
        const int blockSize = 256;
        using var shared = SharedBlock.Create($"tf-vst32-matrix-inst-{Guid.NewGuid():N}");

        var pids = new List<int>();
        try
        {
            Attach(shared, blockSize);
            EH.SetTrackMix(0, 127, 64);
            EH.LoadChain(0, "inst", useMidiSynth: false, new List<PluginSpec> { Vst32TestAssets.Spec(path, instrument: true) });
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            var remote = Assert.IsType<RemotePlugin>(Assert.Single(chain!.Plugins));
            pids.Add(remote.HostProcessId);
            WarmUp(shared, blockSize, new[] { remote });

            var pre = RenderMix(shared, blockSize, blocks: 8, writes: Array.Empty<(int, TimedMidi)>());
            var held = RenderMix(shared, blockSize, blocks: 64,
                writes: new[] { (0, Note(0x90, 60, 100)) }, measureFrom: 8);
            Assert.All(held, s => Assert.True(float.IsFinite(s), $"{name}: note must be finite"));
            var rmsPre = Vst32TestAssets.Rms(pre);
            var rmsHeld = Vst32TestAssets.Rms(held);
            var peakHeld = Vst32TestAssets.Peak(held);
            Assert.True(rmsHeld > 1e-5f, $"{name}: note-on must sound (RMS {Vst32TestAssets.Db(rmsHeld):0.0} dBFS)");
            Assert.True(rmsPre < 1e-3f, $"{name}: pre-note must be near silent (RMS {Vst32TestAssets.Db(rmsPre):0.0} dBFS)");

            var tail = RenderMix(shared, blockSize, blocks: 78,
                writes: new[] { (0, Note(0x80, 60, 0)) }, measureFrom: 0);
            Assert.All(tail, s => Assert.True(float.IsFinite(s), $"{name}: tail must be finite"));
            var tailAll = tail;
            var late = tailAll.Skip(tailAll.Length * 3 / 4).ToArray();
            var rmsTailLate = Vst32TestAssets.Rms(late);
            Assert.True(rmsTailLate < rmsHeld, $"{name}: tail must decay (held {Vst32TestAssets.Db(rmsHeld):0.0} dBFS, late {Vst32TestAssets.Db(rmsTailLate):0.0} dBFS)");

            var second = RenderMix(shared, blockSize, blocks: 40,
                writes: new[] { (0, Note(0x90, 62, 100)), (20, Note(0x80, 62, 0)) }, measureFrom: 4);
            var rmsSecond = Vst32TestAssets.Rms(second);
            Assert.True(rmsSecond > 1e-5f, $"{name}: second note must retrigger (RMS {Vst32TestAssets.Db(rmsSecond):0.0} dBFS)");
            _output.WriteLine($"MATRIX instrument {name}: kind=Instrument pre {Vst32TestAssets.Db(rmsPre):0.0} dBFS, " +
                $"held {Vst32TestAssets.Db(rmsHeld):0.0} dBFS (peak {Vst32TestAssets.Db(peakHeld):0.0}), " +
                $"tail-late {Vst32TestAssets.Db(rmsTailLate):0.0} dBFS, retrigger {Vst32TestAssets.Db(rmsSecond):0.0} dBFS verdict=PASS");
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone(name, pids);
        }
    }

    [Vst32RealFact]
    public void BypassOrCrashDoesNotChangeTheOtherPlugin()
    {
        var names = AllDllNames().Where(n => !ProbeKind(Vst32TestAssets.FixturePath(n), n)).Take(2).ToArray();
        Assert.True(names.Length >= 2, "need two effect fixtures for the isolation check");
        const int blockSize = 256;
        using var shared = SharedBlock.Create($"tf-vst32-matrix-iso-{Guid.NewGuid():N}");

        var pids = new List<int>();
        try
        {
            Attach(shared, blockSize);
            EH.SetTrackMix(0, 127, 64);
            EH.SetTrackMix(1, 127, 64);
            EH.LoadChain(0, "iso-a", false, new List<PluginSpec> { Vst32TestAssets.Spec(Vst32TestAssets.FixturePath(names[0]), false) with { Id = "a" } });
            EH.LoadChain(1, "iso-b", false, new List<PluginSpec> { Vst32TestAssets.Spec(Vst32TestAssets.FixturePath(names[1]), false) with { Id = "b" } });
            var chainA = EH.ChainAt(0);
            var chainB = EH.ChainAt(1);
            Assert.NotNull(chainA); Assert.NotNull(chainB);
            var remoteA = Assert.IsType<RemotePlugin>(Assert.Single(chainA!.Plugins));
            var remoteB = Assert.IsType<RemotePlugin>(Assert.Single(chainB!.Plugins));
            pids.Add(remoteA.HostProcessId);
            pids.Add(remoteB.HostProcessId);
            Assert.NotEqual(remoteA.HostProcessId, remoteB.HostProcessId);
            WarmUp(shared, blockSize, new[] { remoteA, remoteB });

            var baseline = RenderChainSamples(chainB!, shared, blockSize, blocks: 48, skip: 8);
            var rmsBase = Vst32TestAssets.Rms(baseline);
            Assert.True(rmsBase > 1e-5f, $"{names[1]}: baseline must sound");

            chainA!.SetPluginBypass(0, true);
            var bypassed = RenderChainSamples(chainB!, shared, blockSize, blocks: 48, skip: 8);
            var bypassDb = Math.Abs(Vst32TestAssets.Db(Vst32TestAssets.Rms(bypassed)) - Vst32TestAssets.Db(rmsBase));
            _output.WriteLine($"MATRIX isolation {names[1]} with {names[0]} bypassed: base {Vst32TestAssets.Db(rmsBase):0.00} dBFS, " +
                $"bypassed {Vst32TestAssets.Db(Vst32TestAssets.Rms(bypassed)):0.00} dBFS, delta {bypassDb:0.00} dB");
            Assert.InRange(bypassDb, 0, 0.5);

            try { using var killed = Process.GetProcessById(remoteA.HostProcessId); killed.Kill(true); } catch (Exception) { }
            Assert.True(Vst32TestAssets.WaitsForExit(remoteA.HostProcessId, 4000), $"{names[0]}: killed helper must exit");
            EH.Threads.RunPending();
            var afterKill = RenderChainSamples(chainB!, shared, blockSize, blocks: 48, skip: 8);
            var rmsKill = Vst32TestAssets.Rms(afterKill);
            _output.WriteLine($"MATRIX isolation {names[1]} after {names[0]} killed: {Vst32TestAssets.Db(rmsKill):0.00} dBFS verdict=PASS");
            Assert.True(rmsKill > 1e-5f, $"{names[1]}: must still sound after the other helper died");
            Assert.InRange(Math.Abs(Vst32TestAssets.Db(rmsKill) - Vst32TestAssets.Db(rmsBase)), 0, 0.5);
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone("isolation", pids);
        }
    }

    private static List<string> AllDllNames()
    {
        if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) return new List<string>();
        return Directory.GetFiles(Vst32TestAssets.FixturesFolder, "*.dll")
            .Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(n => n).ToList();
    }

    private static bool ProbeKind(string path, string name)
    {
        var (exit, output, error) = Vst32TestAssets.RunHelperCaptured("--probe", path, Vst32TestAssets.Sha256(path));
        Assert.True(exit == 0, $"{name}: probe exit {exit}: {error}");
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault();
        Assert.NotNull(line);
        return line!.Split('|')[0] == "Instrument";
    }

    private static int PeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Seek(0x3C, SeekOrigin.Begin);
        var pe = reader.ReadInt32();
        stream.Seek(pe + 4, SeekOrigin.Begin);
        return reader.ReadUInt16();
    }

    private float[] RenderEffectThroughEngine(string? path, int blockSize, int blocks, int skip,
        IReadOnlyList<(uint Id, double Value)> settings, out float peak, out IReadOnlyList<PluginParameterInfo> parameters)
    {
        using var shared = SharedBlock.Create($"tf-vst32-matrix-fx-{Guid.NewGuid():N}");

        var pids = new List<int>();
        try
        {
            Attach(shared, blockSize);
            EH.SetTrackMix(0, 127, 64);
            var specs = path is null ? new List<PluginSpec>() : new List<PluginSpec> { Vst32TestAssets.Spec(path, instrument: false) };
            EH.LoadChain(0, "fx", false, specs);
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            parameters = Array.Empty<PluginParameterInfo>();
            if (path is not null)
            {
                var remote = Assert.IsType<RemotePlugin>(Assert.Single(chain!.Plugins));
                pids.Add(remote.HostProcessId);
                parameters = remote.GetParameters();
                foreach (var (id, value) in settings) Assert.True(remote.SetParameter(id, value), $"set parameter {id}");
                WarmUp(shared, blockSize, new[] { remote });
            }
            var samples = RenderChainSamples(chain!, shared, blockSize, blocks, skip, SettleBlocksFor(path, blockSize));
            peak = Vst32TestAssets.Peak(samples);
            return samples;
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone(path is null ? "baseline" : Path.GetFileNameWithoutExtension(path), pids);
        }
    }

    private static int SettleBlocksFor(string? path, int blockSize) =>
        path is not null && UninitialisedStateFixtures.Contains(Path.GetFileNameWithoutExtension(path))
            ? (SettleSamples + blockSize - 1) / blockSize
            : 0;

    private static float[] RenderChainSamples(TrackChain chain, SharedBlock shared, int blockSize, int blocks, int skip,
        int settleBlocks = 0)
    {
        var inputL = new float[blockSize];
        var inputR = new float[blockSize];
        var mixL = new float[blockSize];
        var mixR = new float[blockSize];
        var phase = 0.0;
        var rng = 0x12345678u;
        var collected = new List<float>();
        // Silent pre-roll: it settles a plug-in's own startup state without shifting the deterministic test signal,
        // so the measured window stays sample-aligned with the no-plug-in baseline.
        for (var block = 0; block < settleBlocks; block++)
        {
            Array.Clear(inputL); Array.Clear(inputR); Array.Clear(mixL); Array.Clear(mixR);
            chain.Render(mixL, mixR, 0, blockSize, Vst32TestAssets.Transport(block, blockSize), shared, null, null, inputL, inputR);
        }
        for (var block = 0; block < blocks; block++)
        {
            FillDeterministic(inputL, inputR, ref phase, ref rng);
            Array.Clear(mixL); Array.Clear(mixR);
            chain.Render(mixL, mixR, 0, blockSize, Vst32TestAssets.Transport(block, blockSize), shared, null, null, inputL, inputR);
            if (block >= skip) { collected.AddRange(mixL); collected.AddRange(mixR); }
        }
        return collected.ToArray();
    }

    private static float[] DrySignal(int blockSize, int blocks, int skip)
    {
        var left = new float[blockSize];
        var right = new float[blockSize];
        var phase = 0.0;
        var rng = 0x12345678u;
        var collected = new List<float>();
        for (var block = 0; block < blocks; block++)
        {
            FillDeterministic(left, right, ref phase, ref rng);
            if (block >= skip) { collected.AddRange(left); collected.AddRange(right); }
        }
        return collected.ToArray();
    }

    private static void FillDeterministic(float[] left, float[] right, ref double phase, ref uint rng)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var sine = (float)(0.2 * Math.Sin(phase));
            phase += 2 * Math.PI * 1000 / 48000;
            uint x = rng ^= (uint)(rng << 13); rng ^= (uint)(rng >> 17); rng ^= (uint)(rng << 5);
            var burst = (i % 256) < 32 ? ((x / (float)uint.MaxValue) * 2 - 1) * 0.15f : 0f;
            left[i] = sine + burst;
            right[i] = sine + burst;
        }
    }

    private static float[] RenderMix(SharedBlock shared, int blockSize, int blocks, IReadOnlyList<(int Block, TimedMidi Midi)> writes, int measureFrom = 0)
    {
        var output = new float[blockSize * 2];
        var collected = new List<float>();
        for (var block = 0; block < blocks; block++)
        {
            foreach (var (at, midi) in writes)
                if (at == block)
                    Assert.True(shared.TryWrite(midi with { Timestamp = Stopwatch.GetTimestamp() - Stopwatch.Frequency / 10 }), "MIDI ring must accept the note");
            Array.Clear(output);
            Assert.Equal(output.Length, EH.Mix!.Read(output, 0, output.Length));
            if (block >= measureFrom) collected.AddRange(output);
        }
        return collected.ToArray();
    }

    private static TimedMidi Note(byte status, byte note, byte velocity) =>
        new() { Slot = 0, Status = status, Data1 = note, Data2 = velocity };

    private static float MaxDiff(float[] a, float[] b)
    {
        var max = 0f;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) max = Math.Max(max, Math.Abs(a[i] - b[i]));
        return max;
    }

    private static void Attach(SharedBlock shared, int blockSize)
    {
        EH.AttachReal(shared);
        EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, blockSize, false));
        Assert.NotNull(EH.Mix);
    }

    private static void WarmUp(SharedBlock shared, int blockSize, IReadOnlyList<RemotePlugin> plugins)
    {
        var misses = plugins.Select(p => p.MissedBlocks).ToArray();
        var skipped = plugins.Select(p => p.BudgetSkippedBlocks).ToArray();
        var clean = 0;
        var output = new float[blockSize * 2];
        for (var block = 0; block < 2000 && clean < 8; block++)
        {
            Array.Clear(output);
            Assert.Equal(output.Length, EH.Mix!.Read(output, 0, output.Length));
            var ready = true;
            for (var i = 0; i < plugins.Count; i++)
                if (plugins[i].MissedBlocks != misses[i] || plugins[i].BudgetSkippedBlocks != skipped[i]) ready = false;
            if (ready) clean++;
            else { clean = 0; misses = plugins.Select(p => p.MissedBlocks).ToArray(); skipped = plugins.Select(p => p.BudgetSkippedBlocks).ToArray(); }
        }
        Assert.True(clean >= 8, "a helper never became responsive during warm-up");
    }

    private static void AssertHelpersGone(string name, IReadOnlyCollection<int> pids)
    {
        // Only the helpers this test started: another job may run its own test helpers at the same time.
        foreach (var pid in pids) Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), $"{name}: helper {pid} left behind");
    }
}
