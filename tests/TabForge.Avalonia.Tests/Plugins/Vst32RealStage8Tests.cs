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

// Owns: stage 8 end-to-end checks through the real EngineSession chain path (spec -> route -> helper): an instrument clip rendered
//        by the engine, a two-effect insert chain against a hand-run sequence, and three bridged instruments at once with no cross-talk.
// Does not own: the per-fixture matrix through RemotePlugin (Vst32RealPluginTests, Vst32BridgeControlTests, Vst32RealStage6Tests).
[Collection("Vst32Native")]
public sealed class Vst32RealStage8Tests
{
    private const int Frames = 256;
    private readonly ITestOutputHelper _output;

    public Vst32RealStage8Tests(ITestOutputHelper output) => _output = output;

    [Vst32RealTheory]
    [InlineData("TAL-NoiseMaker32")]
    [InlineData("TAL-BassLine-101-32")]
    [InlineData("Dexed32")]
    public void ARealInstrumentClipRendersThroughTheEngineSession(string name)
    {
        var path = Fixture(name);

        using var shared = SharedBlock.Create($"tf-vst32-s8-clip-{Guid.NewGuid():N}");
        var pids = new List<int>();
        try
        {
            Attach(shared);
            EH.SetTrackMix(0, 127, 64);
            EH.LoadChain(0, "inst", useMidiSynth: false, new List<PluginSpec> { Vst32TestAssets.Spec(path, instrument: true) });
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            var remote = Assert.IsType<RemotePlugin>(Assert.Single(chain!.Plugins));
            Assert.True(remote.IsInstrument, $"{name}: the chain slot must be the instrument");
            pids.Add(remote.HostProcessId);
            WarmUp(shared, new[] { remote });

            // A small one-note clip: 8 warm-up blocks (the helper's audio thread starts asynchronously), note on at block 8,
            // note off at block 72 (64 x 256 frames = 341 ms of note, as the TAL fixtures are settled in the stage-6 tests).
            var clip = new[] { new TestNote(8, 0x90, 60, 100), new TestNote(72, 0x80, 60, 0) };
            var rendered = Render(shared, clip, blocks: 88, measureFrom: 16, measureTo: 72);
            _output.WriteLine($"{name}: engine-session clip RMS {Vst32TestAssets.Db((float)rendered.Rms):0.0} dBFS over {rendered.MeasuredFrames} frames");
            Assert.True(rendered.Rms > 1e-5, $"{name}: the engine-session clip must be non-silent (RMS {rendered.Rms:0.000000})");
            Assert.True(rendered.Peak > 1e-4, $"{name}: the engine-session clip must have a peak");
            Assert.All(rendered.Samples, sample => Assert.True(float.IsFinite(sample)));
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone(name, pids);
        }
    }

    [Vst32RealFact]
    public void TwoBridgedEffectsInSeriesMatchASequentialPair()
    {
        using var shared = SharedBlock.Create($"tf-vst32-s8-fx-{Guid.NewGuid():N}");

        var chainPids = new List<int>();
        var manualPids = new List<int>();
        try
        {
            Attach(shared);
            EH.SetTrackMix(0, 127, 64);
            EH.LoadChain(0, "fx", useMidiSynth: false, new List<PluginSpec>
            {
                Vst32TestAssets.Spec(Fixture("PurestGain32"), instrument: false),
                Vst32TestAssets.Spec(Fixture("Density32"), instrument: false),
            });
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            Assert.Equal(2, chain!.Plugins.Count);
            chainPids.AddRange(chain.Plugins.OfType<RemotePlugin>().Select(p => p.HostProcessId));
            Assert.Equal(2, chainPids.Distinct().Count());
            WarmUp(shared, chain.Plugins.OfType<RemotePlugin>().ToArray());

            var chained = RenderChain(chain, shared, blocks: 64, skip: 8);

            using var first = Vst32TestAssets.Start(Fixture("PurestGain32"), instrument: false, Frames);
            using var second = Vst32TestAssets.Start(Fixture("Density32"), instrument: false, Frames);
            manualPids.Add(first.HostProcessId);
            manualPids.Add(second.HostProcessId);
            var manual = RenderPair(first, second, blocks: 64, skip: 8);

            var chainDb = Vst32TestAssets.Db((float)chained.Rms);
            var manualDb = Vst32TestAssets.Db((float)manual.Rms);
            _output.WriteLine($"serial insert vs standalone pair: chain {chainDb:0.00} dBFS, pair {manualDb:0.00} dBFS, " +
                              $"difference {Math.Abs(chainDb - manualDb):0.00} dB");
            Assert.True(chained.Rms > 1e-5, "the serial insert must pass audio");
            Assert.True(manual.Rms > 1e-5, "the standalone pair must pass audio");
            Assert.InRange(Math.Abs(chainDb - manualDb), 0, 0.5);
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone("fx pair", chainPids.Concat(manualPids).ToList());
        }
    }

