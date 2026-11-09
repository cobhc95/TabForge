using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

/// <summary>One planned stroke (a note or a chord): level change dB, onset offset ms (+ = late), direction, interval since the last.</summary>
internal readonly record struct Stroke(double Db, double Ms, bool Down, double IoiMs);

// Owns: one channel's player model per stroke: the pulse and subdivision from the onsets, the bar grid (16 slots of 4 pulses,
//   anchored at the phrase start), and the sum of named parts: level = metric accent + bar pattern (kept bar to bar) + arch +
//   1/f + white + down/up + speed, through a soft ceiling, ghosts on 16th offbeats of strums; palm-mute chugs keep only accent,
//   pattern and a little white. Timing = bar pattern (anticorrelated with the slot before) + 1/f + jitter (anticorrelated with
//   the note before last) + tempo drift + bias, within the look-ahead.
// Does not own: the evidence (Evidence), the strings of a strum (Strum), queueing (Humanizer), the values (HumanTable).
// Tests: HumanizerTests.
internal sealed class Phrase
{
    public readonly Evidence Evidence = new();
    public readonly Rng Rng = new();
    private readonly double[] _patDb = new double[Slots], _patMs = new double[Slots];
    private readonly Pink _pinkDb = new(PinkDbSlope), _pinkMs = new(PinkMsSlope);
    private long _anchor = -1, _lastOn = -1, _bar = -1;
    private double _tempo, _drift, _w1, _w2, _shortness;
    private int _lastKey = -1, _sub = 2;

    /// <summary>Ghost strokes since Reset.</summary>
    public int Ghosts { get; private set; }

    public void Reset(int channel)
    {
        (_anchor, _lastOn, _bar, _tempo, _drift, _w1, _w2, _shortness, _lastKey, _sub, Ghosts) = (-1, -1, -1, 0, 0, 0, 0, 0, -1, 2, 0);
        Rng.Seed(0x2545F491u * (uint)(channel + 1));
        _pinkDb.Reset(Rng);
        _pinkMs.Reset(Rng);
        Evidence.Reset();
    }

    /// <summary>A note of this channel ended after <paramref name="frames"/>: short notes against the spacing read as palm-muted chugs.</summary>
    public void Ended(long frames)
    {
        var ioi = _lastOn >= 0 && Evidence.Onsets > 1 ? Evidence.Onset(0) - Evidence.Onset(1) : 0;
        if (ioi > 0) _shortness = frames < ShortRatio * ioi ? 1 : 0;
    }

