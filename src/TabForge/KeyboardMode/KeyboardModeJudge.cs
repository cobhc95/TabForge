namespace TabForge.KeyboardMode;

// Owns: judging played key events against expected notes: matching a press to the nearest unresolved note of the same key within the good window, misses once a window has passed,
//   extras (presses that match nothing), sustain (key held for the note length) and chord groups. Pure: no clock, no device, no UI; same input gives the same result.
// Does not own: where events and notes come from, latency measurement (the caller passes compensated times), totals (KeyboardModeScore) or drawing.
// Tests: TestKeyboardModeJudge.
public sealed class KeyboardModeJudge
{
    private readonly KeyboardModeTolerances _tol;
    private readonly KeyboardModeResult[] _results;
    private readonly List<int> _groupStarts = new();
    private readonly Dictionary<int, KeyboardModeResult> _held = new();   // key -> the hit note that is still held
    private readonly List<KeyboardModePlayed> _extras = new();
    private int _prefix;   // every result before this index is resolved

    public KeyboardModeJudge(IEnumerable<KeyboardModeExpected> expected, KeyboardModeTolerances? tolerances = null)
    {
        _tol = tolerances ?? KeyboardModeTolerances.Default;
        var sorted = expected.Where(e => !double.IsNaN(e.OnsetSec)).OrderBy(e => e.OnsetSec).ThenBy(e => e.Midi).ToArray();
        _results = new KeyboardModeResult[sorted.Length];
        var groupFirst = 0.0;
        var group = -1;
        for (var i = 0; i < sorted.Length; i++)
        {
            if (group < 0 || Offset(sorted[i].OnsetSec, groupFirst) > _tol.ChordMs) { group++; groupFirst = sorted[i].OnsetSec; _groupStarts.Add(i); }
            _results[i] = new KeyboardModeResult(i, sorted[i], group);
        }
    }

    /// <summary>One result per expected note, in onset order (then key).</summary>
    public IReadOnlyList<KeyboardModeResult> Results => _results;
    /// <summary>First index of each chord group; a group ends where the next starts.</summary>
    public IReadOnlyList<int> GroupStarts => _groupStarts;
    /// <summary>Presses that matched no expected note.</summary>
    public IReadOnlyList<KeyboardModePlayed> Extras => _extras;
    /// <summary>How many leading results are resolved (hit or missed); the score reads these in onset order.</summary>
    public int ResolvedPrefix => _prefix;

    /// <summary>Song time of a key event from the clock reading and the delay between the key and the song position the clock reports (output plus input latency, seconds).
    /// The player reacts to what they hear, which is late by that delay, so the press belongs earlier in the song.</summary>
    public static double Compensate(double rawSongSec, double latencySec) => rawSongSec - latencySec;

    /// <summary>One played event. A press within the good window of an unresolved note of the same key resolves the nearest one (the earlier on a tie) and is returned;
    /// a release closes the sustain of the held note; a press that matches nothing is an extra. Events with an unknown time (NaN) are ignored.</summary>
    public KeyboardModeResult? Feed(KeyboardModePlayed e)
    {
        if (double.IsNaN(e.TimeSec)) return null;
        if (!e.On) { Release(e.Midi, e.TimeSec); return null; }
        Release(e.Midi, e.TimeSec);   // a new press of a held key ends the old hold
        var best = -1;
        var bestDt = double.MaxValue;
        for (var i = FirstCandidate(e.TimeSec); i < _results.Length; i++)
        {
            var r = _results[i];
            var dt = Offset(e.TimeSec, r.Expected.OnsetSec);
            if (dt < -_tol.GoodMs) break;   // later notes are even farther ahead
            if (r.IsResolved || r.Expected.Midi != e.Midi) continue;
            if (Math.Abs(dt) < bestDt) { best = i; bestDt = Math.Abs(dt); }
        }
        if (best < 0) { _extras.Add(e); return null; }
        var hit = _results[best];
        hit.OffsetMs = Offset(e.TimeSec, hit.Expected.OnsetSec);
        hit.PlayedOnSec = e.TimeSec;
        hit.Grade = Math.Abs(hit.OffsetMs) <= _tol.PerfectMs ? KeyboardModeGrade.Perfect : KeyboardModeGrade.Good;
        _held[e.Midi] = hit;
        MovePrefix();
        return hit;
    }

