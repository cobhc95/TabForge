using System;
using System.Globalization;
using System.IO;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Dsp;
using TabForge.Sf2Synth.Guitar;
using Xunit;
using Song = System.Collections.Generic.List<(double At, int Status, int D1, int D2)>;

namespace TabForge.Avalonia.Tests;

// Owns: the DI tone guard (octave-band shape and 2 kHz decay of the pickup DI of 27/29/30 against a measured real guitar DI) and the
//   render of a note list (TABFORGE_DI_NOTES: lines "onset-s note attack-peak-dBFS off-s"; skipped without it): each note's velocity is
//   fitted so its attack peak in the trimmed DI matches the list, then programs 29 and 27 render as pickup DI and 29 through the
//   captured amp into TABFORGE_SYNTH_WAV_DIR (tfsynth7_raw_*.wav, tag TABFORGE_DI_TAG).
// Does not own: the rest of the layer (GuitarLayerTests.cs, .CapturedAmp).
// Tests: GuitarLayer.DiOnly, GuitarBus (pickup), PluckedString, Voicings 27/29.
public sealed partial class GuitarLayerTests
{
    private static readonly int[] FitVelocities = [20, 40, 70, 100, 127];

    // Measured real guitar DI (8.5 s riff, low muted notes and single notes E3-G3): octave bands 63 Hz-8 kHz (Biquad band-pass, Q 1.41)
    // relative to their power sum, dB; T60 of the 2 kHz band on held notes (median of four, 0.42-1.19 s). Statistics only.
    private static readonly int[] DiBands = [63, 125, 250, 500, 1000, 2000, 4000, 8000];
    private static readonly double[] DiTarget = [-16.9, -10.0, -5.8, -4.0, -7.6, -14.6, -21.4, -28.3];
    private const double DiT60At2k = 0.76, DiBandTolerance = 3;

    // A phrase in the same register: muted low D chugs between held single notes.
    private static Song DiPhrase()
    {
        var song = new Song();
        int[] notes = [38, 38, 53, 38, 38, 52, 38, 55];
        for (var i = 0; i < 32; i++)
        {
            var n = notes[i % 8];
            song.Add((i * 0.2, 0x90, n, n < 50 ? 100 : 90));
            song.Add((i * 0.2 + (n < 50 ? 0.08 : 0.35), 0x80, n, 0));
        }
        return song;
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(27)]
    public void TheDiMatchesTheMeasuredGuitarDi(int program)
    {
        var x = Di(program, DiPhrase(), (int)(6.8 * Rate));
        var power = DiBands.Select(hz => Band(x, hz).Average(v => (double)v * v)).ToArray();
        var levels = power.Select(p => 10 * Math.Log10(p / power.Sum())).ToArray();
        output.WriteLine($"program {program}: bands {string.Join(" / ", DiBands)} Hz: ours {string.Join(" / ", levels.Select(v => v.ToString("F1")))}, target {string.Join(" / ", DiTarget)} dB");
        for (var i = 0; i < DiBands.Length; i++) Assert.InRange(levels[i] - DiTarget[i], -DiBandTolerance, DiBandTolerance);

        // T60 at 2 kHz (band-pass four times, so the low harmonics do not leak in): a held F3 at velocity 100, level slope from 50 to 500 ms.
        var held = Band(Band(Band(Band(Di(program, [(0, 0x90, 53, 100), (1.5, 0x80, 53, 0)], Rate), 2000), 2000), 2000), 2000);
        const int frame = Rate / 100;
        var db = Enumerable.Range(5, 45).Select(f => 10 * Math.Log10(held.Skip(f * frame).Take(frame).Average(v => (double)v * v) + 1e-30)).ToArray();
        var mean = db.Average();
        var slope = Enumerable.Range(0, db.Length).Sum(i => (i - 22.0) * (db[i] - mean)) / Enumerable.Range(0, db.Length).Sum(i => (i - 22.0) * (i - 22.0)) * 100;
        output.WriteLine($"program {program}: T60 at 2 kHz {-60 / slope:F2} s (target {DiT60At2k})");
        Assert.InRange(-60 / slope, DiT60At2k / 1.6, DiT60At2k * 1.6);
    }

