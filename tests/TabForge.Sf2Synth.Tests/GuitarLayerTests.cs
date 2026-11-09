using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using TabForge.Sf2Synth.Sf2;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Sf2Synth.Tests;

// Owns: the checks of the modelled guitar/bass layer: finite, audible, unclipped output for every modelled family, pitch within a few
//   cents (basses within 1 cent down to B0, no DC), no aliasing in the slap, no allocation, CPU per string and per channel, routing inside Synthesizer, and the
//   listening WAVs (TABFORGE_SYNTH_WAV_DIR).
// Does not own: the SoundFont player (Sf2SynthTests).
// Tests: GuitarLayer, PluckedString, GuitarBus, StageAmp, BodyModes, Compressor, Oversampler2x, CabinetImpulse.
public sealed partial class GuitarLayerTests(ITestOutputHelper output)
{
    private const int Rate = 48000;

    private static GuitarLayer Playing(int program, params int[] notes)
    {
        var g = new GuitarLayer(Rate);
        g.ProcessMidi(0, 0xC0, program, 0);
        foreach (var n in notes) g.ProcessMidi(0, 0x90, n, 100);
        return g;
    }

    private static float[] Render(ILayer g, int frames, float[]? right = null)
    {
        var l = new float[frames];
        right ??= new float[frames];
        for (var at = 0; at < frames; at += 512) g.Render(l.AsSpan(at, Math.Min(512, frames - at)), right.AsSpan(at, Math.Min(512, frames - at)));
        return l;
    }

    [Theory]
    [InlineData(29, new[] { 40, 47, 52 })]
    [InlineData(30, new[] { 40, 47, 52 })]
    [InlineData(27, new[] { 45, 52, 57, 60, 64 })]
    [InlineData(33, new[] { 28 })]
    [InlineData(24, new[] { 40, 47, 52, 55, 59, 64 })]
    [InlineData(25, new[] { 40, 47, 52, 55, 59, 64 })]
    [InlineData(26, new[] { 45, 52, 57, 60, 64 })]
    [InlineData(28, new[] { 40, 47, 52 })]
    [InlineData(31, new[] { 52, 57 })]
    [InlineData(32, new[] { 28, 33 })]
    [InlineData(34, new[] { 28 })]
    [InlineData(35, new[] { 33 })]
    [InlineData(36, new[] { 23, 35 })]
    [InlineData(37, new[] { 40 })]
    public void ModelledProgramsAreFiniteAudibleAndUnclipped(int program, int[] notes)
    {
        var g = Playing(program, notes);
        Assert.True(g.Owns(0));
        var x = Render(g, 2 * Rate);
        Assert.All(x, v => Assert.True(float.IsFinite(v)));
        var rms = Math.Sqrt(x.Sum(v => (double)v * v) / x.Length);
        var peak = x.Max(MathF.Abs);
        output.WriteLine($"program {program}: rms {20 * Math.Log10(rms):F1} dBFS, peak {20 * Math.Log10(peak):F1} dBFS");
        Assert.True(rms > 1e-3 && peak < 0.98f, $"rms {rms}, peak {peak}");
    }

    [Theory]
    [InlineData(27, 57)]
    [InlineData(29, 45)]
    [InlineData(30, 40)]
    [InlineData(33, 33)]
    [InlineData(25, 64)]
    [InlineData(24, 52)]
    [InlineData(26, 52)]
    [InlineData(32, 33)]
    [InlineData(35, 33)]
    [InlineData(36, 40)]
    public void APluckedNoteSoundsAtItsPitch(int program, int note)
    {
        var x = Render(Playing(program, note), Rate);
        var cents = TestSoundFont.CentsOff(x, Rate, Rate / 2, 8192, 440 * Math.Pow(2, (note - 69) / 12.0));
        output.WriteLine($"program {program} note {note}: {cents:F2} cents");
        Assert.True(Math.Abs(cents) < 5, $"{cents:F1} cents");
    }

