namespace TabForge.Sf2Synth.Dsp;

// Owns: streaming sample-rate conversion at any ratio: 32-tap Kaiser-windowed sinc (beta 8, cut-off 0.45 of the lower rate), 512
//   phases interpolated linearly; Write input, Read what is ready. Latency 16 input samples. Also a one-shot Convert for files.
// Does not own: where it runs (Nam/NamAmp, Dsp/WavIr).
// Tests: NamTests.
internal sealed class Resampler
{
    private const int Taps = 32, Half = Taps / 2, Phases = 512, Size = 1 << 14;
    private const double Beta = 8;

    private readonly float[] _table = new float[(Phases + 1) * Taps], _in = new float[Size];
    private readonly double _step;
    private long _written;
    private double _pos;

    public Resampler(double from, double to)
    {
        _step = from / to;
        var fc = 0.45 * Math.Min(1, to / from);   // cycles per input sample
        for (var p = 0; p <= Phases; p++)
            for (var m = 0; m < Taps; m++)
            {
                var t = (double)p / Phases + Half - 1 - m;   // output time minus tap time, in input samples
                var sinc = Math.Abs(t) < 1e-9 ? 2 * fc : Math.Sin(2 * Math.PI * fc * t) / (Math.PI * t);
                var r = t / Half;
                _table[p * Taps + m] = Math.Abs(r) >= 1 ? 0 : (float)(sinc * I0(Beta * Math.Sqrt(1 - r * r)) / I0(Beta));
            }
        Reset();
    }

    public static double I0(double x)
    {
        double sum = 1, term = 1;
        for (var k = 1; k < 32; k++) { term *= x * x / (4.0 * k * k); sum += term; }
        return sum;
    }

    public void Reset() { Array.Clear(_in); (_written, _pos) = (0, 0); }

    public void Write(ReadOnlySpan<float> x)
    {
        foreach (var v in x) _in[_written++ & (Size - 1)] = v;
    }

    public int Read(Span<float> y)
    {
        var n = 0;
        while (n < y.Length && (long)_pos + Half < _written)
        {
            var i = (long)_pos;
            var f = (_pos - i) * Phases;
            var p = (int)f;
            var a = (float)(f - p);
            int t0 = p * Taps, t1 = t0 + Taps;
            float s = 0;
            var start = i - Half + 1;
            for (var m = 0; m < Taps; m++)
            {
                var h = _table[t0 + m] + a * (_table[t1 + m] - _table[t0 + m]);
                s += h * _in[(start + m) & (Size - 1)];
            }
            y[n++] = s;
            _pos += _step;
        }
        return n;
    }

    // Whole signal, time-aligned, length scaled by the ratio.
    public static float[] Convert(float[] x, double from, double to)
    {
        if (Math.Abs(from - to) < 0.5) return x;
        var r = new Resampler(from, to);
        var y = new float[(int)Math.Ceiling(x.Length * to / from)];
        var padded = new float[x.Length + Taps];
        x.CopyTo(padded, 0);
        var done = 0;
        for (var at = 0; at < padded.Length; at += Size / 2)
        {
            r.Write(padded.AsSpan(at, Math.Min(Size / 2, padded.Length - at)));
            done += r.Read(y.AsSpan(done));
        }
        return y;
    }
}
