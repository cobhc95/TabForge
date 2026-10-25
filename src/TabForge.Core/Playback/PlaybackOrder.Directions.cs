
namespace TabForge.Playback;

// Owns: applying the jump directions (D.C., D.S., Fine, To Coda) to an expanded repeat order, split from PlaybackOrder.cs.
// Does not own: the repeat expansion or the start-bar choice (PlaybackOrder).
public static partial class PlaybackOrder
{
    // -------------------------------------------------------------------------
    // Stage 2: directions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Copies the expanded order until one D.C./D.S. is taken, then plays straight through from its target.
    /// <list type="bullet">
    /// <item>Before the jump, Fine and To Coda are ignored. A jump bar is played in full, then the jump happens;
    /// inside a repeat it is acted on at its last performance, so the repeat plays out first.</item>
    /// <item>D.C. returns to the first performed bar, D.S. to the first segno, D.S.S. to the first double segno
    /// (or the first segno when there is none). A jump without a target is ignored.</item>
    /// <item>After the jump, repeats are not taken again and only the last ending of each repeat is played.
    /// Fine ends playback after its bar. After an "al Coda" jump, the first To Coda bar leads to the nearest
    /// coda after it (or the first one in the song); after an "al Fine" jump, To Coda is ignored.</item>
    /// </list>
    /// </summary>
    private static List<int> ApplyDirections(BarStructure score, List<int> expanded)
    {
        var output = new List<int>(expanded.Count);
        if (expanded.Count == 0) return output;

        var lastPerformance = new Dictionary<int, int>();
        for (var i = 0; i < expanded.Count; i++) lastPerformance[expanded[i]] = i;

        var beforeJumpLimit = Math.Max(256, 64L * expanded.Count);
        for (var i = 0; i < expanded.Count && output.Count < beforeJumpLimit; i++)
        {
            var bar = expanded[i];
            output.Add(bar);
            if (lastPerformance[bar] != i) continue;

            foreach (var jump in score.Jumps(bar))
            {
                var target = FindJumpTarget(score, expanded, jump.Kind);
                if (target < 0) continue;
                PlayAfterJump(score, output, target, jump.AlCoda);
                return output;
            }
        }
        return output;
    }

    private static int FindJumpTarget(BarStructure score, List<int> expanded, JumpKind kind)
    {
        switch (kind)
        {
            case JumpKind.DaCapo:
                return expanded[0];
            case JumpKind.DalSegno:
                return FirstPerformedWith(score, expanded, Mark.Segno);
            case JumpKind.DalSegnoSegno:
                var doubleSegno = FirstPerformedWith(score, expanded, Mark.SegnoSegno);
                return doubleSegno >= 0 ? doubleSegno : FirstPerformedWith(score, expanded, Mark.Segno);
            default:
                return -1;
        }
    }

    private static int FirstPerformedWith(BarStructure score, List<int> expanded, Mark mark)
    {
        foreach (var bar in expanded)
            if (score.Has(bar, mark)) return bar;
        return -1;
    }

    private static void PlayAfterJump(BarStructure score, List<int> output, int target, bool alCoda)
    {
        var n = score.BarCount;
        var limit = 4L * n + 16;
        long played = 0;
        var codaTaken = !alCoda;
        var bar = target;

        // Every step moves forward except the single coda jump, so this always ends.
        while (bar < n && played < limit)
        {
            if (score.DroppedAfterJump(bar)) { bar++; continue; }

            output.Add(bar);
            played++;
            if (score.Has(bar, Mark.Fine)) return;

            if (!codaTaken && score.Has(bar, Mark.ToCoda))
            {
                var coda = score.NearestCoda(bar);
                if (coda >= 0)
                {
                    codaTaken = true;
                    bar = coda;
                    continue;
                }
            }
            bar++;
        }
    }
}
