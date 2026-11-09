using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: the window around each stroke's bar (Before bars back, After ahead): its energy (strokes per beat) and the evidence of a
//   human player in its written notes, as flatness 0..1 per kind (1 = flat: humanize fully,
//   0 = played: leave alone). Velocity is human when it has many distinct values (more than 4 levels, counted as if the window
//   held at least FullWindow notes) and either a real spread or short runs of equal values; timing is human when few onsets sit
//   on the written grid (Grid positions per beat, within GridMs). Fewer than MinNotes notes: flat (a score, nothing to judge).
// Does not own: the strokes and their context (Humanizer), what changes (Dynamics, Timing).
// Tests: HumanizerTests.
internal static class Evidence
{
    // Energy (strokes per beat) and the evidence of flat input over the window around each stroke's bar.
    public static void Window(HumanNote[] a, List<List<int>> st, Ctx[] c)
    {
        var bars = new SortedDictionary<int, (int From, int To, int Beats)>();
        for (var k = 0; k < st.Count; k++)
        {
            ref readonly var x = ref a[st[k][0]];
            bars[x.Bar] = bars.TryGetValue(x.Bar, out var b) ? (b.From, k + 1, b.Beats) : (k, k + 1, Math.Max(1, x.BeatsPerBar));
        }
        var done = new Dictionary<int, (double Npb, double Vf, double Tf)>();
        for (var k = 0; k < st.Count; k++)
        {
            var bar = a[st[k][0]].Bar;
            if (!done.TryGetValue(bar, out var v))
            {
                int strokes = 0, beats = 0;
                var w = new List<HumanNote>();
                foreach (var (key, b) in bars)
                {
                    if (key < bar - Before || key > bar + After) continue;
                    (strokes, beats) = (strokes + b.To - b.From, beats + b.Beats);
                    for (var j = b.From; j < b.To; j++) foreach (var i in st[j]) w.Add(a[i]);
                }
                var (vf, tf) = Flatness(w);
                done[bar] = v = (strokes / (double)beats, vf, tf);
            }
            (c[k].Npb, c[k].Vf, c[k].Tf) = v;
        }
    }

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
