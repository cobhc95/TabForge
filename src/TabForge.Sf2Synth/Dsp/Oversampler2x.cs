namespace TabForge.Sf2Synth.Dsp;

// Owns: 2x up/down sampling around a nonlinearity: 63-tap Kaiser-windowed sinc (beta 8, ~80 dB stop band, pass band to 0.85 of
//   the base Nyquist), polyphase up, decimating down.
// Does not own: what runs at the doubled rate (Guitar/StageAmp).
// Tests: GuitarLayerTests.
internal sealed class Oversampler2x
{
    private const int Taps = 63;
    private const double Cutoff = 0.25 * 0.85, Beta = 8;   // cycles per sample at the doubled rate

    private readonly float[] _even, _odd, _down;
    private readonly FirHistory _up, _dn;

    public Oversampler2x()
    {
        var h = Design();
        var half = FirHistory.Pad((Taps + 1) / 2);
        _even = new float[half];
        _odd = new float[half];
        for (var k = 0; k < Taps; k++) (k % 2 == 0 ? _even : _odd)[k / 2] = 2 * h[k];
        _down = new float[FirHistory.Pad(Taps)];
        Array.Copy(h, _down, Taps);
        _up = new FirHistory(half);
        _dn = new FirHistory(_down.Length);
    }

    private static float[] Design()
    {
        var mid = (Taps - 1) / 2.0;
        var v = new double[Taps];
        for (var n = 0; n < Taps; n++)
        {
            var t = n - mid;
            var sinc = t == 0 ? 2 * Cutoff : Math.Sin(2 * Math.PI * Cutoff * t) / (Math.PI * t);
            v[n] = sinc * I0(Beta * Math.Sqrt(1 - t / mid * (t / mid))) / I0(Beta);
        }
        var sum = v.Sum();
        return v.Select(x => (float)(x / sum)).ToArray();
    }

    private static double I0(double x)
    {
        double sum = 1, term = 1;
        for (var k = 1; k < 32; k++) { term *= x * x / (4.0 * k * k); sum += term; }
        return sum;
    }

    public void Up(ReadOnlySpan<float> input, Span<float> doubled)
    {
        for (var i = 0; i < input.Length; i++)
        {
            _up.Push(input[i]);
            doubled[2 * i] = _up.Dot(_even);
            doubled[2 * i + 1] = _up.Dot(_odd);
        }
    }

    public void Down(ReadOnlySpan<float> doubled, Span<float> output)
    {
        for (var i = 0; i < output.Length; i++)
        {
            _dn.Push(doubled[2 * i]);
            _dn.Push(doubled[2 * i + 1]);
            output[i] = _dn.Dot(_down);
        }
    }

    public void Reset() { _up.Clear(); _dn.Clear(); }
}
