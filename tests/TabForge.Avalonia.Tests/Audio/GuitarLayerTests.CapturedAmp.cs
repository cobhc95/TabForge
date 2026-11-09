using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using TabForge.Sf2Synth.Nam;
using TabForge.Sf2Synth.Sf2;
using Xunit;
using Song = System.Collections.Generic.List<(double At, int Status, int D1, int D2)>;

namespace TabForge.Avalonia.Tests;

// Owns: the checks of the built-in captured amp of the distorted programs: embedded and used only on 29/30 (a user .nam wins),
//   DI trimmed to the capture's training level, stable across input levels, no pop at note start/end, live DistAmp switching
//   without a step or stuck note, level and CPU against the stage amp, and the A/B WAVs tfsynth5_* (TABFORGE_SYNTH_WAV_DIR).
// Does not own: the rest of the layer (GuitarLayerTests.cs), the NAM runtime (NamTests).
// Tests: GuitarRig.Captured, GuitarBus, GuitarLayer.Apply, NamAmp, SynthOptions.DistAmp.
public sealed partial class GuitarLayerTests
{
    private static readonly SynthOptions Stage = SynthOptions.Default with { DistAmp = DistAmp.Stage },
        CleanOnly = SynthOptions.Default with { DistAmp = DistAmp.CleanOnly }, WithCab = SynthOptions.Default with { CapturedAmpCabinet = true };
    private const int RiffFrames = (int)(32 * Eighth * Rate);   // the riff's playing part, without the ring-out

    private static float[] RenderSong(GuitarLayer g, Song song, SynthOptions o, int frames)
    {
        g.Apply(o);
        float[] l = new float[frames], r = new float[frames];
        var at = 0;
        foreach (var e in song.OrderBy(e => e.At).ThenBy(e => (e.Status & 0xF0) == 0x80 ? 0 : 1).Append((At: frames / (double)Rate, Status: -1, D1: 0, D2: 0)))
        {
            for (var to = Math.Min(frames, (int)(e.At * Rate)); at < to; at += Math.Min(512, to - at))
                g.Render(l.AsSpan(at, Math.Min(512, to - at)), r.AsSpan(at, Math.Min(512, to - at)));
            if (e.Status >= 0) g.ProcessMidi(e.Status & 15, e.Status & 0xF0, e.D1, e.D2);
        }
        return l;
    }

    private static int SongFrames => (int)((32 * Eighth + 2) * Rate);

    // CC7 127 and pan hard left: the left output is the bus output × √2.
    private static Song Dry(Song song) => [(0, 0xB0, 7, 127), (0, 0xB0, 10, 0), .. song];

    private static float[] Bus(int program, SynthOptions o) => RenderSong(new GuitarLayer(Rate), Dry(Riff(0, program)), o, SongFrames).Select(v => v / MathF.Sqrt(2)).ToArray();

    private static (double Rms, double Peak) Level(float[] x, int from, int to)
    {
        var s = x.AsSpan(from, to - from).ToArray();
        return (20 * Math.Log10(Math.Sqrt(s.Average(v => (double)v * v)) + 1e-30), s.Max(MathF.Abs));
    }

    [Fact]
    public void TheCapturedAmpIsEmbeddedAndPlaysOnlyTheDistortedPrograms()
    {
        var m = GuitarRig.Captured;
        Assert.NotNull(m);
        output.WriteLine($"{m.Name}: {m.Architecture}, {m.Rate} Hz, loudness {m.Loudness:F1}");
        Assert.Equal(("Captured amp", "WaveNet", 48000.0), (m.Name, m.Architecture, m.Rate));
        foreach (var program in new[] { 24, 26, 27, 28, 31, 33 })
            Assert.Equal(RenderSong(new GuitarLayer(Rate), Arpeggio(0).Select(e => e.Item2 == 0xC0 ? e with { Item3 = program } : e).ToList(), SynthOptions.Default, Rate),
                         RenderSong(new GuitarLayer(Rate), Arpeggio(0).Select(e => e.Item2 == 0xC0 ? e with { Item3 = program } : e).ToList(), Stage, Rate));
        Assert.NotEqual(RenderSong(new GuitarLayer(Rate), Riff(0), SynthOptions.Default, Rate), RenderSong(new GuitarLayer(Rate), Riff(0), Stage, Rate));

        // A user .nam wins over the choice.
        var nam = Path.Combine(Path.GetTempPath(), "tf-rig-captured-test.nam");
        NamTests.Arr[] arrays = [NamTests.A1(1, 4, 1, true, [1, 2, 4, 8])];
        var (_, n) = NamReference.WaveNet(arrays, new float[10000], new float[1]);
        File.WriteAllText(nam, NamTests.WaveNetJson(arrays, NamTests.Random(n, 3, 0.3f)).ToJsonString());
        float[] User(SynthOptions o) { var g = new GuitarLayer(Rate); g.LoadRig(nam, null); Render(g, 64); return RenderSong(g, Riff(0), o, Rate); }
        Assert.Equal(User(SynthOptions.Default), User(Stage));
    }

