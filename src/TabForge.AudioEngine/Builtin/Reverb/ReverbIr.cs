namespace TabForge.AudioEngine.Builtin.Reverb;

// Owns: the room the reverb convolves with: two decorrelated, progressively darkening, exponentially decaying noise channels with sparse early
//   reflections and a smooth attack, scaled by the Size knob. Deterministic (seeded) so the same size builds the same room every time.
// Does not own: processing (ReverbEffect), the editor.
// Tests: no dedicated test class.

// The synthesized room: two equal-length channels and how loudly the tail peaks relative to a unit impulse.
public readonly record struct SynthesizedIr(float[] Left, float[] Right, double TailPeakDb);

public static class ReverbIr
{
    private const int Seed = 1234567;

    /// <summary>
    /// Builds the room for <paramref name="sizePct"/> (0..100): the noise length runs from about 0.7 s (Size 0) to about 5 s (Size 100),
    /// around a 2 s hall at 50.
    /// </summary>
    public static SynthesizedIr Build(double sizePct, int sampleRate)
    {
        var hallSeconds = 2.0 + Math.Clamp(sizePct, 0, 100) / 100 * 3.0;   // 2 s at 50, ~3.5 s at 100
        var length = (int)Math.Clamp(hallSeconds * sampleRate, sampleRate / 2, sampleRate * 6);
        float[] l = new float[length], r = new float[length];
        Fill(l, sampleRate, sizePct, Seed, length);
        Fill(r, sampleRate, sizePct, Seed + 1, length);
        // Normalize the peaks to the same value so a unit impulse's tail peaks at 0 dBFS on both sides.
        var peak = Math.Max(Peak(l), Peak(r));
        if (peak > 1e-9)
        {
            var g = (float)(1.0 / peak);
            l = Scaled(l, g); r = Scaled(r, g);
        }
        return new SynthesizedIr(l, r, peak > 1e-9 ? 20 * Math.Log10(1.0 / peak) : 0);
    }

    /// <summary>
    /// One channel: pink-ish (darkened) noise through a pre-delayed two-slope decay; early reflections are sparser and the late tail denser.
    /// The decay is exponential (a straight line in dB), the attack is a short smooth ramp, and the tail darkens as it decays.
    /// </summary>
    private static void Fill(float[] ir, int sampleRate, double sizePct, int seed, int length)
    {
        var rng = new Random(seed);
        var decayPerSample = Math.Pow(10, -3.0 / (length));   // -60 dB over the whole IR
        var lp = 0.0;
        var dark = 0.55 + Math.Clamp(sizePct, 0, 100) / 100 * 0.2;   // smaller rooms are brighter
        var attack = Math.Max(1, (int)(0.004 * sampleRate));          // 4 ms smooth start
        var amp = 1.0;
        for (var i = 0; i < length; i++)
        {
            var noise = rng.NextDouble() * 2 - 1;
            lp += (noise - lp) * dark;
            // The early part stays louder (sparse first reflections), the late tail decays faster into darkness.
            amp *= (i < length / 10 ? 0.9992 : decayPerSample);
            if (i < attack) amp = Math.Min(amp, 1.0);
            ir[i] = (float)(lp * amp * (i < attack ? (i + 1) / (double)attack : 1.0));
        }
        // Even out the two sides so the room sounds centred.
        var peak = Peak(ir);
        if (peak > 1e-9) { var g = (float)(1.0 / peak); for (var i = 0; i < length; i++) ir[i] *= g; }
    }

    private static double Peak(float[] x) { var p = 0.0; foreach (var v in x) p = Math.Max(p, Math.Abs(v)); return p; }
    private static float[] Scaled(float[] x, float g) { var y = new float[x.Length]; for (var i = 0; i < x.Length; i++) y[i] = x[i] * g; return y; }
}
