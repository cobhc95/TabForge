namespace TabForge.Sf2Synth.Guitar;

// Owns: a feed-forward compressor: peak detector (attack/release), soft-knee gain computer in dB, make-up gain. Mono, in place,
//   no allocation. Used as the bass DI compressor and as the clean amp's gentle tube-style squeeze. With SplitHz set (bass only)
//   it runs two bands: the lows at/below SplitHz get their own gentler, slower path (sustain weight) and the rest the main fast path
//   (attack definition); guitars use the single-band path unchanged.
// Does not own: its values (Voicings), where it sits in the chain (GuitarBus).
// Tests: GuitarLayerTests.

// Threshold dBFS, ratio, attack and release ms, make-up dB. SplitHz = 0: single band. Else the low band uses Sub*.
internal readonly record struct CompSpec(float Threshold, float Ratio, double AttackMs, double ReleaseMs, float Makeup)
{
    public double SplitHz { get; init; }
    public float SubThreshold { get; init; } = -26;
    public float SubRatio { get; init; } = 2;
    public double SubAttackMs { get; init; } = 2;
    public double SubReleaseMs { get; init; } = 250;
}
internal sealed class Compressor
{
    private const float Knee = 6, DbPerLn = 8.685889f, LnPerDb = 0.11512925f;
    private float _thr, _slope, _makeup, _att, _rel, _env, _thr2, _slope2, _att2, _rel2, _env2, _lp, _a;
    private bool _on, _split;

    public void Configure(CompSpec? spec, double rate)
    {
        _on = spec is not null;
        if (spec is not { } s) return;
        (_thr, _slope, _makeup) = (s.Threshold, 1 - 1 / s.Ratio, s.Makeup);
        _att = (float)(1 - Math.Exp(-1 / (s.AttackMs * 0.001 * rate)));
        _rel = (float)(1 - Math.Exp(-1 / (s.ReleaseMs * 0.001 * rate)));
        _split = s.SplitHz > 0;
        if (_split)
        {
            _a = (float)(1 - Math.Exp(-2 * Math.PI * s.SplitHz / rate));
            (_thr2, _slope2) = (s.SubThreshold, 1 - 1 / s.SubRatio);
            _att2 = (float)(1 - Math.Exp(-1 / (s.SubAttackMs * 0.001 * rate)));
            _rel2 = (float)(1 - Math.Exp(-1 / (s.SubReleaseMs * 0.001 * rate)));
        }
        _env = _env2 = _lp = 0;
    }

    public void Reset() => _env = _env2 = _lp = 0;

    public void Process(Span<float> x)
    {
        if (!_on) return;
        if (!_split)
        {
            for (var n = 0; n < x.Length; n++)
            {
                var a = MathF.Abs(x[n]);
                _env += (a > _env ? _att : _rel) * (a - _env);
                var over = MathF.Log(_env + 1e-9f) * DbPerLn - _thr;
                var cut = over <= -Knee / 2 ? 0 : over >= Knee / 2 ? _slope * over : _slope * (over + Knee / 2) * (over + Knee / 2) / (2 * Knee);
                x[n] *= MathF.Exp((_makeup - cut) * LnPerDb);
            }
            return;
        }
        for (var n = 0; n < x.Length; n++)
        {
            _lp += _a * (x[n] - _lp);
            float lo = _lp, hi = x[n] - lo;
            var alo = MathF.Abs(lo);
            _env2 += (alo > _env2 ? _att2 : _rel2) * (alo - _env2);
            var overLo = MathF.Log(_env2 + 1e-9f) * DbPerLn - _thr2;
            var cutLo = overLo <= -Knee / 2 ? 0 : overLo >= Knee / 2 ? _slope2 * overLo : _slope2 * (overLo + Knee / 2) * (overLo + Knee / 2) / (2 * Knee);
            var ahi = MathF.Abs(hi);
            _env += (ahi > _env ? _att : _rel) * (ahi - _env);
            var overHi = MathF.Log(_env + 1e-9f) * DbPerLn - _thr;
            var cutHi = overHi <= -Knee / 2 ? 0 : overHi >= Knee / 2 ? _slope * overHi : _slope * (overHi + Knee / 2) * (overHi + Knee / 2) / (2 * Knee);
            x[n] = lo * MathF.Exp((_makeup - cutLo) * LnPerDb) + hi * MathF.Exp((_makeup - cutHi) * LnPerDb);
        }
    }
}