    // The training DI was about -29 dBFS rms, peak 0.31: the trimmed DI of the riffs sits there (rms within 2 dB, peak at most 0.4).
    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    public void TheDistortedDiIsTrimmedToTheCaptureTrainingLevel(int program)
    {
        var di = Bus(program, CleanOnly);
        var (rms, peak) = Level(di, 0, RiffFrames);
        var trim = Voicings.Of(program).Rig.Captured!.Value.Trim * Voicings.Of(program).Rig.Drive;
        output.WriteLine($"program {program}: DI before trim rms {rms - 20 * Math.Log10(trim):F1} dBFS, peak {peak / trim:F3}; trimmed rms {rms:F1} dBFS, peak {peak:F3} (trim {trim:F3})");
        Assert.InRange(rms, -31, -27);
        Assert.True(peak <= 0.4f, $"peak {peak}");
    }

    // Input -20..+12 dB around the trimmed riff: finite, peak under 1.5, bounded DC, nothing left after the input stops; silence in = silence out.
    [Fact]
    public void TheCapturedAmpStaysStableAcrossInputLevels()
    {
        var di = Bus(30, CleanOnly);
        var silent = new NamAmp(GuitarRig.Captured!, Rate, Chunk);
        var zeros = new float[Rate];
        Process(silent, zeros);
        output.WriteLine($"silence in: peak out {zeros.Max(MathF.Abs):E1}");
        Assert.True(zeros.Max(MathF.Abs) < 1e-5f);
        for (var db = -20; db <= 12; db += 4)
        {
            var amp = new NamAmp(GuitarRig.Captured!, Rate, Chunk);
            var g = (float)Math.Pow(10, db / 20.0);
            var x = di.Select(v => v * g).Concat(new float[Rate]).ToArray();
            Process(amp, x);
            var play = x.AsSpan(0, RiffFrames).ToArray();
            var rms = Math.Sqrt(play.Average(v => (double)v * v));
            var dc = play.Average(v => (double)v);
            var (tail, _) = Level(x, x.Length - Rate / 2, x.Length);
            output.WriteLine($"{db,3} dB: out rms {20 * Math.Log10(rms):F1} dBFS, peak {play.Max(MathF.Abs):F3}, DC {dc / rms:E1} of rms, last 0.5 s {tail:F0} dBFS");
            Assert.True(x.All(float.IsFinite));
            Assert.True(play.Max(MathF.Abs) < 1.5f);
            Assert.True(Math.Abs(dc) < 0.25 * rms);   // the model's own DC while playing (asymmetric clipping); the bus blocks it
            Assert.True(tail < -100);
        }
    }

    private static void Process(NamAmp amp, float[] x)
    {
        for (var at = 0; at < x.Length; at += Chunk) amp.Process(x.AsSpan(at, Math.Min(Chunk, x.Length - at)));
    }

    // From silence, the first 2 ms after the onset step no more than the playing that follows; the end fades to silence before the bus stops.
    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    public void TheCapturedAmpStartsAndEndsWithoutAPop(int program)
    {
        const int onset = Rate / 10;
        var x = RenderSong(new GuitarLayer(Rate), Riff(0, program).Select(e => e with { Item1 = e.Item1 + 0.1 }).ToList(), SynthOptions.Default, SongFrames + 3 * Rate);
        float Step(int from, int to) => Enumerable.Range(from + 1, to - from - 1).Max(i => MathF.Abs(x[i] - x[i - 1]));
        var last = Array.FindLastIndex(x, v => v != 0);
        var (start, after) = (Step(onset, onset + Rate / 500), Step(onset + Rate / 500, onset + Rate / 20));
        output.WriteLine($"program {program}: before onset {x.Take(onset).Max(MathF.Abs):E1}, step first 2 ms {start:F4} vs next 48 ms {after:F4}; last sound {last / (double)Rate:F2} s at {MathF.Abs(x[last]):E1}");
        Assert.True(x.Take(onset).All(v => v == 0));
        Assert.True(start <= after);
        Assert.True(MathF.Abs(x[last]) < 1e-5f && last < x.Length - Rate);
    }

