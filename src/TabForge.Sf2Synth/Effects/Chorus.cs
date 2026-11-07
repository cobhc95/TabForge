namespace TabForge.Sf2Synth.Effects;

// Owns: the chorus behind the SF2 chorus send (CC93): one delay line, two sine-modulated taps (left/right in quadrature), linear interpolation.
// Does not own: the send levels (Voice).
// Tests: Sf2SynthTests.
internal sealed class Chorus(double rate)
{
    private const double BaseSeconds = 0.012, DepthSeconds = 0.004, Hz = 0.4;
    private const float Wet = 0.5f;

    private readonly float[] _buf = new float[(int)((BaseSeconds + DepthSeconds) * rate) + 4];
    private int _pos;
    private double _phase;

    public void Process(ReadOnlySpan<float> input, Span<float> left, Span<float> right)
    {
        var inc = 2 * Math.PI * Hz / rate;
        for (var i = 0; i < input.Length; i++)
        {
            _buf[_pos] = input[i];
            left[i] += Wet * Tap(Math.Sin(_phase));
            right[i] += Wet * Tap(Math.Cos(_phase));
            _phase += inc;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            if (++_pos == _buf.Length) _pos = 0;
        }
    }

    private float Tap(double lfo)
    {
        var delay = (BaseSeconds + DepthSeconds * 0.5 * (1 + lfo)) * rate;
        var p = _pos - delay;
        while (p < 0) p += _buf.Length;
        var k = (int)p;
        var f = (float)(p - k);
        return _buf[k] + f * (_buf[(k + 1) % _buf.Length] - _buf[k]);
    }

    public void Reset() { Array.Clear(_buf); _pos = 0; _phase = 0; }
}