    [Vst32RealFact]
    public void ThreeBridgedInstrumentsRenderTogetherWithoutCrossTalk()
    {

        using var shared = SharedBlock.Create($"tf-vst32-s8-multi-{Guid.NewGuid():N}");
        var pids = new List<int>();
        try
        {
            Attach(shared);
            for (var slot = 0; slot < 3; slot++) EH.SetTrackMix(slot, 127, 64);
            EH.LoadChain(0, "dex-a", false, new List<PluginSpec> { Spec("Dexed32", true, "dex-a") });
            EH.LoadChain(1, "dex-b", false, new List<PluginSpec> { Spec("Dexed32", true, "dex-b") });
            EH.LoadChain(2, "tal", false, new List<PluginSpec> { Spec("TAL-NoiseMaker32", true, "tal") });

            var chains = new[] { 0, 1, 2 }.Select(slot => EH.ChainAt(slot)).ToArray();
            Assert.All(chains, chain => Assert.NotNull(chain));
            var plugins = chains.Select(chain => Assert.IsType<RemotePlugin>(Assert.Single(chain!.Plugins))).ToArray();
            pids.AddRange(plugins.Select(p => p.HostProcessId));
            Assert.Equal(3, pids.Distinct().Count());
            Assert.NotSame(plugins[0], plugins[1]);
            Assert.Equal("Dexed32", Path.GetFileNameWithoutExtension(plugins[0].Path));
            Assert.Equal("Dexed32", Path.GetFileNameWithoutExtension(plugins[1].Path));
            Assert.Equal("TAL-NoiseMaker32", Path.GetFileNameWithoutExtension(plugins[2].Path));

            // Two copies of one instrument, set to different patches: the low-cutoff copy must stay quiet while the other sounds.
            Assert.True(plugins[0].SetParameter(0, 0.0), "Dexed A cutoff");
            Assert.True(plugins[1].SetParameter(0, 1.0), "Dexed B cutoff");
            var talVolume = plugins[2].GetParameters().First(p => p.Name.Equals("volume", StringComparison.OrdinalIgnoreCase));
            Assert.True(plugins[2].SetParameter(talVolume.Id, 1.0), "TAL volume");
            WarmUp(shared, plugins);

            var first = RenderThree(shared, blocks: 48);
            _output.WriteLine($"three instruments, phase 1 peaks: dexA {Vst32TestAssets.Db((float)first[0]):0.0} dB, " +
                              $"dexB {Vst32TestAssets.Db((float)first[1]):0.0} dB, TAL {Vst32TestAssets.Db((float)first[2]):0.0} dB");
            Assert.True(first[1] > 1e-4, "Dexed B must sound");
            Assert.True(first[2] > 1e-4, "the TAL instrument must sound");
            Assert.True(first[0] < first[1] * 0.1, $"Dexed A's low cutoff must stay well below Dexed B ({first[0]:0.000000} vs {first[1]:0.000000})");
            Assert.InRange(Cutoff(plugins[0]), 0.0, 0.05);
            Assert.InRange(Cutoff(plugins[1]), 0.95, 1.0);

            // Restore Dexed A only: its own level comes back while Dexed B and the TAL keep exactly their own level and state.
            Assert.True(plugins[0].SetParameter(0, 1.0), "Dexed A cutoff restore");
            var second = RenderThree(shared, blocks: 48);
            var recoveredDb = Vst32TestAssets.Db((float)second[0]);
            var unchangedB = Math.Abs(Vst32TestAssets.Db((float)second[1]) - Vst32TestAssets.Db((float)first[1]));
            var unchangedTal = Math.Abs(Vst32TestAssets.Db((float)second[2]) - Vst32TestAssets.Db((float)first[2]));
            _output.WriteLine($"three instruments, phase 2 peaks: dexA {recoveredDb:0.0} dB, dexB {Vst32TestAssets.Db((float)second[1]):0.0} dB, " +
                              $"TAL {Vst32TestAssets.Db((float)second[2]):0.0} dB; unchanged dexB {unchangedB:0.00} dB, TAL {unchangedTal:0.00} dB");
            Assert.True(second[0] > first[0] * 10, $"Dexed A must recover its own level ({second[0]:0.000000} vs {first[0]:0.000000})");
            Assert.InRange(unchangedB, 0, 1.0);
            Assert.InRange(unchangedTal, 0, 1.0);
            Assert.InRange(Cutoff(plugins[0]), 0.95, 1.0);
            Assert.InRange(Cutoff(plugins[1]), 0.95, 1.0);
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone("three instruments", pids);
        }
    }

