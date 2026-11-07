namespace TabForge.Sf2Synth.Guitar;

// Owns: the amp's nonlinear core (IAmpModel) and its analog stage model: asymmetric tube-style shapers with bias, coupling
//   high-pass and plate low-pass between stages, power-supply sag. Runs at the doubled rate.
// Does not own: oversampling, equalisers, cabinet (GuitarBus); stage values (Voicings).
// Tests: GuitarLayerTests.

// A learned model (e.g. an LSTM with weights loaded from a file) implements this and replaces StageAmp in GuitarBus.
internal interface IAmpModel
{
    void Configure(RigSpec rig, double doubledRate);
    void Reset();
    void Process(Span<float> block);
}

internal sealed class StageAmp : IAmpModel
{
    private const double SagAttack = 0.005, SagRelease = 0.1;
    private const float NegHeadroom = 1.5f;   // grid conduction clips the positive half first

    private struct Stage { public float Gain, Bias, Offset, HpA, HpX, HpY, LpB, LpY; }

    private readonly Stage[] _st = new Stage[Voicings.MaxStages];
    private int _n;
    private float _drive, _sag, _env, _att, _rel;

    public void Configure(RigSpec rig, double rate)
    {
        _n = Math.Min(rig.Stages.Length, _st.Length);
        _drive = rig.Drive;
        _sag = rig.Sag;
        _att = (float)(1 - Math.Exp(-1 / (SagAttack * rate)));
        _rel = (float)(1 - Math.Exp(-1 / (SagRelease * rate)));
        for (var i = 0; i < _n; i++)
        {
            var s = rig.Stages[i];
            _st[i] = new Stage
            {
                Gain = s.Gain, Bias = s.Bias, Offset = Shape(s.Bias),
                HpA = (float)(1 / (1 + 2 * Math.PI * s.HighPassHz / rate)),
                LpB = (float)Math.Exp(-2 * Math.PI * s.LowPassHz / rate),
            };
        }
        _env = 0;
    }

    public void Reset()
    {
        _env = 0;
        for (var i = 0; i < _st.Length; i++) { ref var s = ref _st[i]; s.HpX = s.HpY = s.LpY = 0; }
    }

    // tanh rational fit x(27+x²)/(27+9x²), saturating at ±3.
    private static float Tanh(float x) => x > 3 ? 1 : x < -3 ? -1 : x * (27 + x * x) / (27 + 9 * x * x);

    private static float Shape(float x) => x >= 0 ? Tanh(x) : NegHeadroom * Tanh(x / NegHeadroom);

    public void Process(Span<float> block)
    {
        var st = _st.AsSpan(0, _n);
        for (var n = 0; n < block.Length; n++)
        {
            var v = block[n] * _drive * (1 - _sag * _env);
            for (var i = 0; i < st.Length; i++)
            {
                ref var s = ref st[i];
                s.HpY = s.HpA * (s.HpY + v - s.HpX);
                s.HpX = v;
                v = Shape(s.Gain * s.HpY + s.Bias) - s.Offset;
                s.LpY = v + s.LpB * (s.LpY - v);
                v = s.LpY;
            }
            var level = MathF.Abs(v);
            _env += (level > _env ? _att : _rel) * (level - _env);
            block[n] = v;
        }
    }
}
