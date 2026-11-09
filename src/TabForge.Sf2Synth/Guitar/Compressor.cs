namespace TabForge.Sf2Synth.Guitar;

// Owns: a feed-forward compressor: peak detector (attack/release), soft-knee gain computer in dB, make-up gain. Mono, in place,
//   no allocation. Used as the bass DI compressor and as the clean amp's gentle tube-style squeeze.
// Does not own: its values (Voicings), where it sits in the chain (GuitarBus).
// Tests: GuitarLayerTests.

// Threshold dBFS, ratio, attack and release ms, make-up dB.
internal readonly record struct CompSpec(float Threshold, float Ratio, double AttackMs, double ReleaseMs, float Makeup);

internal sealed class Compressor
{
    private const float Knee = 6, DbPerLn = 8.685889f, LnPerDb = 0.11512925f;
    private float _thr, _slope, _makeup, _att, _rel, _env;
    private bool _on;

    public void Configure(CompSpec? spec, double rate)
    {
        _on = spec is not null;
        if (spec is not { } s) return;
        (_thr, _slope, _makeup) = (s.Threshold, 1 - 1 / s.Ratio, s.Makeup);
        _att = (float)(1 - Math.Exp(-1 / (s.AttackMs * 0.001 * rate)));
        _rel = (float)(1 - Math.Exp(-1 / (s.ReleaseMs * 0.001 * rate)));
        _env = 0;
    }

    public void Reset() => _env = 0;

    public void Process(Span<float> x)
    {
        if (!_on) return;
        for (var n = 0; n < x.Length; n++)
        {
            var a = MathF.Abs(x[n]);
            _env += (a > _env ? _att : _rel) * (a - _env);
            var over = MathF.Log(_env + 1e-9f) * DbPerLn - _thr;
            var cut = over <= -Knee / 2 ? 0 : over >= Knee / 2 ? _slope * over : _slope * (over + Knee / 2) * (over + Knee / 2) / (2 * Knee);
            x[n] *= MathF.Exp((_makeup - cut) * LnPerDb);
        }
    }
}
