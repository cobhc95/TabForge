using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: a stroke's onset offset in ms (+ = late) and its notes' length change. Offset = feel and family bias + slow tempo wander
//   (octaves of value noise over 1..8 bars, keyed by position: no running state) + smooth micro pushes and pulls (1/8..1/2 bar) +
//   a small jitter + the motif's own timing; in fast runs also the group's first note early, upstrokes late, string-crossing delay,
//   a rush on rising and a lean on falling lines, legato notes late, and a rare smoothly early or late note. Offsets are roughly
//   constant in ms (not a share of the interval) and capped well inside the neighbouring intervals, so nothing flams or swaps.
//   Lengths: legato overlap or a small gap, from the motif, so a repeated riff keeps its articulation.
// Does not own: the context (Humanizer), the level (Dynamics), strums (Strum), the values (HumanTable).
// Tests: HumanizerTests.
internal static class Timing
{
    public static double Shift(in Ctx c, in HumanNote n, Style s, Family f, uint seed, bool drift)
    {
        var x = n.Bar + n.Beat / Math.Max(1, n.BeatsPerBar);
        var q = (int)Math.Round(n.Beat * Grid);
        var k = c.Chug ? ChugK : 1;
        var ms = (drift ? WanderMs * Hash.Octaves(Hash.Of(seed, 0x7A), x, 1, 4, 0.5) : 0)
            + MicroMs * Hash.Octaves(Hash.Of(seed, 0x3C), x, 0.125, 3, 0.5)
            + k * JitMs * Hash.G(Hash.Of(seed, n.Bar, q, 0x11))
            + k * MotifMs * Hash.G(Hash.Of(c.Motif, c.MotifIndex, 3));
        if (c.Fast)
        {
            if ((int)Math.Round(n.Beat * c.Sub) % c.Group == 0) ms += GroupMs;
            if (!c.Down && !c.Legato) ms += UpMs;
            if (c.Legato) ms += LegatoMs;
            if (c.PrevKey >= 0 && c.NextKey >= 0) ms -= RushMs * Math.Clamp((c.NextKey - c.PrevKey) / 4.0, -1, 1);
        }
        if (c.Single && c.PrevString >= 0 && c.String >= 0 && c.String != c.PrevString)
            ms += CrossMs + CrossStepMs * (Math.Min(Math.Abs(c.String - c.PrevString), 3) - 1);
        if (c.Single && Hash.U01(Hash.Of(seed, n.Bar, q, 0x0D)) < s.OddRate) ms += OddMs * (Hash.U(Hash.Of(seed, n.Bar, q, 0x0E)) < 0 ? -1 : 1);
        ms = ms * s.KMs * f.TimeK + s.BiasMs + f.BiasMs;
        var cap = Math.Min(ShiftCapMs * s.KMs, IoiShare * Math.Min(c.IoiPrev, c.IoiNext));
        return Math.Clamp(ms, -cap, cap);
    }

    /// <summary>The length change in ms (+ = longer): a share of the note, at most LenMaxMs, the same for the same motif position.</summary>
    public static double Length(in Ctx c, in HumanNote n, Style s)
    {
        var len = Math.Min(n.OffMs - n.OnMs, double.IsFinite(c.IoiNext) ? c.IoiNext : double.MaxValue);
        return Math.Min(LenFrac * len, LenMaxMs) * Hash.U(Hash.Of(c.Motif, c.MotifIndex, 4)) * s.KMs;
    }
}
