namespace TabForge.Sf2Synth.Guitar;

// Owns: the post-amp transient expander of the distorted programs: a sidechain on the bus AFTER the captured amp. Two
//   energy envelopes (fast 2 ms, slow 100 ms; the capture's output takes tens of ms to build, so the slow one must lag
//   well behind it) of the amp output; when the fast one outruns the slow one (an onset) the gain gets a bump of up to
//   +4 dB that falls away in ~60 ms, so the first 10 ms peak clearly above the 100-300 ms sustain
//   the capture alone leaves flat (its squeeze equalises a harder DI transient: input alone cannot win against it).
//   Sustain and silence sit at unity (plus a fixed -0.5 dB make-up so the phrase rms does not move). Mono, in place, no
//   allocation, deterministic.
// Does not own: the amp (GuitarBus), the values' home (fixed below; the distorted programs only), the DI (unchanged).
// Tests: GuitarLayerTests.
internal sealed class TransientExpander
{
    private const double FastMs = 2, SlowMs = 100, GainAttackMs = 1, HoldMs = 50;
    private const float MaxBumpDb = 4, DeadDb = 0.5f, MakeupDb = -0.5f, DbPerLn = 4.3429448f, LnPerDb = 0.11512925f;

    private float _fast, _slow, _gain, _hold, _cF, _cS, _gAtt, _holdFall;

    public void Configure(double rate)
    {
        _cF = (float)(1 - Math.Exp(-1 / (FastMs * 0.001 * rate)));
        _cS = (float)(1 - Math.Exp(-1 / (SlowMs * 0.001 * rate)));
        _gAtt = (float)(1 - Math.Exp(-1 / (GainAttackMs * 0.001 * rate)));
        _holdFall = (float)Math.Exp(-1 / (HoldMs * 0.001 * rate));
        Reset();
    }

    public void Reset() => (_fast, _slow, _gain, _hold) = (0, 0, 0, 0);

    public void Process(Span<float> x)
    {
        float f = _fast, s = _slow, g = _gain, hold = _hold, cF = _cF, cS = _cS, gA = _gAtt, hf = _holdFall;
        for (var n = 0; n < x.Length; n++)
        {
            var e = x[n] * x[n];
            f += cF * (e - f);
            s += cS * (e - s);
            var raw = (MathF.Log(f + 1e-12f) - MathF.Log(s + 1e-12f)) * DbPerLn;
            var target = raw <= DeadDb ? 0 : raw >= MaxBumpDb + DeadDb ? MaxBumpDb : raw - DeadDb;
            hold = MathF.Max(target, hold * hf);   // the bump holds, then dies in ~50 ms whatever the envelopes do
            g += gA * (hold - g);
            x[n] *= MathF.Exp((MakeupDb + g) * LnPerDb);
        }
        (_fast, _slow, _gain, _hold) = (f, s, g, hold);
    }
}
