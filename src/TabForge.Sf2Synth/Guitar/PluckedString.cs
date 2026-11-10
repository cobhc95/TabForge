using TabForge.Sf2Synth.Humanize;

namespace TabForge.Sf2Synth.Guitar;

// Owns: one string as a digital waveguide: integer delay + Thiran all-pass (exact tuning), two dispersion all-passes from the
//   fretted string's stiffness (StringPhysics), loop low-pass + gain for a length-correct decay (highs die first) with a fast first
//   stage (double decay: the loop loses Drop dB more over DropMs after a pluck that starts that much louder); the pluck
//   (PluckedString.Attack), the pickup comb at its distance from the bridge (two taps for a humbucker or two pickups), legato
//   (hammer, pull, slide glide) on the same string, re-pluck, release damping, retune, the note's Nuance (seed, pick position,
//   brightness, release, pitch offset over its age), and on the distorted programs a per-note slow pitch wobble.
// Does not own: which note or when (GuitarLayer), values (Voicings, StringPhysics), anything after the pickup (GuitarBus).
// Tests: GuitarLayerTests.
internal sealed partial class PluckedString
{
    public const double LowestHz = 25;
    private const float Silent = 1e-4f, RepluckKeep = 0.3f;
    private const double ShortNote = 0.12;

    private readonly float[] _buf;
    private readonly int _mask;
    private readonly double _rate;
    private int _w, _d, _pickup, _pickup2, _glideLeft;
    private float _eta, _apX, _apY, _c, _d1X, _d1Y, _d2X, _d2Y, _lpA, _lp, _g;
    private StringSpec _spec = null!;
    private StringPhysics.Fretted _f;
    private double _hz, _dark, _t60, _glideSemis, _cents, _dropRate;
    private float _wobbleCents, _wobbleHz, _wobblePhase, _wobbleDark, _pickupShift;
    private Nuance _nu;
    private long _age;

    public bool Active { get; private set; }
    public bool Released { get; private set; }
    public bool Held { get; set; }   // note-off under the sustain pedal
    public int Channel { get; private set; }
    public int Note { get; private set; }
    public long Started { get; private set; }
    public int String => _f.String;
    public double Age => _age / _rate;

    public PluckedString(double rate)
    {
        _rate = rate;
        var size = 1;
        while (size < rate / LowestHz + 8) size <<= 1;
        _buf = new float[size];
        _mask = size - 1;
    }