    /// <summary>Plans a stroke of <paramref name="n"/> notes whose lowest key is <paramref name="low"/>.</summary>
    public Stroke Plan(Family f, Style s, long now, double rate, int n, int low, bool drift, double lookMs)
    {
        var r = Rng;
        var ioi = _lastOn >= 0 ? (now - _lastOn) / rate : 10;
        var pulse = Pulse(rate);
        var first = _anchor < 0 || ioi > Math.Max(PhraseGap, 2 * pulse / rate);
        if (first) (_anchor, _bar) = (now, -1);
        var slot = (long)Math.Round((now - _anchor) / (pulse * 4 / Slots));
        int pos = (int)(slot % Slots);
        var bar = slot / Slots;
        if (bar != _bar) NewBar(_bar < 0);
        _bar = bar;
        var down = _sub >= 4 || slot / _sub % 2 == 0;
        var chug = low == _lastKey && _shortness > 0;

        var accent = pos == 0 ? DownDb : pos % 4 == 0 ? BeatDb : pos % 2 == 0 ? EighthDb : SixteenthDb;
        var x = (bar % ArchBars + pos / (double)Slots) / ArchBars;
        var arch = ArchDb * (x < 0.75 ? x / 0.375 - 1 : 1 - (x - 0.75) / 0.125);
        var speed = ioi < SlowIoi ? FastDb * Math.Min(1, (SlowIoi - ioi) / (SlowIoi - FastIoi)) : ioi > GapIoi ? GapDb : 0;
        var hand = f.AltDb > 0 ? down ? f.AltDb : -f.AltDb : down ? 0 : s.UpDb;
        var db = accent + PatDb * _patDb[pos] + (chug ? ChugDb * r.G() : arch + PinkDb * _pinkDb.Next(r) + WhiteDb * r.G() + hand + speed);
        db = db * s.KDb * f.LevelK + s.BiasMs * BiasDbPerMs;
        if (db > KneeDb) db = KneeDb + (db - KneeDb) / Ratio;
        db = Math.Clamp(db, -CapDb, CapDb);
        if (n >= 3 && f.Strum > 0 && pos % 2 == 1 && !first && r.U01() < s.Ghost) (db, Ghosts) = (db + GhostDb, Ghosts + 1);

        var w = JitMs * r.G();
        var jit = (w - AntiJit * _w2) * (ioi < FastJitIoi ? FastJit : 1);
        (_w2, _w1) = (_w1, w);
        if (drift)
        {
            var dt = Math.Min(ioi, 1);
            _tempo = Math.Clamp(_tempo * Math.Exp(-dt / TempoSec) + TempoStep * Math.Sqrt(dt) * r.G(), -TempoMax, TempoMax);
            _drift = Math.Clamp(_drift * Math.Exp(-dt / DriftSec) + _tempo * dt * 1000, -DriftMs, DriftMs);
        }
        else _drift = 0;
        var pat = (_patMs[pos] - AntiPat * _patMs[(pos + Slots - 1) % Slots]) / Math.Sqrt(1 + AntiPat * AntiPat);
        var ms = (PatMs * pat + PinkMs * _pinkMs.Next(r) + jit + _drift) * (chug ? ChugJit : 1) * s.KMs * f.TimeK + s.BiasMs + f.BiasMs;
        (_lastOn, _lastKey) = (now, low);
        return new(db, Math.Clamp(ms, -lookMs, lookMs), down, ioi * 1000);
    }

    // The bar pattern: drawn fresh at a phrase start, else kept with correlation rho and a fresh share.
    private void NewBar(bool fresh)
    {
        for (var i = 0; i < Slots; i++)
        {
            _patDb[i] = fresh ? Rng.G() : PatDbRho * _patDb[i] + Math.Sqrt(1 - PatDbRho * PatDbRho) * Rng.G();
            _patMs[i] = fresh ? Rng.G() : PatMsRho * _patMs[i] + Math.Sqrt(1 - PatMsRho * PatMsRho) * Rng.G();
        }
    }

    // The pulse: the unit (shortest interval heard at least twice, else the shortest) times 1, 2, 4 or 8, nearest 0.5 s.
    // Also sets the subdivision (the unit in quarter pulses, 1..4).
    private double Pulse(double rate)
    {
        var e = Evidence;
        int n = e.Onsets;
        if (n < 3) { _sub = 2; return PulsePref * rate; }
        double unit = double.MaxValue, once = double.MaxValue;
        for (var i = 0; i + 1 < n; i++)
        {
            double d = e.Onset(i) - e.Onset(i + 1), seen = 0;
            for (var j = 0; j + 1 < n; j++) if (j != i && Math.Abs(e.Onset(j) - e.Onset(j + 1) - d) < UnitTol * d) seen++;
            if (seen > 0) unit = Math.Min(unit, d);
            once = Math.Min(once, d);
        }
        if (unit == double.MaxValue) unit = once;
        var best = unit;
        for (var m = 2; m <= 8; m *= 2) if (Math.Abs(Math.Log2(unit * m / rate / PulsePref)) < Math.Abs(Math.Log2(best / rate / PulsePref))) best = unit * m;
        best = Math.Clamp(best, PulseMin * rate, PulseMax * rate);
        _sub = (int)Math.Clamp(Math.Round(unit / (best * 4 / Slots)), 1, 4);
        return best;
    }
}
