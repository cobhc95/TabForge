using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: the evidence of a human player in the written notes of one window, as flatness 0..1 per kind (1 = flat: humanize fully,
//   0 = played: leave alone). Velocity is human when it has many distinct values (more than 4 levels, counted as if the window
//   held at least FullWindow notes) and either a real spread or short runs of equal values; timing is human when few onsets sit
//   on the written grid (Grid positions per beat, within GridMs). Fewer than MinNotes notes: flat (a score, nothing to judge).
// Does not own: the window (Humanizer), what changes (Dynamics, Timing).
// Tests: HumanizerTests.
internal static class Evidence
{
    public static (double Vel, double Time) Flatness(List<HumanNote> w)
    {
        if (w.Count < MinNotes) return (1, 1);
        double sum = 0, sq = 0;
        int run = 1, maxRun = 1, grid = 0;
        var seen = new HashSet<int>();
        for (var i = 0; i < w.Count; i++)
        {
            var v = w[i].Velocity;
            (sum, sq) = (sum + v, sq + v * v);
            seen.Add(v);
            if (i > 0) { run = w[i - 1].Velocity == v ? run + 1 : 1; maxRun = Math.Max(maxRun, run); }
            var g = w[i].Beat * Grid;
            if (Math.Abs(g - Math.Round(g)) * w[i].BeatMs / Grid < GridMs) grid++;
        }
        var n = w.Count;
        var std = Math.Sqrt(Math.Max(0, sq / n - sum * sum / n / n));
        var distinct = seen.Count * Math.Max(1, FullWindow / (double)n);
        var human = Math.Min(Smooth(DistinctLo, DistinctHi, distinct), Math.Max(Smooth(StdLo, StdHi, std), 1 - Smooth(RunLo, RunHi, maxRun)));
        return (1 - human, Smooth(GridLo, GridHi, grid / (double)n));
    }
}