    public void Pluck(int channel, int note, double hz, int velocity, StringSpec spec, long started, uint seed, float[] scratch, in Nuance nu = default)
    {
        var ringing = Active && Channel == channel && Note == note;
        if (!ringing) { Array.Clear(_buf); _apX = _apY = _d1X = _d1Y = _d2X = _d2Y = _lp = 0; }
        else for (var i = 0; i < _buf.Length; i++) _buf[i] *= RepluckKeep;
        (Active, Released, Held, Channel, Started, _spec, _age, _glideLeft, _glideSemis, _nu, _cents, _wobbleCents, _wobbleHz, _wobblePhase, _wobbleDark, _pickupShift) = (true, false, false, channel, started, spec, 0, 0, 0, nu, 0, 0, 0, 0, 0, 0);
        var v = Math.Clamp(velocity / 127.0, 0, 1);
        if (spec.Wobble)
        {
            uint h = (uint)(channel * 131 + note + 1) * 0x9E3779B9u ^ (uint)started * 0x85EBCA6Bu ^ (uint)velocity * 0xC2B2AE35u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
            float U() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xFFFFFF) / (float)0x800000 - 1f; }
            var u1 = U(); var u2 = U(); var u3 = U();
            var vf = (float)v;
            _wobbleCents = 3.0f * (0.65f + 0.5f * vf) * (0.55f + 0.45f * MathF.Abs(u1));
            _wobbleHz = 2.5f + 2.5f * (0.5f + 0.5f * u2);
            _wobblePhase = u3 * (float)Math.PI;
            _wobbleDark = 0.03f * u1 * (0.5f + vf);
            _pickupShift = 6f * u2 * (0.4f + vf);
            _dark = Math.Clamp(spec.Darkness * (1.35 - 0.7 * v) + nu.Bright + _wobbleDark, 0, 0.95);
        }
        else _dark = Math.Clamp(spec.Darkness * (1.25 - 0.5 * v) + nu.Bright, 0, 0.95);
        Fret(note, hz, -1);
        var drop = spec.Drop * Math.Pow(v, spec.DropVel);
        _dropRate = drop / (spec.DropMs * 0.001);
        Excite(v, (float)(0.5 * Math.Pow(v, spec.Vel) * Math.Pow(10, drop / 20)), nu.Seed != 0 ? nu.Seed : seed, scratch);
    }

    // Same string, new note: hammer/pull (kick > 0 re-excites a little) or slide (glide seconds).
    public void Legato(int note, double hz, double glide, float kick, uint seed, float[] scratch)
    {
        _glideSemis = glide > 0 ? note - Note + _glideSemis : 0;
        _glideLeft = (int)(glide * _rate);
        (Released, Held) = (false, false);
        Fret(note, hz, _f.String);
        if (kick > 0) Excite(0.5, kick, seed, scratch);
    }

    private void Fret(int note, double hz, int str)
    {
        Note = note;
        _f = StringPhysics.Of(note, _spec.Bass, str);
        _c = (float)_f.Dispersion;
        _t60 = _spec.T60Ref * Math.Pow(Math.Max(hz, 1) / _spec.RefHz, Math.Log(_spec.T60At1k / _spec.T60Ref) / Math.Log(1000 / _spec.RefHz));
        Retune(hz);
    }

    // Loop delay = D + Thiran fraction + 2 dispersion all-passes + loop low-pass, all as phase delays at f0.
    public void Retune(double hz) { _hz = hz; Tune(hz * Math.Pow(2, (_glideSemis + _cents / 100) / 12)); }

    private void Tune(double hz)
    {
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
        (_pickup, _pickup2) = (Coil(_spec.PickupMm + _pickupShift, period, d), Coil(_spec.Pickup2Mm + _pickupShift, period, d));
    }

    private int Coil(double mm, double period, int d) => mm > 0 ? Math.Clamp((int)Math.Round(Math.Min(0.5, mm / 1000 / _f.Length) * period), 1, d - 1) : 0;

    // Phase delay of (c + z⁻¹)/(1 + c·z⁻¹) at w.
    private static double AllpassDelay(double c, double w) =>
        -(Math.Atan2(-Math.Sin(w), c + Math.Cos(w)) - Math.Atan2(-c * Math.Sin(w), 1 + c * Math.Cos(w))) / w;

    // Finger damping: short notes (palm-mute style) damp harder.
    public void Release()
    {
        if (!Active || Released) return;
        (Released, Held) = (true, false);
        var shortNote = Age < ShortNote;
        _t60 = _spec.ReleaseT60 * (shortNote ? 0.5 : 1) * (1 + _nu.Release);
        _dark = Math.Min(0.95, _dark + _spec.ReleaseDarken * (shortNote ? 1.5 : 1));
        Retune(_hz);
    }

    public void Stop() { Active = false; Released = true; Held = false; }

    public void Render(Span<float> output)
    {
        if (_glideLeft > 0)
        {
            var step = Math.Min(_glideLeft, output.Length);
            _glideSemis -= _glideSemis * step / _glideLeft;
            _glideLeft -= step;
            Retune(_hz);
        }
        if (_wobbleCents != 0 || (_nu.Moves && (Age < HumanTable.DetuneUntil || _nu.VibCents != 0)))
        {
            double cents = _nu.Moves ? _nu.Offset(Age, Released) : 0;
            if (_wobbleCents != 0) cents += _wobbleCents * Math.Min(1, Age / 0.15) * Math.Sin(2 * Math.PI * _wobbleHz * Age + _wobblePhase);
            _cents = cents;
            Retune(_hz);
        }
        if (_spec.Mwah > 0) Mwah();
        var buf = _buf;
        var mf = _mw;
        int w = _w, d = _d, m = _mask, pickup = _pickup, p2 = _pickup2;
        float eta = _eta, apX = _apX, apY = _apY, c = _c, d1X = _d1X, d1Y = _d1Y, d2X = _d2X, d2Y = _d2Y, a = _lpA, lp = _lp, g = _g, mg = _mwGain, peak = 0;
        if (_dropRate > 0)   // first stage of the double decay: an extra rate falling over DropMs
        {
            var r = _dropRate * Math.Exp(-Age / (_spec.DropMs * 0.001));
            g *= (float)Math.Pow(10, -r / (20 * _hz));
            if (r < 0.05) _dropRate = 0;
        }
        for (var i = 0; i < output.Length; i++)
        {
            var x = buf[(w - d) & m];
            var t = eta * x + apX - eta * apY; apX = x; apY = t;
            var s1 = c * t + d1X - c * d1Y; d1X = t; d1Y = s1;
            var s2 = c * s1 + d2X - c * d2Y; d2X = s1; d2Y = s2;
            lp = s2 + a * (lp - s2);
            buf[w] = g * lp;
            var o = pickup == 0 ? x : p2 == 0 ? x - buf[(w - d + pickup) & m] : x - 0.5f * (buf[(w - d + pickup) & m] + buf[(w - d + p2) & m]);
            if (mg > 0) o += mg * mf.Process(o);
            output[i] += o;
            peak = MathF.Max(peak, MathF.Abs(x));
            w = (w + 1) & m;
        }
        _mw = mf;
        if (_click > 1e-6f || _tap > 1e-6f) AddTransients(output);
        (_w, _apX, _apY, _d1X, _d1Y, _d2X, _d2Y, _lp) = (w, apX, apY, d1X, d1Y, d2X, d2Y, lp);
        _age += output.Length;
        if (peak < (Released ? Silent : Silent * 0.1f)) Stop();
    }
}
