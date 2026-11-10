using System;
using System.Linq;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Guitar;
using Xunit;
using Song = System.Collections.Generic.List<(double At, int Status, int D1, int D2)>;

namespace TabForge.Sf2Synth.Tests;

// Owns: the attack guards of every modelled program (bus output, one note at velocity 110): attack-to-sustain (peak of the first
//   15 ms over the rms of 100-300 ms), initial decay slope (5 ms rms in dB, fitted from its peak over 200 ms), the attack-peak range
//   between velocity 50 and 127, and attack-to-sustain of a downstroke chord on the guitars. Targets from the Nov 2026 measurement
//   (status doc): a real guitar DI shows 17.7 dB attack-to-sustain and -58 dB/s over the first 200 ms; the real amp output 7.7 dB.
// Does not own: the rest of the layer (GuitarLayerTests.cs and its other parts).
// Tests: PluckedString (double decay, pluck vs velocity), GuitarBus, Voicings.
public sealed partial class GuitarLayerTests
{
    private static float[] Struck(int program, int note, int velocity, params int[] chord)
    {
        var song = new Song { (0, 0xC0, program, 0) };
        int[] keys = chord.Length > 0 ? chord : [note];
        for (var k = 0; k < keys.Length; k++) song.AddRange([(k * 0.0044, 0x90, keys[k], velocity), (1.0, 0x80, keys[k], 0)]);
        return RenderSong(new GuitarLayer(Rate), Dry(song), SynthOptions.Default, Rate);
    }

    private static double PeakDb(float[] x, double ms) => 20 * Math.Log10(x.Take((int)(ms / 1000 * Rate)).Max(MathF.Abs) + 1e-12);

    private static double AttackToSustain(float[] x)
    {
        int a = Rate / 10, b = 3 * Rate / 10;
        return PeakDb(x, 15) - 10 * Math.Log10(x.Skip(a).Take(b - a).Average(v => (double)v * v) + 1e-24);
    }

    private static double InitialSlope(float[] x)
    {
        const int w = Rate / 200;
        var env = Enumerable.Range(0, x.Length / w).Select(i => 10 * Math.Log10(x.Skip(i * w).Take(w).Average(v => (double)v * v) + 1e-24)).ToArray();
        var ip = Array.IndexOf(env, env.Take(6).Max());
        var y = env.Skip(ip).Take(40).ToArray();
        double mt = (y.Length - 1) / 2.0, my = y.Average();
        return y.Select((v, i) => (i - mt) * (v - my)).Sum() / y.Select((_, i) => (i - mt) * (i - mt)).Sum() * 200;   // dB per second
    }

    [Theory]
    [InlineData(24, 52, 13.3, -20.9, 15.4)]
    [InlineData(25, 52, 13.4, -19.4, 13.6)]
    [InlineData(26, 52, 12.1, -18.7, 16.4)]
    [InlineData(27, 52, 16.8, -26.8, 17.1)]
    [InlineData(28, 52, 25.3, -85.7, 16.0)]
    [InlineData(29, 52, 6.6, -1.1, -1.5)]   // the captured amp squeezes like the real one (real amp output 7.7 dB, attack peak std 1.2 dB)
    [InlineData(30, 52, 6.6, -1.1, -1.5)]
    [InlineData(31, 52, 11.8, -21.0, 17.1)]
    [InlineData(32, 33, 6.9, -32.6, 17.4)]
    [InlineData(33, 33, 11.4, -12.0, 16.1)]
    [InlineData(34, 33, 9.5, -19.0, 17.8)]
    [InlineData(35, 33, 10.8, -16.1, 16.4)]
    [InlineData(36, 33, 14.4, -31.5, 12.9)]
    [InlineData(37, 33, 16.2, -27.6, 15.5)]
    public void EveryNotePeaksAboveItsSustain(int program, int note, double attack, double slope, double range)
    {
        var x = Struck(program, note, 110);
        var (a, s) = (AttackToSustain(x), InitialSlope(x));
        var r = PeakDb(Struck(program, note, 127), 15) - PeakDb(Struck(program, note, 50), 15);
        output.WriteLine($"program {program}: attack-to-sustain {a:F1} dB, initial slope {s:F1} dB/s, attack peak 127 - 50 {r:F1} dB");
        Assert.InRange(a, attack - 2, attack + 2);
        Assert.InRange(s, slope - 8, slope + 8);
        Assert.True(r > range - 2, $"peak range {r:F1} dB");
    }

    [Fact]
    public void TheDiPeaksLikeTheMeasuredGuitarDi()
    {
        var a = AttackToSustain(Di(29, [(0, 0x90, 52, 110), (1.0, 0x80, 52, 0)], Rate));
        output.WriteLine($"DI 29: attack-to-sustain {a:F1} dB (real DI 17.7)");
        Assert.InRange(a, 17.7 - 2, 17.7 + 2);
    }

    [Theory]
    [InlineData(24, 10.7)]
    [InlineData(25, 8.7)]
    [InlineData(26, 5.1)]
    [InlineData(27, 9.6)]
    [InlineData(29, 6.5)]
    [InlineData(30, 6.5)]
    public void AStrumPeaksAboveItsSustain(int program, double attack)
    {
        var a = AttackToSustain(Struck(program, 0, 110, 40, 47, 52, 56, 59, 64));
        output.WriteLine($"program {program}: strum attack-to-sustain {a:F1} dB");
        Assert.InRange(a, attack - 2, attack + 2);
    }
}

