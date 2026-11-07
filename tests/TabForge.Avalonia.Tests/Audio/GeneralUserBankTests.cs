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
using Song = System.Collections.Generic.List<(double At, int Status, int D1, int D2)>;

namespace TabForge.Avalonia.Tests;

// Owns: the real-bank checks of Sf2Synth, run only when TABFORGE_GS_SF2 names a GeneralUser GS file (never shipped in the repo):
//   the hydra parses consistently (presets, zones, samples, loops, stereo links, modulators), and A/B renders with the guitar
//   layer off and on report load time, CPU per voice, peak, NaN and the largest sample step (written as WAVs on TABFORGE_SYNTH_WAV_DIR).
// Does not own: the synthetic-bank checks (Sf2SynthTests, GuitarLayerTests).
// Tests: SoundFont, Sf2Reader, Synthesizer, Voice, GuitarLayer (levels against MeltySynth on the same bank).
public sealed class GeneralUserBankTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private static readonly string? BankPath = Environment.GetEnvironmentVariable("TABFORGE_GS_SF2");

    [Fact]
    public void TheRealBankParsesConsistently()
    {
        if (string.IsNullOrEmpty(BankPath) || !File.Exists(BankPath)) return;
        var watch = Stopwatch.StartNew();
        var font = new SoundFont(BankPath);
        output.WriteLine($"load {watch.ElapsedMilliseconds} ms, {font.Data.Length} sample points");
        var zones = font.Instruments.SelectMany(i => i.Zones).ToArray();
        var badSample = zones.Count(z => z.Link >= font.Samples.Length);
        var badInstrument = font.Presets.SelectMany(p => p.Zones).Count(z => z.Link >= font.Instruments.Length);
        var real = font.Samples.Take(font.Samples.Length - 1).ToArray();   // the last header is EOS
        var badBounds = real.Count(s => s.Start < 0 || s.End > font.Data.Length || s.End <= s.Start);
        var badLoops = real.Count(s => s.LoopEnd > s.LoopStart && (s.LoopStart < s.Start || s.LoopEnd > s.End));
        var looped = font.Instruments.Sum(i => i.Zones.Count(z => ((z.Has((int)G.SampleModes) ? z.Gen[(int)G.SampleModes] : i.Global?.Gen[(int)G.SampleModes] ?? 0) & 1) == 1));
        var stereo = real.Count(s => (s.Type & 6) != 0);
        var linkOk = real.Where(s => (s.Type & 6) != 0).Count(s => s.Link < real.Length && (real[s.Link].Type & 6) != 0);
        var mods = font.Instruments.Sum(i => i.Zones.Sum(z => z.Mods.Length) + (i.Global?.Mods.Length ?? 0))
                   + font.Presets.Sum(p => p.Zones.Sum(z => z.Mods.Length) + (p.Global?.Mods.Length ?? 0));
        var gm = Enumerable.Range(0, 128).Count(p => font.Find(0, p, false).Program == p && font.Find(0, p, false).Bank == 0);
        output.WriteLine($"{font.Presets.Count} presets ({gm}/128 GM melodic, drums {font.Presets.Count(p => p.Bank == 128)}), {font.Instruments.Length} instruments, {zones.Length} zones, {real.Length} samples");
        output.WriteLine($"looped zones {looped}, stereo samples {stereo} (link valid {linkOk}), modulators {mods}");
        output.WriteLine($"bad sample links {badSample}, bad instrument links {badInstrument}, bad bounds {badBounds}, loops outside sample {badLoops}");
        Assert.Equal(128, gm);
        Assert.Equal(0, badSample + badInstrument + badBounds);
        Assert.Equal(stereo, linkOk);
    }

    [Fact]
    public void ABRendersWithTheLayerOffAndOn()
    {
        if (string.IsNullOrEmpty(BankPath) || !File.Exists(BankPath)) return;
        var font = new SoundFont(BankPath);
        var songs = new (string Name, Song Song)[]
        {
            ("riff30", GuitarLayerTests.Riff(0, 30)), ("riff29", GuitarLayerTests.Riff(0, 29)),
            ("arp27", GuitarLayerTests.Arpeggio(1)), ("bass33", GuitarLayerTests.BassLine(2)), ("mix", Mix()),
        };
        foreach (var (name, song) in songs)
            foreach (var layer in new[] { false, true })
            {
                var s = new Synthesizer(font, Rate, 64, effects: true) { MasterVolume = 0.5f, Layer = layer ? new GuitarLayer(Rate) : null };
                var (l, r, ms, voices) = Render(s, song);
                var seconds = l.Length / (double)Rate;
                var both = l.Concat(r).ToArray();
                var nan = both.Count(v => !float.IsFinite(v));
                var peak = both.Max(MathF.Abs);
                var (step, at) = MaxStep(l);
                var file = $"tfsynth_gs_{name}_{(layer ? "layer" : "sf2")}.wav";
                TestSoundFont.WriteWav(file, l, r, Rate);
                output.WriteLine($"{file}: {ms / seconds:F1} ms/s, mean voices {voices:F1} ({(voices > 0 ? ms / seconds / voices : 0):F2} ms/s per SF2 voice), peak {20 * Math.Log10(peak):F1} dBFS, clipped {both.Count(v => MathF.Abs(v) >= 1)}, NaN {nan}, max step {step:F3} at {at / (double)Rate:F3} s");
                Assert.Equal(0, nan);
            }
    }

    // The reference: MeltySynth on the same bank, same note, dry, master 0.5 both.
    [Fact]
    public void LevelsMatchMeltySynth()
    {
        if (string.IsNullOrEmpty(BankPath) || !File.Exists(BankPath)) return;
        var font = new SoundFont(BankPath);
        var melty = new MeltySynth.SoundFont(BankPath);
        foreach (var (ch, program, key, vel) in new[] { (0, 0, 60, 100), (0, 0, 60, 40), (0, 30, 40, 100), (0, 33, 28, 100), (0, 48, 64, 100), (9, 0, 36, 100), (9, 0, 38, 100) })
        {
            var ours = new Synthesizer(font, Rate, 64, effects: false) { MasterVolume = 0.5f };
            var theirs = new MeltySynth.Synthesizer(melty, new MeltySynth.SynthesizerSettings(Rate) { EnableReverbAndChorus = false }) { MasterVolume = 0.5f };
            ours.ProcessMidiMessage(ch, 0xC0, program, 0);
            theirs.ProcessMidiMessage(ch, 0xC0, program, 0);
            ours.ProcessMidiMessage(ch, 0x90, key, vel);
            theirs.NoteOn(ch, key, vel);
            float[] a = new float[Rate], b = new float[Rate], c = new float[Rate], d = new float[Rate];
            ours.Render(a, b);
            theirs.Render(c, d);
            double Db(float[] x, float[] y) => 10 * Math.Log10(x.Concat(y).Sum(v => (double)v * v) / (2.0 * x.Length) + 1e-20);
            output.WriteLine($"ch {ch + 1} program {program} key {key} vel {vel}: ours {Db(a, b):F1} dB, MeltySynth {Db(c, d):F1} dB, diff {Db(a, b) - Db(c, d):F1} dB");
            if (vel == 100) Assert.InRange(Db(a, b) - Db(c, d), -2, 2);   // velocity 40 differs on purpose: we honour the bank's own velocity modulators
        }
    }
    // Same MIDI, layer on vs off: the modelled guitars sit within 3 dB of the bank's own (velocity 100, chord and single note).
    [Fact]
    public void TheLayerIsAsLoudAsTheBankGuitars()
    {
        if (string.IsNullOrEmpty(BankPath) || !File.Exists(BankPath)) return;
        var font = new SoundFont(BankPath);
        foreach (var program in new[] { 24, 25, 26, 27, 28, 29, 30, 31, 33, 34 })
            foreach (var notes in new[] { new[] { 45 }, program >= 32 ? [33] : new[] { 40, 47, 52, 55 } })
            {
                double Db(bool layer)
                {
                    var s = new Synthesizer(font, Rate, 64, effects: false) { MasterVolume = 0.5f, Layer = layer ? new GuitarLayer(Rate) : null };
                    s.ProcessMidiMessage(0, 0xC0, program, 0);
                    foreach (var n in notes) s.ProcessMidiMessage(0, 0x90, program >= 32 ? n - 12 : n, 100);
                    float[] l = new float[Rate], r = new float[Rate];
                    s.Render(l, r);
                    return 10 * Math.Log10(l.Concat(r).Sum(v => (double)v * v) / (2.0 * Rate) + 1e-20);
                }
                var diff = Db(true) - Db(false);
                output.WriteLine($"program {program}, {notes.Length} note(s): layer {Db(true):F1} dB, bank {Db(false):F1} dB, diff {diff:F1} dB");
                Assert.InRange(diff, -3, 3);
            }
    }

    private static (float Step, int At) MaxStep(float[] x)
    {
        var (best, at) = (0f, 0);
        for (var i = 1; i < x.Length; i++) { var d = MathF.Abs(x[i] - x[i - 1]); if (d > best) (best, at) = (d, i); }
        return (best, at);
    }

    private static (float[] L, float[] R, double Ms, double Voices) Render(Synthesizer s, Song song)
    {
        var frames = (int)((32 * GuitarLayerTests.Eighth + 2) * Rate);
        float[] l = new float[frames], r = new float[frames];
        var at = 0;
        var watch = new Stopwatch();
        double voiceSum = 0;
        var blocks = 0;
        void Run(int to)
        {
            for (; at < to; at += Math.Min(512, to - at))
            {
                var n = Math.Min(512, to - at);
                watch.Start();
                s.Render(l.AsSpan(at, n), r.AsSpan(at, n));
                watch.Stop();
                voiceSum += s.ActiveVoices;
                blocks++;
            }
        }
        foreach (var e in song.OrderBy(e => e.At).ThenBy(e => (e.Status & 0xF0) == 0x80 ? 0 : 1))
        {
            Run(Math.Min(frames, (int)(e.At * Rate)));
            s.ProcessMidiMessage(e.Status & 15, e.Status & 0xF0, e.D1, e.D2);
        }
        Run(frames);
        return (l, r, watch.Elapsed.TotalMilliseconds, voiceSum / Math.Max(1, blocks));
    }

    // Drums ch10, piano 0, strings 48, finger bass 33, distorted guitar 30.
    private static Song Mix()
    {
        Song song = [.. GuitarLayerTests.Riff(1, 30), .. GuitarLayerTests.BassLine(2), (0, 0xC3, 48, 0), (0, 0xC4, 0, 0), (0, 0xB3, 7, 80), (0, 0xB4, 7, 90)];
        var e8 = GuitarLayerTests.Eighth;
        foreach (var (bar, chord) in new[] { (0, new[] { 52, 59, 64 }), (1, new[] { 48, 55, 64 }), (2, new[] { 52, 59, 64 }), (3, new[] { 52, 59, 64 }) })
            foreach (var n in chord)
            {
                GuitarLayerTests.Note(song!, 3, bar * 8 * e8, 8 * e8 - 0.05, n + 12, 70);
                GuitarLayerTests.Note(song!, 4, bar * 8 * e8 + 2 * e8, e8, n + 12, 80);
                GuitarLayerTests.Note(song!, 4, bar * 8 * e8 + 6 * e8, e8, n + 12, 80);
            }
        for (var i = 0; i < 32; i++)
        {
            GuitarLayerTests.Note(song!, 9, i * e8, 0.1, 42, 80);
            if (i % 4 == 0) GuitarLayerTests.Note(song!, 9, i * e8, 0.1, 36, 110);
            if (i % 4 == 2) GuitarLayerTests.Note(song!, 9, i * e8, 0.1, 38, 105);
        }
        return song;
    }
}
