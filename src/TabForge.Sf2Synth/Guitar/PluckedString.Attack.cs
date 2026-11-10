using TabForge.Sf2Synth.Dsp;

namespace TabForge.Sf2Synth.Guitar;

// Owns: how a string is set going and what rides on top of it: the initial shape (triangle at the pick point + pick-combed,
//   velocity-filtered noise; a narrow struck bump for slap; a sine for harmonics), the pick click (high-passed noise, 1.5 ms),
//   the tap (noise through a band-pass: fingertip, pick snap, thumb thump, upright knock) and the fretless mwah (a band-pass that
//   follows the pitch and falls with the note). Click/tap strength follows velocity^ClickVel / velocity^TapVel (1 on guitars).
// Does not own: the waveguide loop (PluckedString), the values (Voicings).
// Tests: GuitarLayerTests.
internal sealed partial class PluckedString
{
    private const float Click = 0.06f, MwahGain = 0.8f;
    private const double ClickSeconds = 0.0015, MwahSeconds = 0.3, MwahQ = 2.5, MwahRise = 1.5;

    private float _click, _clickX, _tap, _tapFall, _mwGain;
    private Biquad _tapF, _mw;
    private uint _noise = 1;

    private void Excite(double v, float amp, uint seed, float[] scratch)
    {
        var n = Math.Min(_d, scratch.Length);
        var pick = Math.Clamp((int)Math.Round(Math.Min(0.5, _spec.PickMm * (1 + _nu.Pick) / 1000 / _f.Length) * n), 1, n - 1);
        var c = (float)(1 - Math.Exp(-2 * Math.PI * _spec.PluckHz * (0.25 + 0.75 * Math.Pow(v, 1.2)) / _rate));
        var mix = (float)_spec.NoiseMix;
        var width = Math.Max(2f, n * (0.06f - 0.03f * (float)v));   // slap: a harder thumb is a narrower bump
        float y = 0, mean = 0;
        var state = seed | 1;
        for (var i = 0; i < n; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;   // xorshift32
            y += c * ((state & 0xFFFFFF) / (float)0x800000 - 1 - y);
            scratch[i] = y;
        }
        for (var i = 0; i < n; i++)
        {
            var shape = _spec.Slap ? (MathF.Abs(i - pick) < width ? 0.5f + 0.5f * MathF.Cos(MathF.PI * (i - pick) / width) : 0)
                : i < pick ? (float)i / pick : (float)(n - i) / (n - pick);
            float e = _spec.Harmonic
                ? MathF.Sin(2 * MathF.PI * i / n) + 0.15f * MathF.Sin(4 * MathF.PI * i / n)
                : (1 - mix) * shape + mix * 0.5f * (scratch[i] - scratch[(i + pick) % n]);
            scratch[i] = e;
            mean += e;
        }
        mean /= n;
        for (var i = 0; i < n; i++) _buf[(_w - n + i) & _mask] += amp * (scratch[i] - mean);
        var on = !_spec.Harmonic;
        var cv = _spec.ClickVel == 1 ? (float)v : (float)Math.Pow(v, _spec.ClickVel);
        var tv = _spec.TapVel == 1 ? (float)v : (float)Math.Pow(v, _spec.TapVel);
        (_click, _noise, _clickX) = (on ? Click * _spec.Click * amp * cv : 0, state, 0);
        var t = _spec.Tap;
        _tap = on && t.Amount > 0 ? t.Amount * amp * tv : 0;
        if (_tap > 0)
        {
            _tapF.C = BiquadCoefs.Of(new EqSpec(EqKind.BandPass, t.Hz, t.Q), _rate);
            _tapF.Reset();
            _tapFall = (float)Math.Exp(-1 / (t.Ms * 0.001 * _rate));
        }
        _mwGain = _spec.Mwah > 0 ? MwahGain : 0;
    }

    // Once per block: centre = Mwah × f0, a little higher while the note is fresh.
    private void Mwah()
    {
        var hz = _spec.Mwah * _hz * Math.Pow(2, _glideSemis / 12) * (1 + MwahRise * Math.Exp(-Age / MwahSeconds));
        _mw.C = BiquadCoefs.Of(new EqSpec(EqKind.BandPass, hz, MwahQ), _rate);
    }

    private void AddTransients(Span<float> output)
    {
        float clickFall = (float)Math.Exp(-1 / (ClickSeconds * _rate)), click = _click, tap = _tap, fall = _tapFall, last = _clickX;
        var f = _tapF;
        var s = _noise;
        for (var i = 0; i < output.Length; i++)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            var x = (s & 0xFFFFFF) / (float)0x800000 - 1;
            output[i] += click * (x - last) + tap * f.Process(x);
            (last, click, tap) = (x, click * clickFall, tap * fall);
        }
        (_noise, _clickX, _click, _tap, _tapF) = (s, last, click, tap, f);
    }
}
