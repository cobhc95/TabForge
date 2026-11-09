namespace TabForge.Sf2Synth.Humanize;

// Owns: the seeded random sources, all pure functions of a key: Hash (mix of integers; uniform, unit-variance near-Gaussian),
//   Value (smooth value noise of unit std, keyed by position) and Octaves (a sum of value noises, one per octave of period, so a
//   slow 1/f-like drift is known at any position without running state); Rng (xorshift stream for the strings of one strum).
// Does not own: what the noise drives (Dynamics, Timing, Strum), the amounts (HumanTable).
// Tests: HumanizerTests.
internal static class Hash
{
    public static uint Of(uint a, int b, int c = 0, int d = 0)
    {
        var h = a ^ (uint)b * 0x9E3779B1u;
        h = (h ^ h >> 15) * 0x85EBCA6Bu ^ (uint)c * 0xC2B2AE35u;
        h = (h ^ h >> 13) * 0x2C1B3C6Du ^ (uint)d * 0x27D4EB2Fu;
        h = (h ^ h >> 16) * 0x7FEB352Du;
        return h ^ h >> 15;
    }

    /// <summary>Uniform -1..1.</summary>
    public static double U(uint h) => (h & 0xFFFFFF) / (double)0x800000 - 1;
    public static double U01(uint h) => (h & 0xFFFFFF) / (double)0x1000000;

    /// <summary>Unit variance, bounded at 3.5 sigma.</summary>
    public static double G(uint h) => (U(h) + U(Of(h, 1)) + U(Of(h, 2)) + U(Of(h, 3))) * 0.8660254037844386;

    /// <summary>Smooth noise of period 1 at <paramref name="x"/>: Gaussian values at the integers, smoothstep between; unit std.</summary>
    public static double Value(uint seed, double x)
    {
        var i = Math.Floor(x);
        var f = x - i;
        var s = f * f * (3 - 2 * f);
        var n = (int)(long)i;
        return (G(Of(seed, n)) * (1 - s) + G(Of(seed, n + 1)) * s) * 1.1601;   // 1 / sqrt(0.743): the smoothstep blend's variance
    }

    /// <summary>Octaves of <see cref="Value"/>: periods <paramref name="p0"/> x 2^i (i &lt; n), amplitude period^<paramref name="slope"/>; unit std.</summary>
    public static double Octaves(uint seed, double x, double p0, int n, double slope)
    {
        double sum = 0, norm = 0;
        for (var i = 0; i < n; i++)
        {
            var p = p0 * (1 << i);
            var a = Math.Pow(p / p0, slope);
            sum += a * Value(Of(seed, i, 77), x / p);
            norm += a * a;
        }
        return sum / Math.Sqrt(norm);
    }
}

internal sealed class Rng(uint seed)
{
    private uint _s = seed | 1;

    public double U() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (double)0x800000 - 1; }
    public double U01() => 0.5 + 0.5 * U();
    public double G() => (U() + U() + U() + U()) * 0.8660254037844386;
}
