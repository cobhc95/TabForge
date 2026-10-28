namespace TabForge.AudioEngine.Builtin.Dsp;

// Owns: the second-order filter section: RBJ cookbook coefficients (peak, shelves, high-pass, low-pass), the section's state, and its magnitude response
//   computed from the coefficients (what the EQ curve draws, so the picture is the filter).
// Does not own: which bands an effect has (EqDesign), smoothing of the controls (Smoothed).
// Tests: BuiltinEqTests.

/// <summary>Normalised biquad coefficients (a0 = 1): y = b0 x + b1 x1 + b2 x2 - a1 y1 - a2 y2.</summary>
public readonly record struct BiquadCoef(double B0, double B1, double B2, double A1, double A2)
{
    public const double Butterworth = 0.70710678118654757;

    public static BiquadCoef Identity => new(1, 0, 0, 0, 0);

    private static (double Cos, double Alpha) Common(double sampleRate, double hz, double q)
    {
        var w = 2 * Math.PI * Math.Clamp(hz, 1, sampleRate * 0.49) / sampleRate;
        return (Math.Cos(w), Math.Sin(w) / (2 * Math.Max(q, 0.05)));
    }

    private static BiquadCoef Make(double b0, double b1, double b2, double a0, double a1, double a2) => new(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);

    public static BiquadCoef Peak(double sampleRate, double hz, double gainDb, double q)
    {
        var (cos, alpha) = Common(sampleRate, hz, q);
        var a = Math.Pow(10, gainDb / 40);
        return Make(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    public static BiquadCoef LowShelf(double sampleRate, double hz, double gainDb, double q)
    {
        var (cos, alpha) = Common(sampleRate, hz, q);
        var a = Math.Pow(10, gainDb / 40);
        var beta = 2 * Math.Sqrt(a) * alpha;
        return Make(a * ((a + 1) - (a - 1) * cos + beta), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - beta),
            (a + 1) + (a - 1) * cos + beta, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - beta);
    }

    public static BiquadCoef HighShelf(double sampleRate, double hz, double gainDb, double q)
    {
        var (cos, alpha) = Common(sampleRate, hz, q);
        var a = Math.Pow(10, gainDb / 40);
        var beta = 2 * Math.Sqrt(a) * alpha;
        return Make(a * ((a + 1) + (a - 1) * cos + beta), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - beta),
            (a + 1) - (a - 1) * cos + beta, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - beta);
    }

    public static BiquadCoef HighPass(double sampleRate, double hz, double q)
    {
        var (cos, alpha) = Common(sampleRate, hz, q);
        return Make((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static BiquadCoef LowPass(double sampleRate, double hz, double q)
    {
        var (cos, alpha) = Common(sampleRate, hz, q);
        return Make((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>The section's gain in dB at <paramref name="hz"/>, from the coefficients.</summary>
    public double MagnitudeDb(double hz, double sampleRate)
    {
        var w = 2 * Math.PI * Math.Clamp(hz, 1, sampleRate * 0.499) / sampleRate;
        double c1 = Math.Cos(w), c2 = Math.Cos(2 * w);
        var num = B0 * B0 + B1 * B1 + B2 * B2 + 2 * (B0 * B1 + B1 * B2) * c1 + 2 * B0 * B2 * c2;
        var den = 1 + A1 * A1 + A2 * A2 + 2 * (A1 + A1 * A2) * c1 + 2 * A2 * c2;
        return 10 * Math.Log10(Math.Max(num, 1e-30) / Math.Max(den, 1e-30));
    }
}

/// <summary>The memory of one channel through one section (transposed direct form II, double precision, denormals flushed).</summary>
public struct BiquadState
{
    private double _z1, _z2;

    public double Process(in BiquadCoef c, double x)
    {
        var y = c.B0 * x + _z1;
        _z1 = DspMath.Flush(c.B1 * x - c.A1 * y + _z2);
        _z2 = DspMath.Flush(c.B2 * x - c.A2 * y);
        return y;
    }

    public void Reset() => _z1 = _z2 = 0;
}
