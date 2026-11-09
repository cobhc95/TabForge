using TabForge.Sf2Synth.Dsp;
using TabForge.Sf2Synth.Humanize;
using TabForge.Sf2Synth.Sf2;

namespace TabForge.Sf2Synth;

// Owns: rendering one voice: 4-point cubic Hermite sample interpolation with loop modes 0/1/3, resonant low-pass (RBJ, coefficients
//   swept linearly across the chunk), pitch from key/tuning/LFOs/mod env/pitch wheel, gain ramped across each chunk (click-free),
//   constant-power pan, reverb/chorus sends, the note's Nuance (pitch offset over its age, brightness). No allocation.
// Does not own: setup from zones (Voice.Setup), allocation and stealing (Synthesizer).
// Tests: Sf2SynthTests.
internal sealed partial class Voice(double rate)
{
    private const float Scale = 1f / 32768;
    private const double KillSeconds = 0.005;
    private readonly double _rate = rate;
    private short[] _data = [];
    private int _start, _end, _loopStart, _loopEnd, _loopMode, _rootKey, _correction, _modKey, _modVel;
    private double _rateRatio, _pos;
    private Channel _ch = null!;
    private Envelope _volEnv, _modEnv;
    private Lfo _modLfo, _vibLfo;
    private BiquadCoefs _filter;
    private float _z1, _z2, _gl, _gr;
    private bool _filterOn, _first;
    private Nuance _nu;
    private double _age;

    public int Channel, Key, Velocity, ExclusiveClass;
    public long Started;
    public bool Active, Released, Held;
    public float Level => _gl + _gr;

    public void Release()
    {
        (Released, Held) = (true, false);
        _volEnv.Release();
        _modEnv.Release();
    }

    public void Kill() { Release(); _volEnv.Release(KillSeconds); }

    public void Stop() { Active = false; _gl = _gr = 0; }

    public void Render(Span<float> left, Span<float> right, Span<float> reverb, Span<float> chorus)
    {
        var n = left.Length;
        if (_ch.Version != _version) Evaluate();
        var dt = n / _rate;
        var modEnv = _modEnv.Value;
        var modLfo = _modLfo.Step(dt);
        var vibLfo = _vibLfo.Step(dt);
        _volEnv.Step(dt);
        _modEnv.Step(dt);

        var cents = (_modKey - _rootKey) * P(G.ScaleTuning) + P(G.CoarseTune) * 100 + P(G.FineTune) + _correction + P(G.Pitch) + _ch.TuneCents
                    + vibLfo * P(G.VibLfoToPitch) + modLfo * P(G.ModLfoToPitch) + modEnv * P(G.ModEnvToPitch);
        if (_nu.Moves) { cents += (float)_nu.Offset(_age, Released); _age += dt; }
        var step = _rateRatio * Math.Pow(2, cents / 1200.0);

        var fc = Math.Clamp(P(G.FilterFc) + modEnv * P(G.ModEnvToFc) + modLfo * P(G.ModLfoToFc), 1500, 13500);
        if (_nu.Bright != 0 && fc < 13500) fc = Math.Clamp(fc - _nu.Bright * (float)HumanTable.FcCentsPerBright, 1500, 13500);
        var q = Math.Clamp(P(G.FilterQ), 0, 960);
        _filterOn |= fc < 13500 || q > 0;
        BiquadCoefs target = default, d = default;
        if (_filterOn)
        {
            // Q in cB above DC; -3.01 dB so q = 0 is a flat Butterworth corner.
            target = BiquadCoefs.Of(new EqSpec(EqKind.LowPass, 8.176 * Math.Pow(2, fc / 1200.0), Math.Pow(10, (q / 10 - 3.01) / 20)), _rate);
            if (_first) _filter = target;
            d = new BiquadCoefs { B0 = (target.B0 - _filter.B0) / n, B1 = (target.B1 - _filter.B1) / n, B2 = (target.B2 - _filter.B2) / n, A1 = (target.A1 - _filter.A1) / n, A2 = (target.A2 - _filter.A2) / n };
        }
        _first = false;

        var atten = Math.Max(0, P(G.Attenuation) - modLfo * P(G.ModLfoToVolume));
        var gain = _volEnv.Done ? 0 : _volEnv.Value * MathF.Pow(10, -atten / 200);
        var theta = (Math.Clamp(P(G.Pan), -500, 500) / 1000f + 0.5f) * MathF.PI / 2;
        float tl = gain * MathF.Cos(theta), tr = gain * MathF.Sin(theta), dl = (tl - _gl) / n, dr = (tr - _gr) / n;
        float rev = Math.Clamp(P(G.Reverb), 0, 1000) / 1000, cho = Math.Clamp(P(G.Chorus), 0, 1000) / 1000;

        var data = _data;
        var looping = _loopMode == 1 || (_loopMode == 3 && !Released);
        int loopLen = _loopEnd - _loopStart;
        var ended = false;
        float gl = _gl, gr = _gr, z1 = _z1, z2 = _z2;
        var c = _filter;
        for (var i = 0; i < n; i++)
        {
            var k = (int)_pos;
            var f = (float)(_pos - k);
            int k0 = k - 1, k2 = k + 1, k3 = k + 2;
            if (looping && k3 >= _loopEnd) { if (k2 >= _loopEnd) k2 -= loopLen; k3 -= loopLen; if (k >= _loopEnd) k -= loopLen; }
            float x0 = data[k0] * Scale, x1 = data[k] * Scale, x2 = data[k2] * Scale, x3 = data[k3] * Scale;
            // Catmull-Rom / cubic Hermite.
            var s = ((((0.5f * (x3 - x0) + 1.5f * (x1 - x2)) * f + (x0 - 2.5f * x1 + 2 * x2 - 0.5f * x3)) * f + 0.5f * (x2 - x0)) * f) + x1;
            if (_filterOn)
            {
                var y = c.B0 * s + z1;
                z1 = c.B1 * s - c.A1 * y + z2;
                z2 = c.B2 * s - c.A2 * y;
                s = y;
                c.B0 += d.B0; c.B1 += d.B1; c.B2 += d.B2; c.A1 += d.A1; c.A2 += d.A2;
            }
            left[i] += s * gl;
            right[i] += s * gr;
            var mono = s * (gl + gr) * 0.5f;
            reverb[i] += mono * rev;
            chorus[i] += mono * cho;
            gl += dl;
            gr += dr;
            _pos += step;
            if (looping) { if (_pos >= _loopEnd) _pos -= loopLen; }
            else if (_pos >= _end) { ended = true; break; }
        }
        (_gl, _gr, _z1, _z2) = (tl, tr, z1, z2);
        if (_filterOn) _filter = target;
        if (ended || _volEnv.Done) Stop();
    }
}