    /// <summary>Offset in ms of a press from the nearest note of the same key that has not been hit, within <paramref name="rangeMs"/> either way; NaN when there is none.
    /// Missed notes count too, so a press that is far too late still tells how late it was.</summary>
    public double NearestOffsetMs(KeyboardModePlayed e, double rangeMs = 1000)
    {
        var best = double.NaN;
        for (var i = 0; i < _results.Length; i++)
        {
            var r = _results[i];
            var dt = Offset(e.TimeSec, r.Expected.OnsetSec);
            if (dt < -rangeMs) break;
            if (dt > rangeMs || r.Expected.Midi != e.Midi || r.IsHit) continue;
            if (double.IsNaN(best) || Math.Abs(dt) < Math.Abs(best)) best = dt;
        }
        return best;
    }

    /// <summary>Marks every note whose good window has fully passed at <paramref name="nowSec"/> as a miss.</summary>
    public void Advance(double nowSec)
    {
        if (double.IsNaN(nowSec)) return;
        for (var i = _prefix; i < _results.Length && Offset(nowSec, _results[i].Expected.OnsetSec) > _tol.GoodMs; i++)
            if (!_results[i].IsResolved) _results[i].Grade = KeyboardModeGrade.Miss;
        MovePrefix();
    }

    /// <summary>The song ended or the player stopped: every pending note is a miss and every held key is released.</summary>
    public void Finish(double nowSec)
    {
        foreach (var r in _results) if (!r.IsResolved) r.Grade = KeyboardModeGrade.Miss;
        foreach (var key in _held.Keys.ToArray()) Release(key, nowSec);
        MovePrefix();
    }

    /// <summary>Gives up a chord group: every note of it still pending is a miss (wait mode's skip).</summary>
    public void MissGroup(int group)
    {
        var end = group + 1 < _groupStarts.Count ? _groupStarts[group + 1] : _results.Length;
        for (var i = _groupStarts[group]; i < end; i++) if (!_results[i].IsResolved) _results[i].Grade = KeyboardModeGrade.Miss;
        MovePrefix();
    }

    /// <summary>The hits and misses of one chord group, and whether every note is resolved.</summary>
    public (int Hits, int Misses, bool Complete) GroupState(int group)
    {
        int hits = 0, misses = 0, pending = 0;
        var end = group + 1 < _groupStarts.Count ? _groupStarts[group + 1] : _results.Length;
        for (var i = _groupStarts[group]; i < end; i++)
            if (!_results[i].IsResolved) pending++; else if (_results[i].IsHit) hits++; else misses++;
        return (hits, misses, pending == 0);
    }

    /// <summary>A resolved chord with some notes hit and some missed.</summary>
    public bool IsPartial(int group) { var (h, m, c) = GroupState(group); return c && h > 0 && m > 0; }

    private void Release(int midi, double timeSec)
    {
        if (!_held.Remove(midi, out var r)) return;
        r.HeldSec = Math.Max(0, timeSec - r.PlayedOnSec);
        r.Sustained = r.Expected.DurationSec * 1000 < _tol.MinSustainMs || r.HeldSec >= r.Expected.DurationSec * _tol.SustainFraction - 1e-9;
    }

    private void MovePrefix() { while (_prefix < _results.Length && _results[_prefix].IsResolved) _prefix++; }

    /// <summary>Offset in ms rounded to a micro-ms so a press exactly on a window edge is not pushed over it by floating point.</summary>
    private static double Offset(double playedSec, double expectedSec) => Math.Round((playedSec - expectedSec) * 1000, 6);

    /// <summary>The first result whose onset is not more than the good window before the time (binary search on onset).</summary>
    private int FirstCandidate(double timeSec)
    {
        int lo = _prefix, hi = _results.Length;
        while (lo < hi) { var mid = (lo + hi) / 2; if (Offset(timeSec, _results[mid].Expected.OnsetSec) > _tol.GoodMs) lo = mid + 1; else hi = mid; }
        return lo;
    }
}