    // Captured, stage, clean, captured with cabinet, captured: each change fades; no step beyond the playing's own, and the notes still end.
    [Fact]
    public void SwitchingTheDistortedAmpLiveLeavesNoStepAndNoStuckNote()
    {
        var g = Playing(30, 40, 47, 52);
        var modes = new[] { SynthOptions.Default, Stage, CleanOnly, WithCab, SynthOptions.Default };
        const int part = 24 * 512;
        var x = new float[modes.Length * part];
        var r = new float[part];
        for (var i = 0; i < modes.Length; i++)
        {
            g.Apply(modes[i]);
            for (var at = 0; at < part; at += 512) g.Render(x.AsSpan(i * part + at, 512), r.AsSpan(at, 512));
        }
        float Step(int from, int to) => Enumerable.Range(from + 1, to - from - 1).Max(i => MathF.Abs(x[i] - x[i - 1]));
        for (var i = 1; i < modes.Length; i++)
        {
            var (around, before, after) = (Step(i * part - 64, i * part + 512), Step((i - 1) * part + 1024, i * part - 64), Step(i * part + 512, (i + 1) * part));
            output.WriteLine($"{modes[i - 1].DistAmp}{(modes[i - 1].CapturedAmpCabinet ? "+cab" : "")} -> {modes[i].DistAmp}{(modes[i].CapturedAmpCabinet ? "+cab" : "")}: step around {around:F4}, before {before:F4}, after {after:F4}");
            Assert.True(around <= 1.5f * Math.Max(before, after));
        }
        Assert.True(x.All(float.IsFinite));
        foreach (var n in new[] { 40, 47, 52 }) g.ProcessMidi(0, 0x80, n, 0);
        Render(g, 2 * Rate);
        Assert.Equal(0, g.ActiveStrings);
    }

    // Riff level against the stage amp (within 2 dB), CPU per channel, and the A/B listening files.
    [Fact]
    public void TheCapturedAmpIsMeasuredAgainstTheStageAmp()
    {
        foreach (var program in new[] { 29, 30 })
        {
            var captured = Bus(program, SynthOptions.Default);
            var (cap, capPeak) = Level(captured, 0, RiffFrames);
            var dc = captured.Take(RiffFrames).Average(v => (double)v) / Math.Pow(10, cap / 20);
            output.WriteLine($"riff {program}: captured DC {dc:E1} of rms");
            Assert.True(Math.Abs(dc) < 0.01);
            var (stage, stagePeak) = Level(Bus(program, Stage), 0, RiffFrames);
            var (cab, _) = Level(Bus(program, WithCab), 0, RiffFrames);
            output.WriteLine($"riff {program}: captured rms {cap:F1} dBFS (peak {capPeak:F3}), with cabinet {cab:F1}, stage {stage:F1} (peak {stagePeak:F3}), captured - stage {cap - stage:F1} dB");
            Assert.InRange(cap - stage, -2, 2);
        }
        int[] six = [40, 47, 52, 55, 59, 64];
        foreach (var (name, o) in new[] { ("captured", SynthOptions.Default), ("captured + cabinet", WithCab), ("stage", Stage) })
        {
            var g = Playing(30, six);
            g.Apply(o);
            Render(g, Rate / 10);
            var watch = Stopwatch.StartNew();
            Render(g, 4 * Rate);
            output.WriteLine($"program 30, six strings, {name}: {watch.Elapsed.TotalMilliseconds / 40:F2} % of one core");
        }
        foreach (var (file, program, o) in new[] { ("riff30_captured", 30, SynthOptions.Default), ("riff30_stage", 30, Stage), ("riff29_captured", 29, SynthOptions.Default),
                     ("riff29_stage", 29, Stage), ("riff30_cleanonly", 30, CleanOnly) })
        {
            var s = new Synthesizer(new SoundFont(TestSoundFont.Build()), Rate, 64, effects: true) { Layer = new GuitarLayer(Rate), MasterVolume = 0.5f, Options = o };
            float[] l = new float[SongFrames], r = new float[SongFrames];
            var at = 0;
            foreach (var e in Riff(0, program).OrderBy(e => e.Item1).ThenBy(e => (e.Item2 & 0xF0) == 0x80 ? 0 : 1))
            {
                var f = Math.Min(SongFrames, (int)(e.Item1 * Rate));
                if (f > at) { s.Render(l.AsSpan(at, f - at), r.AsSpan(at, f - at)); at = f; }
                s.ProcessMidiMessage(e.Item2 & 15, e.Item2 & 0xF0, e.Item3, e.Item4);
            }
            s.Render(l.AsSpan(at), r.AsSpan(at));
            TestSoundFont.WriteWav($"tfsynth5_{file}.wav", l, r, Rate);
        }
    }

    private const int Chunk = GuitarLayer.Chunk;
}