    private static void Attach(SharedBlock shared)
    {
        EH.AttachReal(shared);
        EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, Frames, false));
        Assert.NotNull(EH.Mix);
    }

    private static string Fixture(string name)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        return path;
    }

    private static PluginSpec Spec(string name, bool instrument, string id) =>
        Vst32TestAssets.Spec(Fixture(name), instrument) with { Id = id };

    private static double Cutoff(RemotePlugin plugin) =>
        plugin.GetParameters().First(p => p.Id == 0).Value;

    private static (double Rms, float Peak, int MeasuredFrames, float[] Samples) Render(SharedBlock shared, TestNote[] clip, int blocks, int measureFrom, int measureTo)
    {
        var output = new float[Frames * 2];
        var measured = new List<float>();
        for (var block = 0; block < blocks; block++)
        {
            foreach (var note in clip.Where(n => n.Block == block)) Write(shared, 0, note);
            Array.Clear(output);
            var read = EH.Mix!.Read(output, 0, output.Length);
            Assert.Equal(output.Length, read);
            if (block >= measureFrom && block < measureTo) measured.AddRange(output);
        }
        var samples = measured.ToArray();
        var rms = samples.Length == 0 ? 0 : Math.Sqrt(samples.Sum(s => (double)s * s) / samples.Length);
        var peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
        return (rms, peak, samples.Length, samples);
    }

    private static void Write(SharedBlock shared, int slot, TestNote note) =>
        Assert.True(shared.TryWrite(new TimedMidi
        {
            Timestamp = Stopwatch.GetTimestamp() - Stopwatch.Frequency / 10,   // due now, at frame 0 of the next block
            Slot = slot, Status = note.Status, Data1 = note.Note, Data2 = note.Velocity,
        }), "the shared MIDI ring must accept the clip note");

    /// <summary>Runs silent engine blocks until every helper has answered eight in a row (bounded, no sleeps).</summary>
    private static void WarmUp(SharedBlock shared, IReadOnlyList<RemotePlugin> plugins)
    {
        var misses = plugins.Select(p => p.MissedBlocks).ToArray();
        var skipped = plugins.Select(p => p.BudgetSkippedBlocks).ToArray();
        var clean = 0;
        var output = new float[Frames * 2];
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

    private static (double Rms, float Peak) RenderChain(TrackChain chain, SharedBlock shared, int blocks, int skip)
    {
        var inputL = new float[Frames];
        var inputR = new float[Frames];
        var mixL = new float[Frames];
        var mixR = new float[Frames];
        var phase = 0.0;
        var samples = new List<float>();
        for (var block = 0; block < blocks; block++)
        {
            FillSine(inputL, inputR, ref phase);
            Array.Clear(mixL); Array.Clear(mixR);
            chain.Render(mixL, mixR, 0, Frames, Vst32TestAssets.Transport(block, Frames), shared, monitor: null, peers: null, busL: inputL, busR: inputR);
            if (block >= skip) samples.AddRange(mixL.Concat(mixR));
        }
        var all = samples.ToArray();
        var rms = all.Length == 0 ? 0 : Math.Sqrt(all.Sum(s => (double)s * s) / all.Length);
        return (rms, all.Length == 0 ? 0 : all.Max(Math.Abs));
    }

    private static (double Rms, float Peak) RenderPair(RemotePlugin first, RemotePlugin second, int blocks, int skip)
    {
        var input = new[] { new float[Frames], new float[Frames] };
        var between = new[] { new float[Frames], new float[Frames] };
        var output = new[] { new float[Frames], new float[Frames] };
        WarmUpPair(first, second, input, between, output);
        var phase = 0.0;
        var samples = new List<float>();
        for (var block = 0; block < blocks; block++)
        {
            FillSine(input[0], input[1], ref phase);
            first.Process(input, between, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
            second.Process(between, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
            if (block >= skip) samples.AddRange(output[0].Concat(output[1]));
        }
        var all = samples.ToArray();
        var rms = all.Length == 0 ? 0 : Math.Sqrt(all.Sum(s => (double)s * s) / all.Length);
        return (rms, all.Length == 0 ? 0 : all.Max(Math.Abs));
    }

    /// <summary>One clip phase per track (8 warm-up blocks, note on at 8, off at 40); returns each track's peak over the held note.</summary>
    private static double[] RenderThree(SharedBlock shared, int blocks)
    {
        var peaks = new double[3];
        var output = new float[Frames * 2];
        for (var block = 0; block < blocks; block++)
        {
            if (block is 8 or 40)
                for (var slot = 0; slot < 3; slot++)
                    Write(shared, slot, new TestNote(0, block == 8 ? (byte)0x90 : (byte)0x80, 60, block == 8 ? (byte)100 : (byte)0));
            Array.Clear(output);
            Assert.Equal(output.Length, EH.Mix!.Read(output, 0, output.Length));
            if (block is >= 16 and < 38)
                for (var slot = 0; slot < 3; slot++) peaks[slot] = Math.Max(peaks[slot], shared.Peak(slot));
        }
        return peaks;
    }

    /// <summary>Warms a hand-run pair until both helpers answer eight silent blocks in a row (bounded, no sleeps).</summary>
    private static void WarmUpPair(RemotePlugin first, RemotePlugin second, float[][] input, float[][] between, float[][] output)
    {
        var misses = new[] { first.MissedBlocks, second.MissedBlocks };
        var skipped = new[] { first.BudgetSkippedBlocks, second.BudgetSkippedBlocks };
        var clean = 0;
        for (var block = 0; block < 2000 && clean < 8; block++)
        {
            first.Process(input, between, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
            second.Process(between, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
            var now = new[] { first.MissedBlocks, second.MissedBlocks };
            var nowSkipped = new[] { first.BudgetSkippedBlocks, second.BudgetSkippedBlocks };
            if (now[0] == misses[0] && now[1] == misses[1] && nowSkipped[0] == skipped[0] && nowSkipped[1] == skipped[1]) clean++;
            else { clean = 0; misses = now; skipped = nowSkipped; }
        }
        Assert.True(clean >= 8, "a helper never became responsive during the pair warm-up");
    }

    private static void FillSine(float[] left, float[] right, ref double phase)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var sample = (float)(0.2 * Math.Sin(phase));
            phase += 2 * Math.PI * 1000 / 48000;
            left[i] = sample;
            right[i] = sample;
        }
    }

    /// <summary>Every helper this test started must be gone; other jobs' helpers are never touched or inspected.</summary>
    private static void AssertHelpersGone(string name, IReadOnlyCollection<int> pids)
    {
        foreach (var pid in pids) Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), $"{name}: helper {pid} left behind");
    }

    private sealed record TestNote(int Block, byte Status, byte Note, byte Velocity);
}
