using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using Xunit;
using Song = System.Collections.Generic.List<(double At, int Status, int D1, int D2)>;

namespace TabForge.Sf2Synth.Tests;

// Owns: the objective realism table (bank sample vs layer, same note, velocity 100, dry): spectral centroid at 50 ms / 300 ms / 1 s,
//   T60 of the fundamental and the 5th harmonic, attack (10 to 90 % of the 1 ms envelope peak), harmonic-to-noise ratio (0.1-0.27 s);
//   and the A/B phrase WAVs tfsynth3_<program>_<sf2|layer>.wav (TABFORGE_SYNTH_WAV_DIR).
// Does not own: levels and parsing (GeneralUserBankTests.cs).
// Tests: GuitarLayer against the GeneralUser GS samples.
public sealed partial class GeneralUserBankTests
{
    private static readonly int[] Clean = [24, 25, 26, 27, 28, 31, 32, 33, 34, 35, 36, 37];

    [Fact]
    public void TimbreIsMeasuredAgainstTheBankSamples()
    {
        if (string.IsNullOrEmpty(BankPath) || !File.Exists(BankPath)) return;
        output.WriteLine("| prog | note | centroid 50 ms / 300 ms / 1 s (Hz) | T60 f0 (s) | T60 h5 (s) | attack (ms) | HNR (dB) |");
        output.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var program in Clean)
        {
            var note = program >= 32 ? 33 : 52;
            var hz = 440 * Math.Pow(2, (note - 69) / 12.0);
            var m = new[] { false, true }.Select(layer => Measure(Note(program, note, layer), hz)).ToArray();
            string Pair(Func<double[], double> f, string fmt) => $"{f(m[0]).ToString(fmt)} / {f(m[1]).ToString(fmt)}";
            output.WriteLine($"| {program} | {note} | {Pair(x => x[0], "F0")}, {Pair(x => x[1], "F0")}, {Pair(x => x[2], "F0")} | {Pair(x => x[3], "F1")} | {Pair(x => x[4], "F1")} | {Pair(x => x[5], "F0")} | {Pair(x => x[6], "F0")} |");
            Assert.All(m[1], v => Assert.True(double.IsFinite(v)));
        }
        output.WriteLine("(each cell: bank / layer)");
    }

    private float[] Note(int program, int note, bool layer)
    {
        var s = new Synthesizer(Bank, Rate, 64, effects: false) { MasterVolume = 0.5f, Layer = layer ? new GuitarLayer(Rate) : null };
        s.ProcessMidiMessage(0, 0xC0, program, 0);
        s.ProcessMidiMessage(0, 0x90, note, 100);
        float[] l = new float[3 * Rate], r = new float[3 * Rate];
        s.Render(l, r);
        return l.Select((v, i) => v + r[i]).ToArray();
    }

    // centroid 50 ms, 300 ms, 1 s; T60 f0, T60 h5; attack ms; HNR dB.
    private static double[] Measure(float[] x, double hz)
    {
        double Centroid(double t)
        {
            var p = Spectrum(x, (int)(t * Rate), 2048);
            var sum = Enumerable.Range(1, 1023).Sum(k => Math.Sqrt(p[k]));
            return sum > 0 ? Enumerable.Range(1, 1023).Sum(k => k * Math.Sqrt(p[k])) / sum * Rate / 2048.0 : 0;
        }
        var env = Enumerable.Range(0, x.Length / 48).Select(f => Math.Sqrt(Enumerable.Range(f * 48, 48).Average(i => (double)x[i] * x[i]))).ToArray();
        var peak = env.Max();
        var attack = Array.FindIndex(env, e => e >= 0.9 * peak) - Array.FindIndex(env, e => e >= 0.1 * peak);
        return [Centroid(0.05), Centroid(0.3), Centroid(1.0), T60(x, hz), T60(x, 5 * hz), attack, Hnr(x, hz)];
    }

    // Partial level every 50 ms (4096 Hann, peak within ±1.5 %), straight-line fit from its peak to 40 dB down or the end.
    private static double T60(float[] x, double hz)
    {
        var frames = Enumerable.Range(0, (x.Length - 4096) / 2400).Select(f => 10 * Math.Log10(Partial(x, f * 2400, hz) + 1e-30)).ToArray();
        var top = Array.IndexOf(frames, frames.Max());
        var end = top + 1;
        while (end < frames.Length && frames[end] > frames[top] - 40) end++;
        if (end - top < 3) return 0;
        var ts = Enumerable.Range(top, end - top).Select(i => i * 0.05).ToArray();
        var ys = frames[top..end];
        double mt = ts.Average(), my = ys.Average();
        var slope = ts.Zip(ys).Sum(p => (p.First - mt) * (p.Second - my)) / ts.Sum(t => (t - mt) * (t - mt));
        return slope < 0 ? Math.Min(60, -60 / slope) : 60;
    }

    private static double Partial(float[] x, int at, double hz) =>
        Enumerable.Range(-3, 7).Max(j => Dft(x, at, 4096, hz * (1 + 0.005 * j)));

    private static double Dft(float[] x, int at, int n, double hz)
    {
        Complex sum = 0;
        var w = -2 * Math.PI * hz / Rate;
        for (var i = 0; i < n; i++) sum += x[at + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n)) * Complex.FromPolarCoordinates(1, w * i);
        return sum.Magnitude * sum.Magnitude;
    }

    // 8192 Hann from 0.1 s: power within ±3 bins of each partial peak vs the rest, up to 8 kHz. Each partial is searched around the
    // last one plus the last spacing, so stiff strings' stretched partials are followed.
    private static double Hnr(float[] x, double hz)
    {
        var p = Spectrum(x, Rate / 10, 8192);
        var bin = Rate / 8192.0;
        var top = (int)(8000 / bin);
        var harmonic = new bool[top + 1];
        double prev = 0, step = hz / bin;
        while (prev + step < top - 3)
        {
            int lo = (int)(prev + 0.8 * step), hi = Math.Min(top, (int)Math.Ceiling(prev + 1.2 * step));
            var c = Enumerable.Range(lo, hi - lo + 1).MaxBy(i => p[i]);
            for (var i = Math.Max(1, c - 3); i <= Math.Min(top, c + 3); i++) harmonic[i] = true;
            if (prev > 0) step = 0.5 * step + 0.5 * (c - prev);
            prev = c;
        }
        double h = 0, n = 0;
        for (var i = 1; i <= top; i++) if (harmonic[i]) h += p[i]; else n += p[i];
        return 10 * Math.Log10(h / (n + 1e-30));
    }

    // Power spectrum (bins 0..n/2) of a Hann window, radix-2 FFT.
    internal static double[] Spectrum(float[] x, int at, int n)
    {
        var a = new Complex[n];
        for (var i = 0; i < n; i++) a[i] = x[at + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
            for (var i = 0; i < n; i += len)
                for (var k = 0; k < len / 2; k++)
                {
                    var w = Complex.FromPolarCoordinates(1, -2 * Math.PI * k / len) * a[i + k + len / 2];
                    (a[i + k], a[i + k + len / 2]) = (a[i + k] + w, a[i + k] - w);
                }
        return a.Take(n / 2 + 1).Select(c => c.Magnitude * c.Magnitude).ToArray();
    }

    // Same MIDI through the bank and the layer: single notes, a strum or double stop, a short line (fretless: overlapping, so it glides).
    [Fact]
    public void WritesThePhraseWavsWhenAsked()
    {
        if (string.IsNullOrEmpty(BankPath) || !File.Exists(BankPath) || Environment.GetEnvironmentVariable("TABFORGE_SYNTH_WAV_DIR") is null) return;
        foreach (var program in new[] { 24, 25, 26, 27, 33, 34, 35, 36, 32 })
            foreach (var layer in new[] { false, true })
            {
                var s = new Synthesizer(Bank, Rate, 64, effects: true) { MasterVolume = 0.5f, Layer = layer ? new GuitarLayer(Rate) : null };
                var (l, r, _, _) = Render(s, Phrase(program));
                TestSoundFont.WriteWav($"tfsynth3_{program}_{(layer ? "layer" : "sf2")}.wav", l, r, Rate);
            }
    }

    private static Song Phrase(int program)
    {
        var bass = program >= 32;
        Song song = [(0, 0xC0, program, 0)];
        int[] singles = bass ? [28, 33, 38] : [52, 55, 57];
        for (var i = 0; i < 3; i++) GuitarLayerTests.Note(song!, 0, i * 0.5, 0.45, singles[i], 70 + 20 * i);
        int[] chord = bass ? [33, 40] : [40, 47, 52, 55, 59, 64];
        for (var i = 0; i < chord.Length; i++) GuitarLayerTests.Note(song!, 0, 1.6 + 0.015 * i, 1.6, chord[i], 95);
        int[] line = bass ? [28, 31, 33, 35, 36, 35, 33, 31, 28] : [57, 60, 62, 64, 67, 64, 62, 60, 57];
        var overlap = program == 35 ? 0.05 : -0.02;
        for (var i = 0; i < line.Length; i++) GuitarLayerTests.Note(song!, 0, 3.6 + i * 0.25, i == line.Length - 1 ? 1.5 : 0.25 + overlap, line[i], 85 + 10 * (i % 2));
        return song;
    }
}

