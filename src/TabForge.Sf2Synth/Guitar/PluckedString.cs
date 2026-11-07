namespace TabForge.Sf2Synth.Guitar;

// Owns: one string as a digital waveguide (Karplus-Strong family): integer delay + first-order Thiran all-pass for exact tuning,
//   two dispersion all-passes (stiffness), loop low-pass + gain for a length-correct decay, the pluck (triangle at the pick
//   position + pick-combed velocity-filtered noise), pickup comb, re-pluck, release, retune.
// Does not own: which note or when (GuitarLayer), values (Voicings), anything after the pickup (GuitarBus).
// Tests: GuitarLayerTests.
internal sealed class PluckedString
{
    public const double LowestHz = 25;
    private const float Silent = 1e-4f, RepluckKeep = 0.3f;
    private const double ReleaseDarken = 0.3;

    private readonly float[] _buf;
    private readonly int _mask;
    private readonly double _rate;
    private int _w, _d, _pickup;
    private float _eta, _apX, _apY, _c, _d1X, _d1Y, _d2X, _d2Y, _lpA, _lp, _g;
    private StringSpec _spec = null!;
    private double _hz, _dark, _t60;

    public bool Active { get; private set; }
    public bool Released { get; private set; }
    public bool Held { get; set; }   // note-off under the sustain pedal
    public int Channel { get; private set; }
    public int Note { get; private set; }
    public long Started { get; private set; }

    public PluckedString(double rate)
    {
        _rate = rate;
        var size = 1;
        while (size < rate / LowestHz + 8) size <<= 1;
        _buf = new float[size];
        _mask = size - 1;
    }

    public void Pluck(int channel, int note, double hz, int velocity, StringSpec spec, long started, uint seed, float[] scratch)
    {
        var ringing = Active && Channel == channel && Note == note;
        if (!ringing) { Array.Clear(_buf); _apX = _apY = _d1X = _d1Y = _d2X = _d2Y = _lp = 0; }
        (Active, Released, Held, Channel, Note, Started, _spec) = (true, false, false, channel, note, started, spec);
        var v = Math.Clamp(velocity / 127.0, 0, 1);
        _dark = Math.Clamp(spec.Darkness * (1.25 - 0.5 * v), 0, 0.95);
        _c = (float)spec.Stiffness;
        _t60 = spec.T60Ref * Math.Pow(Math.Max(hz, 1) / spec.RefHz, Math.Log(spec.T60At1k / spec.T60Ref) / Math.Log(1000 / spec.RefHz));
        Retune(hz);
        Excite(v, seed, scratch, ringing);
    }

    private void Excite(double v, uint seed, float[] scratch, bool ringing)
    {
        var n = Math.Min(_d, scratch.Length);
        if (ringing) for (var i = 0; i < _buf.Length; i++) _buf[i] *= RepluckKeep;
        var amp = (float)(0.5 * Math.Pow(v, 1.4));
        var pick = Math.Clamp((int)Math.Round(_spec.PickPos * n), 1, n - 1);
        var c = (float)(1 - Math.Exp(-2 * Math.PI * _spec.PluckHz * (0.25 + 0.75 * Math.Pow(v, 1.2)) / _rate));
        var mix = (float)_spec.NoiseMix;
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
            float e = _spec.Harmonic
                ? MathF.Sin(2 * MathF.PI * i / n) + 0.15f * MathF.Sin(4 * MathF.PI * i / n)
                : (1 - mix) * (i < pick ? (float)i / pick : (float)(n - i) / (n - pick)) + mix * 0.5f * (scratch[i] - scratch[(i + pick) % n]);
            scratch[i] = e;
            mean += e;
        }
        mean /= n;
        for (var i = 0; i < n; i++) _buf[(_w - n + i) & _mask] += amp * (scratch[i] - mean);
    }

    // Loop delay = D + Thiran fraction + 2 dispersion all-passes + loop low-pass, all as phase delays at f0.
    public void Retune(double hz)
    {
        _hz = hz;
        var w0 = 2 * Math.PI * hz / _rate;
        var a = _dark;
        var lpDelay = Math.Atan2(a * Math.Sin(w0), 1 - a * Math.Cos(w0)) / w0;
        var lpGain = (1 - a) / Math.Sqrt(1 - 2 * a * Math.Cos(w0) + a * a);
        var period = _rate / hz;
        var rest = period - lpDelay - 2 * AllpassDelay(_c, w0);
        var d = Math.Clamp((int)Math.Floor(rest - 0.5), 2, _buf.Length - 4);
        double frac = rest - d, target = frac, eta = 0;
        for (var i = 0; i < 3; i++) { eta = (1 - target) / (1 + target); target += frac - AllpassDelay(eta, w0); }
        (_d, _eta, _lpA) = (d, (float)eta, (float)a);
        _g = (float)Math.Min(0.99999, Math.Pow(10, -3 / (_t60 * hz)) / lpGain);
        _pickup = _spec.PickupPos > 0 ? Math.Clamp((int)Math.Round(_spec.PickupPos * period), 1, d - 1) : 0;
    }

    // Phase delay of (c + z⁻¹)/(1 + c·z⁻¹) at w.
    private static double AllpassDelay(double c, double w) =>
        -(Math.Atan2(-Math.Sin(w), c + Math.Cos(w)) - Math.Atan2(-c * Math.Sin(w), 1 + c * Math.Cos(w))) / w;

    public void Release()
    {
        if (!Active || Released) return;
        (Released, Held) = (true, false);
        _t60 = _spec.ReleaseT60;
        _dark = Math.Min(0.95, _dark + ReleaseDarken);
        Retune(_hz);
    }

    public void Stop() { Active = false; Released = true; Held = false; }

    public void Render(Span<float> output)
    {
        var buf = _buf;
        int w = _w, d = _d, m = _mask, pickup = _pickup;
        float eta = _eta, apX = _apX, apY = _apY, c = _c, d1X = _d1X, d1Y = _d1Y, d2X = _d2X, d2Y = _d2Y, a = _lpA, lp = _lp, g = _g, peak = 0;
        for (var i = 0; i < output.Length; i++)
        {
            var x = buf[(w - d) & m];
            var t = eta * x + apX - eta * apY; apX = x; apY = t;
            var s1 = c * t + d1X - c * d1Y; d1X = t; d1Y = s1;
            var s2 = c * s1 + d2X - c * d2Y; d2X = s1; d2Y = s2;
            lp = s2 + a * (lp - s2);
            buf[w] = g * lp;
            output[i] += pickup > 0 ? x - buf[(w - d + pickup) & m] : x;
            peak = MathF.Max(peak, MathF.Abs(x));
            w = (w + 1) & m;
        }
        (_w, _apX, _apY, _d1X, _d1Y, _d2X, _d2Y, _lp) = (w, apX, apY, d1X, d1Y, d2X, d2Y, lp);
        if (peak < (Released ? Silent : Silent * 0.1f)) Stop();
    }
}