    // Low B (30.9 Hz) to A1 through the full bass rig: within 1 cent, and the tail carries no DC.
    [Theory]
    [InlineData(33, 23)]
    [InlineData(33, 26)]
    [InlineData(34, 28)]
    [InlineData(36, 33)]
    public void BassNotesAreInTuneWithinACentWithoutDc(int program, int note)
    {
        var x = Render(Playing(program, note), 2 * Rate);
        var hz = 440 * Math.Pow(2, (note - 69) / 12.0);
        var cents = TestSoundFont.CentsOff(x, Rate, Rate / 2, 16384, hz);
        var tail = x.AsSpan(Rate, Rate).ToArray();
        var w = Enumerable.Range(0, Rate).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Rate)).ToArray();   // Hann: the partials leak nothing into DC
        var (mean, rms) = (tail.Select((v, i) => v * w[i]).Sum() / w.Sum(), Math.Sqrt(tail.Average(v => (double)v * v)));
        output.WriteLine($"program {program} note {note}: {cents:F2} cents, DC {mean / rms:E1} of rms");
        Assert.True(Math.Abs(cents) < 1, $"{cents:F2} cents");
        Assert.True(Math.Abs(mean) < 1e-3 * rms);
    }

    // A full-velocity slap: what lies above 16 kHz in the first 40 ms is far below the transient (nothing folds back).
    [Theory]
    [InlineData(36)]
    [InlineData(37)]
    public void TheSlapTransientDoesNotAlias(int program)
    {
        var g = new GuitarLayer(Rate);
        g.ProcessMidi(0, 0xC0, program, 0);
        g.ProcessMidi(0, 0x90, 28, 127);
        var p = GeneralUserBankTests.Spectrum(Render(g, 4096), 0, 2048);
        var bin = Rate / 2048.0;
        double all = p.Sum(), high = p.Skip((int)(16000 / bin)).Sum();
        output.WriteLine($"program {program}: above 16 kHz {10 * Math.Log10(high / all):F1} dB of the transient");
        Assert.True(high < 1e-5 * all);
    }

    [Fact]
    public void PlayingAllocatesNothing()
    {
        var g = Playing(30, 40, 47, 52);
        g.ProcessMidi(1, 0xC0, 33, 0);
        g.ProcessMidi(1, 0x90, 28, 100);
        g.ProcessMidi(2, 0xC0, 35, 0);
        g.ProcessMidi(3, 0xC0, 25, 0);
        var l = new float[512];
        var r = new float[512];
        for (var i = 0; i < 8; i++) g.Render(l, r);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 40; i++)
        {
            g.ProcessMidi(0, 0x90, 40 + i % 12, 90);
            g.ProcessMidi(0, 0xE0, 0, 64 + i % 20);
            g.ProcessMidi(0, 0xB0, 64, i % 2 == 0 ? 127 : 0);
            g.ProcessMidi(0, 0x80, 40 + (i + 6) % 12, 0);
            g.ProcessMidi(1, 0xC0, i % 2 == 0 ? 34 : 33, 0);
            g.ProcessMidi(1, 0x90, 28 + i % 5, 100);
            g.ProcessMidi(2, 0x90, 33 + i % 3, 90);   // overlapping: fretless glides
            g.ProcessMidi(2, 0x80, 33 + (i + 2) % 3, 0);
            g.ProcessMidi(2, 0xC0, i % 4 == 0 ? 36 : 35, 0);
            g.ProcessMidi(3, 0x90, 40 + i % 24, 80);
            g.Render(l, r);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void RenderCostIsMeasuredPerString()
    {
        int[] six = [40, 47, 52, 55, 59, 64];
        foreach (var (program, notes) in new[] { (30, new[] { 40 }), (30, six), (27, six), (24, six), (25, six), (26, six), (33, new[] { 28 }), (32, new[] { 28 }), (35, new[] { 33 }), (36, new[] { 28 }) })
        {
            var g = Playing(program, notes);
            Render(g, Rate / 10);
            var watch = Stopwatch.StartNew();
            Render(g, 4 * Rate);
            var ms = watch.Elapsed.TotalMilliseconds / 4;
            output.WriteLine($"program {program}, {notes.Length} string(s): {ms:F2} ms per second of audio ({ms / 10:F2} % of one core for the channel), {ms / notes.Length:F2} ms per string (rig included)");
            Assert.True(ms < 400);   // Debug sanity bound: the captured amp runs unoptimised here (Release figures in the status doc)
        }
    }

    [Fact]
    public void TheSynthesizerSendsOwnedChannelsToTheLayerAndKeepsTheRest()
    {
        var s = new Synthesizer(new SoundFont(TestSoundFont.Build()), Rate, 64, effects: false) { Layer = new GuitarLayer(Rate) };
        s.ProcessMidiMessage(0, 0xC0, 30, 0);
        s.ProcessMidiMessage(0, 0x90, 40, 100);
        Assert.Equal(0, s.ActiveVoices);   // the layer plays it
        s.ProcessMidiMessage(1, 0x90, 60, 100);
        s.ProcessMidiMessage(9, 0x90, 36, 100);
        Assert.Equal(2, s.ActiveVoices);   // piano channel and drums stay on the SoundFont
        var x = Render(new SynthAsLayer(s), Rate / 2);
        Assert.True(x.Max(MathF.Abs) > 1e-2);
    }

    // Lets Render() drive a Synthesizer (it overwrites; the helper only reads the result).
    private sealed class SynthAsLayer(Synthesizer s) : ILayer
    {
        public bool Owns(int channel) => false;
        public void ProcessMidi(int channel, int command, int data1, int data2) => s.ProcessMidiMessage(channel, command, data1, data2);
        public void Render(Span<float> left, Span<float> right) => s.Render(left, right);
        public void Reset() => s.Reset();
    }

    // The user's amp: a good .nam plays, a missing cabinet falls back with the reason, swapping back mid-note leaves no step.
    [Fact]
    public void TheUsersAmpLoadsFallsBackAndSwapsWithoutAStep()
    {
        var nam = Path.Combine(Path.GetTempPath(), "tf-rig-test.nam");
        NamTests.Arr[] arrays = [NamTests.A1(1, 4, 1, true, [1, 2, 4, 8])];
        var (_, n) = NamReference.WaveNet(arrays, new float[10000], new float[1]);
        File.WriteAllText(nam, NamTests.WaveNetJson(arrays, NamTests.Random(n, 3, 0.3f)).ToJsonString());
        var g = Playing(30, 40, 47, 52);
        var status = g.LoadRig(nam, "no-such-cab.wav");
        output.WriteLine(status);
        Assert.Contains("amp tf-rig-test", status);
        Assert.Contains("built-in cabinet (no-such-cab.wav not usable: file not found)", status);
        Assert.Contains("built-in amp (bad.nam not usable", GuitarLayer.DescribeRig(Path.Combine(Path.GetTempPath(), "bad.nam"), null));
        var a = Render(g, Rate / 2);
        g.LoadRig(null, null);
        var b = Render(g, Rate / 2);
        float Step(float[] x, int from, int to) => Enumerable.Range(from + 1, to - from - 1).Max(i => MathF.Abs(x[i] - x[i - 1]));
        output.WriteLine($"user amp max step {Step(a, 0, a.Length):F4}, around the swap {Step(b, 0, 2048):F4}, built-in after {Step(b, 2048, b.Length):F4}");
        Assert.True(a.All(float.IsFinite) && a.Max(MathF.Abs) > 1e-3);
        Assert.True(Step(b, 0, 2048) <= 1.5f * Math.Max(Step(a, 0, a.Length), Step(b, 2048, b.Length)));
    }

    // ---- listening files -------------------------------------------------------------------------------------------------------------

    internal const double Eighth = 0.25;   // 120 bpm

    [Fact]
    public void WritesListeningFilesWhenAsked()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TABFORGE_SYNTH_WAV_DIR"))) return;
        var riff = Riff(0);
        var arp = Arpeggio(1);
        var bass = BassLine(2);
        var pad = new List<(double, int, int, int)> { (0, 0xB3, 7, 60) };
        foreach (var (bar, root) in new[] { (0, 57), (1, 60), (2, 55), (3, 52) }) Note(pad, 3, bar * 2, 1.95, root, 70);
        var written = Write("tfsynth_riff30.wav", riff) & Write("tfsynth_arp27.wav", arp) & Write("tfsynth_bass33.wav", bass)
                      & Write("tfsynth_mix.wav", [.. riff, .. arp, .. bass, .. pad]);
        output.WriteLine(written ? "written" : "TABFORGE_SYNTH_WAV_DIR not set: nothing written");
    }

    private static bool Write(string name, List<(double At, int Status, int D1, int D2)> song)
    {
        var s = new Synthesizer(new SoundFont(TestSoundFont.Build()), Rate, 64, effects: true) { Layer = new GuitarLayer(Rate), MasterVolume = 0.3f };
        var frames = (int)((32 * Eighth + 2) * Rate);
        float[] l = new float[frames], r = new float[frames];
        var at = 0;
        foreach (var e in song.OrderBy(e => e.At).ThenBy(e => (e.Status & 0xF0) == 0x80 ? 0 : 1))
        {
            var f = Math.Min(frames, (int)(e.At * Rate));
            if (f > at) { s.Render(l.AsSpan(at, f - at), r.AsSpan(at, f - at)); at = f; }
            s.ProcessMidiMessage(e.Status & 15, e.Status & 0xF0, e.D1, e.D2);
        }
        s.Render(l.AsSpan(at), r.AsSpan(at));
        return TestSoundFont.WriteWav(name, l, r, Rate);
    }

    internal static void Note(List<(double, int, int, int)> song, int ch, double at, double length, int note, int vel)
    {
        song.Add((at, 0x90 | ch, note, vel));
        song.Add((at + length, 0x80 | ch, note, 0));
    }

    internal static List<(double, int, int, int)> Riff(int ch, int program = 30)
    {
        var song = new List<(double, int, int, int)> { (0, 0xC0 | ch, program, 0) };
        int[][] bars = [[40, 40, 40, 40, 43, 0, 45, 0], [40, 40, 40, 40, 48, 0, 47, 0], [40, 40, 40, 40, 43, 0, 45, 0], [40, 40, 40, 0, 0, 0, 0, 0]];
        for (var bar = 0; bar < 4; bar++)
            for (var e = 0; e < 8; e++)
            {
                var root = bars[bar][e];
                if (root == 0) continue;
                var held = e + 1 < 8 && bars[bar][e + 1] == 0;
                var length = bar == 3 && e == 2 ? 6 * Eighth : held ? 2 * Eighth - 0.03 : 0.18;
                foreach (var n in new[] { root, root + 7, root + 12 }) Note(song, ch, (bar * 8 + e) * Eighth, length, n, held ? 110 : 95);
            }
        return song;
    }

    internal static List<(double, int, int, int)> Arpeggio(int ch)
    {
        var song = new List<(double, int, int, int)> { (0, 0xC0 | ch, 27, 0) };
        int[][] bars = [[57, 60, 64, 69, 64, 60, 64, 60], [48, 55, 60, 64, 67, 64, 60, 55], [43, 50, 55, 59, 62, 59, 55, 50], [40, 47, 52, 55, 59, 55, 52, 47]];
        for (var bar = 0; bar < 4; bar++)
            for (var e = 0; e < 8; e++) Note(song, ch, (bar * 8 + e) * Eighth, 1.0, bars[bar][e], 70 + 6 * (e % 3));
        return song;
    }

    internal static List<(double, int, int, int)> BassLine(int ch)
    {
        var song = new List<(double, int, int, int)> { (0, 0xC0 | ch, 33, 0) };
        int[][] bars = [[28, 28, 28, 28, 28, 28, 31, 33], [28, 28, 28, 28, 36, 36, 35, 35], [28, 28, 28, 28, 28, 28, 31, 33], [28, 28, 28, 28, 28, 28, 28, 28]];
        for (var bar = 0; bar < 4; bar++)
            for (var e = 0; e < 8; e++) Note(song, ch, (bar * 8 + e) * Eighth, bar == 3 && e == 2 ? 6 * Eighth : Eighth - 0.03, bars[bar][e], 100);
        return song;
    }
}

