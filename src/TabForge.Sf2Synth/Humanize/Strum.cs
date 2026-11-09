using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: the strings of one strum (keys low to high): onset per string after the stroke (low to high on a downstroke, high to low
//   up), total spread by style, family, written dynamics (slower when soft), direction, laid-back bias and at most a share of the
//   interval; gaps first-longest to last-shortest with jitter, each under the fusion limit; level graded first to last, per-string
//   scatter, upstrokes soft on the lowest two strings, the lowest sometimes barely touched, inner strings sometimes soft when fast.
// Does not own: the stroke (Phrase), applying it (Humanizer), the values (HumanTable).
// Tests: HumanizerTests.
internal static class Strum
{
    /// <summary>Writes each note's extra level (dB) and onset (ms after the stroke). <paramref name="levelDb"/>: the written level re velocity 127.</summary>
    public static void Spread(Family f, Style s, in Stroke st, double levelDb, Span<double> db, Span<double> ms, Rng r)
    {
        var n = db.Length;
        db.Clear();
        ms.Clear();
        if (n < 2 || f.Strum == 0) return;
        var dyn = levelDb < MfDb ? PpX + (1 - PpX) * Math.Clamp((levelDb - PpDb) / (MfDb - PpDb), 0, 1)
                                 : 1 + (FfX - 1) * Math.Clamp((levelDb - MfDb) / (FfDb - MfDb), 0, 1);
        var total = s.StrumMs * f.Strum * dyn * (st.Down ? 1 : UpX) * (1 + BiasStrumPerMs * Math.Max(0, s.BiasMs)) * (n - 1) / 5;
        total = Math.Min(Math.Min(total, IoiFrac * st.IoiMs), StrumCapMs);
        double sum = 0, t = 0;
        for (var h = 1; h < n; h++) sum += ms[h] = (GapFirst + (GapLast - GapFirst) * (n > 2 ? (h - 1.0) / (n - 2) : 0)) * (1 + GapJit * r.U());
        for (var h = 1; h < n; h++) ms[h] = t += Math.Min(ms[h] * total / sum, GapMaxMs);
        if (!st.Down) ms.Reverse();
        var fast = st.IoiMs < SlowIoi * 1000;
        for (var i = 0; i < n; i++)
        {
            var h = st.Down ? i : n - 1 - i;
            var d = FirstDb + (LastDb - FirstDb) * h / (n - 1.0) + StringDb * r.G();
            if (!st.Down && n >= 3)
            {
                if (i < 2) d += UpLowDb;
                if (i == 0 && r.U01() < MissChance) d += MissDb;
                else if (fast && i > 0 && i < n - 1 && r.U01() < InnerChance) d += InnerDb;
            }
            db[i] = d * s.KDb;
        }
    }
}
