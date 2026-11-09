using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: one channel's player model for a group of note-ons at one instant (one note or a chord): the pulse from the onsets
//   (autocorrelation-style fit, preferring ~0.5 s), the phrase anchor, accents on strong pulses, a rising and falling arch,
//   down/up strokes on fast notes (down louder), speed damping, steadier palm-mute chugs, a slow level drift, the strum
//   (low to high on a downstroke, high to low up; string tilt) and laziness. Amounts scale with the evidence; delays only.
// Does not own: the evidence (Evidence), queueing (Humanizer), the values (HumanTable).
// Tests: HumanizerTests.
internal sealed class Phrase
{
    public readonly Evidence Evidence = new();
    private long _anchor = -1, _lastOn = -1;
    private double _drift, _shortness;
    private int _lastKey = -1;
    private uint _rng;

    public void Reset(int channel)
    {
        (_anchor, _lastOn, _drift, _shortness, _lastKey, _rng) = (-1, -1, 0, 0, -1, 0x2545F491u * (uint)(channel + 1));
        Evidence.Reset();
    }

    private double U() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / (double)0x800000 - 1; }

    /// <summary>A note of this channel ended after <paramref name="frames"/>: short notes against the spacing read as palm-muted chugs.</summary>
    public void Ended(long frames)
    {
        var ioi = _lastOn >= 0 && Evidence.Onsets > 1 ? Evidence.Onset(0) - Evidence.Onset(1) : 0;
        if (ioi > 0) _shortness = frames < ShortRatio * ioi ? 1 : 0;
    }

    /// <summary>Plans a group: keys sorted low to high; writes each note's velocity and delay (frames).</summary>
    public void Plan(Profile p, long now, double rate, ReadOnlySpan<int> keys, Span<int> vel, Span<int> delay, bool dyn, bool time, bool drift)
    {
        var n = keys.Length;
        var ioi = _lastOn >= 0 ? (now - _lastOn) / rate : 10;
        var pulse = Pulse(rate);
        if (_anchor < 0 || ioi > Math.Max(PhraseGap, 2 * pulse / rate)) _anchor = now;
        var fast = 1 - Smooth(FastIoi, SlowIoi, ioi);
        var b = (now - _anchor) / pulse;
        var idx = (long)Math.Round(b);
        var on = Math.Abs(b - idx) < OnPulse;
        var step = Math.Min(ioi * rate, pulse);
        var down = ioi >= DownOnly || Math.Round((now - _anchor) / step) % 2 == 0;
        var chug = keys[0] == _lastKey && _shortness > 0 ? p.Chug : 1;
        _drift = Math.Clamp(_drift * 0.97 + 0.6 * U(), -p.Drift, p.Drift);

        var accent = on ? idx % 4 == 0 ? p.Accent + p.Strong : idx % 2 == 0 ? p.Accent : 0 : p.Off;
        var arch = p.Arch * (Math.Sin(Math.PI * (b / ArchPulses % 1)) - 0.6) * chug;
        var even = 1 - 0.5 * fast;
        var group = (accent + arch) * even + fast * p.Stroke * (down ? 1 : -1) + U() * (p.JitterSlow + (p.JitterFast - p.JitterSlow) * fast) * chug
                    + p.FastBias * fast + (drift ? _drift : 0);
        var vf = dyn ? Evidence.VelFlat : 0;
        var tf = time ? Evidence.TimeFlat : 0;
        var loud = vel[n - 1] / 127.0;
        var strum = n < 2 ? 0 : Math.Clamp(p.StrumMaxMs - (p.StrumMaxMs - p.StrumMinMs) * (0.6 * loud + 0.4 * fast), p.StrumMinMs, p.StrumMaxMs);
        var lazy = (0.5 + 0.5 * U()) * p.LazyMs * (1 - 0.7 * fast);
        for (var i = 0; i < n; i++)
        {
            var r = n < 2 ? 0.5 : (double)i / (n - 1);
            var tilt = p.Tilt * 2 * (down ? 0.5 - r : r - 0.5);
            var d = (int)Math.Round(vf * Math.Clamp(group + tilt, -MaxDelta, MaxDelta));
            if (vel[i] > 0) vel[i] = Math.Clamp(vel[i] + d, 1, 127);
            var order = down ? r : 1 - r;
            delay[i] = (int)Math.Round(tf * (lazy + strum * order) * rate / 1000);
        }
        (_lastOn, _lastKey) = (now, keys[0]);
    }

    // The period that fits most onsets on its grid (anchored at the newest), weighted towards ~0.5 s. Candidates: the intervals
    // and multiples of the shortest. Falls back to the last interval, else 0.5 s.
    private double Pulse(double rate)
    {
        var e = Evidence;
        int n = e.Onsets;
        if (n < 3) return PulsePref * rate;
        double best = PulsePref * rate, score = -1, shortest = double.MaxValue;
        for (var i = 0; i + 1 < n; i++) shortest = Math.Min(shortest, e.Onset(i) - e.Onset(i + 1));
        for (var c = 0; c + 1 < n + 4; c++)
        {
            var d = c + 1 < n ? e.Onset(c) - e.Onset(c + 1) : shortest * (c + 3 - n);
            if (d < PulseMin * rate || d > PulseMax * rate) continue;
            var fit = 0;
            for (var i = 1; i < n; i++)
            {
                var x = (e.Onset(0) - e.Onset(i)) / d;
                if (Math.Abs(x - Math.Round(x)) < 0.06) fit++;
            }
            var pref = Math.Log2(d / rate / PulsePref);
            var s = fit / (n - 1.0) * Math.Exp(-0.5 * pref * pref);
            if (s > score + 1e-9) (best, score) = (d, s);
        }
        return best;
    }
}
