using TabForge.AudioEngine.Builtin.Dsp;

namespace TabForge.AudioEngine.Builtin.Eq;

// Owns: one EQ band on the audio thread: its frequency, gain and Q glide to the knob values, the coefficients are redesigned only while something moves,
//   and a band with no gain costs nothing (its memory is cleared so it starts clean when it is turned up).
// Does not own: the design (EqDesign), the chain of bands (EqEffect.Process.cs).
// Tests: BuiltinEqTests.
internal sealed class EqBand
{
    private const double GlideMs = 15;
    private readonly int _band;
    private readonly double _rate;
    private Smoothed _freq, _gain, _q;
    private bool _shelf, _dirty = true;
    private BiquadCoef _coef = BiquadCoef.Identity;
    private BiquadState _left, _right;

    public EqBand(int band, double sampleRate, double hz, double q, bool shelf)
    {
        (_band, _rate, _shelf) = (band, sampleRate, shelf);
        _freq = new Smoothed(hz, GlideMs, sampleRate);
        _gain = new Smoothed(0, GlideMs, sampleRate);
        _q = new Smoothed(q, GlideMs, sampleRate);
    }

    public bool Idle => _gain.Value == 0 && _gain.Target == 0;

    public void Aim(double hz, double gainDb, double q, bool shelf)
    {
        _freq.Target = hz;
        _gain.Target = gainDb;
        _q.Target = q;
        if (shelf != _shelf) { _shelf = shelf; _dirty = true; }
    }

    /// <summary>Moves the glides on by <paramref name="samples"/> and redesigns the section when a value moved.</summary>
    public void Advance(int samples)
    {
        if (_freq.Settled && _gain.Settled && _q.Settled && !_dirty) return;
        _coef = EqDesign.Band(_rate, _band, _freq.Advance(samples), _gain.Advance(samples), _q.Advance(samples), _shelf);
        _dirty = false;
        if (Idle) { _left.Reset(); _right.Reset(); }
    }

    public void Process(ref double left, ref double right)
    {
        if (Idle) return;
        left = _left.Process(_coef, left);
        right = _right.Process(_coef, right);
    }
}
