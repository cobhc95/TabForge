using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Eq;

// Owns: the EQ low cut or high cut on the audio thread: one section (12 dB per octave) or two (24 dB), faded in and out with the slope and glided with the
//   frequency, so turning it on, off or changing its slope never clicks. Off costs nothing.
// Does not own: the design (EqDesign), the knob values (EqEffect.Process.cs).
// Tests: BuiltinEqTests.
internal sealed class EqCut
{
    private const double GlideMs = 15, FadeMs = 10;
    private readonly bool _high;
    private readonly double _rate;
    private Smoothed _freq, _q1, _on, _second;
    private BiquadCoef _c1 = BiquadCoef.Identity, _c2 = BiquadCoef.Identity;
    private BiquadState _l1, _r1, _l2, _r2;
    private bool _dirty = true;

    public EqCut(bool high, double sampleRate, double hz)
    {
        (_high, _rate) = (high, sampleRate);
        _freq = new Smoothed(hz, GlideMs, sampleRate);
        _q1 = new Smoothed(BiquadCoef.Butterworth, GlideMs, sampleRate);
        _on = new Smoothed(0, FadeMs, sampleRate);
        _second = new Smoothed(0, FadeMs, sampleRate);
    }

    public void Aim(double hz, double slope)
    {
        _freq.Target = hz;
        _on.Target = slope >= EqEffect.Slope12 ? 1 : 0;
        _second.Target = slope >= EqEffect.Slope24 ? 1 : 0;
        _q1.Target = EqDesign.CutQ1(slope >= EqEffect.Slope24);
    }

    private bool Idle => _on.Value == 0 && _on.Target == 0;
    private bool SecondIdle => _second.Value == 0 && _second.Target == 0;

    public void Advance(int samples)
    {
        if (Idle) { _l1.Reset(); _r1.Reset(); _l2.Reset(); _r2.Reset(); _dirty = true; return; }
        if (!(_freq.Settled && _q1.Settled) || _dirty)
        {
            var hz = _freq.Advance(samples);
            _c1 = EqDesign.Cut(_rate, _high, hz, _q1.Advance(samples));
            _c2 = EqDesign.Cut(_rate, _high, hz, EqDesign.Cut24Q2);
            _dirty = false;
        }
        if (SecondIdle) { _l2.Reset(); _r2.Reset(); }
    }

    public void Process(ref double left, ref double right)
    {
        if (Idle) return;
        var on = _on.Next();
        var second = _second.Next();
        var l = _l1.Process(_c1, left);
        var r = _r1.Process(_c1, right);
        if (!SecondIdle || second > 0)
        {
            var l2 = _l2.Process(_c2, l);
            var r2 = _r2.Process(_c2, r);
            l += (l2 - l) * second;
            r += (r2 - r) * second;
        }
        left += (l - left) * on;
        right += (r - right) * on;
    }
}
