using TabForge.Sf2Synth.Dsp;

namespace TabForge.Sf2Synth.Guitar;

// Owns: the acoustic body as a modal bank: the bridge signal goes straight to the top (Direct) plus one band-pass resonator per
//   body mode (Helmholtz air, top, back, higher plate modes), summed in parallel, so every attack knocks the body and every note is
//   coloured by its modes. Mono, in place, no allocation.
// Does not own: the mode values (Voicings), the string (PluckedString), the rest of the rig (GuitarBus).
// Tests: GuitarLayerTests.

// Gain is the mode's peak relative to the direct path.
internal readonly record struct ModeSpec(double Hz, double Q, float Gain);

internal sealed record BodySpec(float Direct, ModeSpec[] Modes);

internal sealed class BodyModes
{
    public const int Max = 10;
    private readonly Biquad[] _m = new Biquad[Max];
    private readonly float[] _g = new float[Max];
    private int _n;
    private float _direct;

    public void Configure(BodySpec? body, double rate)
    {
        _n = Math.Min(body?.Modes.Length ?? 0, Max);
        _direct = body?.Direct ?? 1;
        for (var i = 0; i < _n; i++)
        {
            var m = body!.Modes[i];
            _m[i].C = BiquadCoefs.Of(new EqSpec(EqKind.BandPass, m.Hz, m.Q), rate);
            _g[i] = m.Gain;
        }
        Reset();
    }

    public void Reset() { for (var i = 0; i < Max; i++) _m[i].Reset(); }

    public void Process(Span<float> x)
    {
        if (_n == 0) return;
        var m = _m.AsSpan(0, _n);
        var g = _g.AsSpan(0, _n);
        for (var n = 0; n < x.Length; n++)
        {
            var s = x[n];
            var y = _direct * s;
            for (var i = 0; i < m.Length; i++) y += g[i] * m[i].Process(s);
            x[n] = y;
        }
    }
}