    private static float[] Band(float[] x, double hz)
    {
        var f = new Biquad { C = BiquadCoefs.Of(new EqSpec(EqKind.BandPass, hz, 1.41), Rate) };
        return x.Select(v => f.Process(v)).ToArray();
    }

    [Fact]
    public void ANoteListRendersAsDiAtItsAttackLevels()
    {
        var path = Environment.GetEnvironmentVariable("TABFORGE_DI_NOTES");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var tag = Environment.GetEnvironmentVariable("TABFORGE_DI_TAG") ?? "now";
        var list = File.ReadAllLines(path).Where(l => l.Trim().Length > 0)
            .Select(l => l.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        var frames = (int)((list.Max(e => e[3]) + 0.5) * Rate);
        foreach (var program in new[] { 29, 27 })
        {
            var trim = Voicings.Of(program).Rig.Captured is { } c ? c.Trim * Voicings.Of(program).Rig.Drive : 1f;
            // Fit: our attack peak (dBFS, trimmed DI, first 30 ms) per note and velocity; each event takes the velocity whose peak matches, linear between the fit points.
            var curve = list.Select(e => (int)e[1]).Distinct().ToDictionary(n => n, n => FitVelocities.Select(v => AttackPeakDb(Di(program, [(0, 0x90, n, v), (0.3, 0x80, n, 0)], (int)(0.3 * Rate)), 0, trim)).ToArray());
            foreach (var (n, p) in curve) output.WriteLine($"program {program} note {n}: attack peak {string.Join(" / ", p.Select(v => v.ToString("F1")))} dBFS at velocity {string.Join(" / ", FitVelocities)}");
            var song = new Song { (0, 0xC0, program, 0) };
            foreach (var e in list)
            {
                var vel = VelocityFor(curve[(int)e[1]], e[2]);
                song.Add((e[0], 0x90, (int)e[1], vel));
                song.Add((Math.Max(e[3], e[0] + 0.03), 0x80, (int)e[1], 0));
            }
            var di = Di(program, song, frames).Select(v => v * trim).ToArray();
            TestSoundFont.WriteWav($"tfsynth7_raw_di{program}_{tag}.wav", di, di, Rate);
            if (program != 29) continue;
            var amp = RenderSong(new GuitarLayer(Rate), Dry(song), SynthOptions.Default, frames).Select(v => v / MathF.Sqrt(2)).ToArray();
            TestSoundFont.WriteWav($"tfsynth7_raw_amp{program}_{tag}.wav", amp, amp, Rate);
        }
    }

    // The pickup DI, bus output (no trim).
    private static float[] Di(int program, Song song, int frames)
    {
        var g = new GuitarLayer(Rate) { DiOnly = true };
        return RenderSong(g, Dry([(0, 0xC0, program, 0), .. song]), SynthOptions.Default, frames).Select(v => v / MathF.Sqrt(2)).ToArray();
    }

    private static double AttackPeakDb(float[] x, int at, float trim) => 20 * Math.Log10(x.Skip(at).Take(Rate * 3 / 100).Max(MathF.Abs) * trim + 1e-12);

    private static int VelocityFor(double[] peaks, double target)
    {
        if (target <= peaks[0]) return FitVelocities[0];
        for (var i = 1; i < peaks.Length; i++)
            if (target <= peaks[i]) return (int)Math.Round(FitVelocities[i - 1] + (FitVelocities[i] - FitVelocities[i - 1]) * (target - peaks[i - 1]) / Math.Max(1e-6, peaks[i] - peaks[i - 1]));
        return 127;
    }
}
