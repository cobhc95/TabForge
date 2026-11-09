using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: one channel's evidence of a human player in the incoming notes, smoothed into VelFlat and TimeFlat 0..1 (0 = human:
//   untouched). Velocity is human when it has many distinct values (more than 4 levels, counted as if the window were full) and
//   either a real spread or short runs of equal values; timing is human when few inter-onset intervals sit in exact simple
//   ratios (within 0.1 ms) to another. Fewer than MinNotes notes: no evidence, nothing changes. Allocation-free rings.
// Does not own: what is changed (Phrase, Humanizer), the pulse (Phrase reads Onset).
// Tests: HumanizerTests.
internal sealed class Evidence
{
    private readonly int[] _vel = new int[Window];
    private readonly long[] _on = new long[Window];
    private int _nv, _vi, _no, _oi;

    public double VelFlat { get; private set; }
    public double TimeFlat { get; private set; }
    public int Onsets => _no;

    public void Reset() => (_nv, _vi, _no, _oi, VelFlat, TimeFlat) = (0, 0, 0, 0, 0, 0);

    public void AddNote(int velocity) { _vel[_vi] = velocity; _vi = (_vi + 1) % Window; _nv = Math.Min(_nv + 1, Window); }

    public void AddOnset(long frame) { _on[_oi] = frame; _oi = (_oi + 1) % Window; _no = Math.Min(_no + 1, Window); }

    /// <summary>Onset frames, newest first (back = 0).</summary>
    public long Onset(int back) => _on[(_oi - 1 - back + 2 * Window) % Window];

    public void Update(double rate)
    {
        VelFlat = Follow(VelFlat, _nv < MinNotes ? 0 : 1 - VelHuman());
        TimeFlat = Follow(TimeFlat, _no < MinNotes ? 0 : Smooth(GridLo, GridHi, OnGrid(rate)));
    }

    private static double Follow(double f, double target)
    {
        f += (target - f) * HumanTable.Follow;
        return f < Snap ? 0 : f;
    }

    private int Vel(int back) => _vel[(_vi - 1 - back + 2 * Window) % Window];

    private double VelHuman()
    {
        double sum = 0, sq = 0;
        int distinct = 0, run = 1, maxRun = 1;
        for (var i = 0; i < _nv; i++)
        {
            var v = Vel(i);
            (sum, sq) = (sum + v, sq + v * v);
            var seen = false;
            for (var j = 0; j < i && !seen; j++) seen = Vel(j) == v;
            if (!seen) distinct++;
            if (i > 0) { run = Vel(i - 1) == v ? run + 1 : 1; maxRun = Math.Max(maxRun, run); }
        }
        var std = Math.Sqrt(Math.Max(0, sq / _nv - sum * sum / _nv / _nv));
        return Math.Min(Smooth(DistinctLo, DistinctHi, distinct * (double)Window / _nv),Math.Max(Smooth(StdLo, StdHi, std), 1 - Smooth(RunLo, RunHi, maxRun)));
    }

    // Fraction of intervals that equal another one times a simple ratio m:n (1..4) within SameMs.
    private double OnGrid(double rate)
    {
        var tol = SameMs * rate / 1000;
        int n = _no - 1, hits = 0;
        for (var i = 0; i < n; i++)
        {
            var a = Onset(i) - Onset(i + 1);
            var hit = false;
            for (var j = 0; j < n && !hit; j++)
            {
                if (j == i) continue;
                var b = Onset(j) - Onset(j + 1);
                for (var m = 1; m <= 4 && !hit; m++)
                    for (var k = 1; k <= 4 && !hit; k++) hit = Math.Abs(a * m - b * k) <= tol * Math.Max(m, k);
            }
            if (hit) hits++;
        }
        return n > 0 ? (double)hits / n : 0;
    }
}
