using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;
using TabForge.Services;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using TabForge.Sf2Synth.Sf2;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the checks of the modelled guitar/bass layer: finite, audible, unclipped output for programs 29/30/27/33, pitch within a few
//   cents, no allocation, CPU per string, routing inside Synthesizer and inside the engine's GmSynth (experimental choice), and the
//   listening WAVs (TABFORGE_SYNTH_WAV_DIR).
// Does not own: the SoundFont player (Sf2SynthTests), the engine choice text (SoundEngineTests).
// Tests: GuitarLayer, PluckedString, GuitarBus, StageAmp, Oversampler2x, CabinetImpulse, GmSynth, GmBank.
public sealed class GuitarLayerTests(ITestOutputHelper output)
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
    public void APluckedNoteSoundsAtItsPitch(int program, int note)
    {
        var x = Render(Playing(program, note), Rate);
        var cents = TestSoundFont.CentsOff(x, Rate, Rate / 2, 8192, 440 * Math.Pow(2, (note - 69) / 12.0));
        output.WriteLine($"program {program} note {note}: {cents:F2} cents");
        Assert.True(Math.Abs(cents) < 5, $"{cents:F1} cents");
    }

    [Fact]
    public void PlayingAllocatesNothing()
    {
        var g = Playing(30, 40, 47, 52);
        g.ProcessMidi(1, 0xC0, 33, 0);
        g.ProcessMidi(1, 0x90, 28, 100);
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
            g.Render(l, r);
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void RenderCostIsMeasuredPerString()
    {
        foreach (var (program, notes) in new[] { (30, new[] { 40 }), (30, new[] { 40, 47, 52, 55, 59, 64 }), (27, new[] { 40, 47, 52, 55, 59, 64 }), (33, new[] { 28 }) })
        {
            var g = Playing(program, notes);
            Render(g, Rate / 10);
            var watch = Stopwatch.StartNew();
            Render(g, 4 * Rate);
            var ms = watch.Elapsed.TotalMilliseconds / 4;
            output.WriteLine($"program {program}, {notes.Length} string(s): {ms:F2} ms per second of audio, {ms / notes.Length:F2} ms per string (rig included)");
            Assert.True(ms < 200);
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

    [Fact]
    public void TheEngineChoicePlaysTheLayerAndItsSoundFont()
    {
        var sf2 = Path.Combine(Path.GetTempPath(), "tf-sf2synth-test.sf2");
        File.WriteAllBytes(sf2, TestSoundFont.Build());
        var bank = new GmBank(windowsHost: false, bundledPath: Path.Combine(Path.GetTempPath(), "tf-no-such-bank.sf2"));
        Assert.True(bank.Select(SoundEngines.TabForgeSynthPath + "|" + sf2, out var error), error);
        Assert.True(bank.Current.Experimental && bank.Current.Sf2 is not null);
        using var synth = new GmSynth(Rate, 512, bank);
        BlockMidi[] notes = [new() { Status = 0xC0, Data1 = 30 }, new() { Frame = 10, Status = 0x90, Data1 = 40, Data2 = 100 }, new() { Frame = 20, Status = 0x91, Data1 = 69, Data2 = 100 }];
        var input = new[] { new float[4800], new float[4800] };
        var outBuf = new[] { new float[4800], new float[4800] };
        synth.Process(input, outBuf, 4800, notes, new TransportInfo());
        Assert.True(outBuf[0].Max(MathF.Abs) > 1e-2);
        synth.Process(input, outBuf, 4800, ReadOnlySpan<BlockMidi>.Empty, new TransportInfo());
        var before = GC.GetAllocatedBytesForCurrentThread();
        synth.Process(input, outBuf, 4800, ReadOnlySpan<BlockMidi>.Empty, new TransportInfo());
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    // The user's amp: a good .nam plays, a missing cabinet falls back with the reason, swapping back mid-note leaves no step,
    // the settings reach the engine path, and a changed amp keeps the loaded SoundFont (only the rig swaps).
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

        var plugins = new PluginSettings { SoundEngine = SoundEngines.TabForgeSynth, SynthAmpPath = nam };
        Assert.EndsWith("|" + nam, SoundFontStore.ActivePath(plugins, windowsHost: false));
        var sf2 = Path.Combine(Path.GetTempPath(), "tf-sf2synth-test.sf2");
        File.WriteAllBytes(sf2, TestSoundFont.Build());
        var bank = new GmBank(windowsHost: false, bundledPath: sf2);
        Assert.True(bank.Select(SoundEngines.TabForgeSynthPath + "|" + sf2, out _));
        var font = bank.Current.Sf2;
        Assert.True(bank.Select(SoundEngines.TabForgeSynthPath + "|" + sf2 + "|" + nam, out _));
        Assert.True(bank.Current.RigOnly && ReferenceEquals(font, bank.Current.Sf2) && bank.Current.Amp == nam);
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
