namespace TabForge.Sf2Synth;

// Owns: the SF2 DAHDSR envelope, stepped once per render chunk (the voice ramps gain across the chunk, so steps never click).
//   Volume mode: linear-amplitude attack, then decay/release as straight lines in dB (96 dB per full decay/release time, §8.1.3).
//   Modulation mode: linear 0..1 throughout.
// Does not own: timecent conversion from generators (Voice.Setup) or the LFOs (Lfo).
// Tests: Sf2SynthTests.
internal struct Envelope
{
    private enum Stage { Delay, Attack, Hold, Decay, Sustain, Release, Done }
    private const double FloorDb = -100;

    private Stage _s;
    private double _t, _delay, _attack, _hold, _decay, _sustain, _release, _db, _lin;
    private bool _vol;

    public readonly bool Done => _s == Stage.Done;
    public readonly float Value => _vol ? (_s == Stage.Attack || _s <= Stage.Delay ? (float)_lin : (float)Math.Pow(10, _db / 20)) : (float)_lin;

    // sustain: volume mode = attenuation in dB (>= 0); modulation mode = level 0..1.
    public void Start(bool vol, double delay, double attack, double hold, double decay, double sustain, double release)
    {
        (_vol, _delay, _attack, _hold, _decay, _sustain, _release) = (vol, delay, attack, hold, decay, sustain, release);
        (_s, _t, _db, _lin) = (Stage.Delay, 0, 0, 0);
    }

    public void Release(double? seconds = null)
    {
        if (_s >= Stage.Release) { if (seconds is { } fast && _s == Stage.Release) _release = Math.Min(_release, fast); return; }
        if (_vol && _s <= Stage.Attack) _db = _lin > 0 ? 20 * Math.Log10(_lin) : FloorDb;
        if (seconds is { } s) _release = s;
        (_s, _t) = (Stage.Release, 0);
    }

    public void Step(double dt)
    {
        while (dt > 0 && _s != Stage.Done && _s != Stage.Sustain)
        {
            var length = _s switch { Stage.Delay => _delay, Stage.Attack => _attack, Stage.Hold => _hold, _ => double.MaxValue };
            var used = Math.Min(dt, Math.Max(0, length - _t));
            _t += used;
            dt -= used;
            switch (_s)
            {
                case Stage.Attack: _lin = _attack > 0 ? _t / _attack : 1; break;
                case Stage.Decay: DecayBy(used); break;
                case Stage.Release: ReleaseBy(used); break;
            }
            if (_s <= Stage.Hold && _t >= length) Next(_s + 1);
        }
    }

    private void Next(Stage s)
    {
        (_s, _t) = (s, 0);
        if (s == Stage.Decay) { _lin = 1; _db = 0; }
    }

    private void DecayBy(double dt)
    {
        if (_vol)
        {
            _db -= 96 * dt / Math.Max(_decay, 1e-3);
            if (_db <= -_sustain) { _db = -_sustain; _s = _sustain >= -FloorDb ? Stage.Done : Stage.Sustain; }
        }
        else
        {
            _lin -= dt / Math.Max(_decay, 1e-3);
            if (_lin <= _sustain) { _lin = _sustain; _s = Stage.Sustain; }
        }
    }

    private void ReleaseBy(double dt)
    {
        if (_vol) { _db -= 96 * dt / Math.Max(_release, 1e-3); if (_db <= FloorDb) _s = Stage.Done; }
        else { _lin -= dt / Math.Max(_release, 1e-3); if (_lin <= 0) { _lin = 0; _s = Stage.Done; } }
    }
}

// Triangle LFO with delay (§8.1.2 gens 21-24), -1..1, stepped per chunk.
internal struct Lfo
{
    private double _delay, _hz, _phase;

    public void Start(double delay, double hz) => (_delay, _hz, _phase) = (delay, hz, 0);

    public float Step(double dt)
    {
        if (_delay > 0) { _delay -= dt; return 0; }
        _phase = (_phase + _hz * dt) % 1;
        return (float)(_phase < 0.25 ? 4 * _phase : _phase < 0.75 ? 2 - 4 * _phase : 4 * _phase - 4);
    }
}
