using TabForge.Models;

namespace TabForge.Services;

// Owns: finding bars that hold no notes on any track, and removing them from every track.
// Does not own: the undo step (ArrangementController) or the question (EmptyBarFlow).
// Tests: TestBarDeleteGuards.
internal static class EmptyBars
{
    /// <summary>True when no track has a note in either voice of <paramref name="bar"/> (rests, annotations and empty cells are fine).</summary>
    public static bool IsEmpty(SongProject project, int bar) =>
        project.Tracks.All(t => bar >= t.Measures.Count || (t.Measures[bar].Cells.All(c => c.Notes.Count == 0) && t.Measures[bar].Voice2Cells.All(c => c.Notes.Count == 0)));

    /// <summary>The empty bars of [first, last], ascending; never all the bars of the song (one stays).</summary>
    public static List<int> InRange(SongProject project, int first, int last)
    {
        var bars = new List<int>();
        var count = BarRangeEditor.MaxMeasures(project);
        for (var bar = Math.Max(0, first); bar <= Math.Min(last, count - 1); bar++)
            if (IsEmpty(project, bar)) bars.Add(bar);
        if (bars.Count >= count && bars.Count > 0) bars.RemoveAt(0);
        return bars;
    }

    /// <summary>Removes <paramref name="bars"/> (ascending) from every track; the result maps each old bar to its new number (-1 = removed).</summary>
    public static int[] Remove(SongProject project, IReadOnlyList<int> bars)
    {
        var oldCount = BarRangeEditor.MaxMeasures(project);
        for (var i = bars.Count - 1; i >= 0; i--) BarRangeEditor.Remove(project, bars[i], bars[i]);
        var set = new HashSet<int>(bars);
        var removedBefore = 0;
        var map = new int[oldCount];
        for (var b = 0; b < oldCount; b++)
        {
            if (set.Contains(b)) { map[b] = -1; removedBefore++; }
            else map[b] = b - removedBefore;
        }
        return map;
    }
}
