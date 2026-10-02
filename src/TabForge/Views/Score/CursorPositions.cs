using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

/// <summary>
/// Feature 6b: the only places the editing cursor may sit are real beat starts (note, rest or annotated beat) and, in an
/// incomplete bar, one append slot right after the last beat. An empty bar has its first slot. Clicks, arrow keys,
/// Home/End and Shift+arrow selection all use this one set.
/// </summary>
internal static class CursorPositions
{
    public static bool IsBeatCell(TabCell cell) => cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation;

    /// <summary>Allowed cursor cells in ascending order; never empty.</summary>
    public static List<int> Allowed(IReadOnlyList<TabCell> cells)
    {
        var allowed = new List<int>();
        var end = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            if (!IsBeatCell(cells[i])) continue;
            allowed.Add(i);
            end = Math.Max(end, ScoreEditCommands.BeatStart(cells[i], i) + MusicTime.CellSlots(cells[i]));
        }
        var append = (int)Math.Ceiling(end - 0.001);
        if (append < cells.Count && !allowed.Contains(append)) allowed.Add(append);
        if (allowed.Count == 0) allowed.Add(0);
        allowed.Sort();
        return allowed;
    }

    /// <summary>The allowed cell for a click at a slot position: the beat it lies inside, otherwise the nearest allowed position.</summary>
    public static int Resolve(IReadOnlyList<TabCell> cells, double slotPosition)
    {
        var allowed = Allowed(cells);
        foreach (var index in allowed)
        {
            if (!IsBeatCell(cells[index])) continue;
            var start = ScoreEditCommands.BeatStart(cells[index], index);
            if (slotPosition >= start - 0.001 && slotPosition < start + MusicTime.CellSlots(cells[index]) - 0.001) return index;
        }
        var best = allowed[0]; var bestDistance = double.MaxValue;
        foreach (var index in allowed)
        {
            var distance = Math.Abs(slotPosition - ScoreEditCommands.BeatStart(cells[index], index));
            if (distance < bestDistance) { best = index; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>
    /// The beat a selection end belongs to: the beat that covers grid cell <paramref name="cell"/>, so a selection is made of whole beats only. A cell in a gap
    /// between beats takes the next beat (<paramref name="preferNext"/>, a range start) or the previous one (a range end); a bar without beats keeps the cell.
    /// </summary>
    public static int SnapToBeat(IReadOnlyList<TabCell> cells, int cell, bool preferNext)
    {
        int previous = -1, next = -1;
        if (cell >= 0 && cell < cells.Count && IsBeatCell(cells[cell]) && ScoreEditCommands.BeatStart(cells[cell], cell) <= cell + 0.001) return cell;
        for (var i = 0; i < cells.Count; i++)
        {
            if (!IsBeatCell(cells[i])) continue;
            var start = ScoreEditCommands.BeatStart(cells[i], i);
            if (cell >= start - 0.001 && cell < start + MusicTime.CellSlots(cells[i]) - 0.001) return i;
            if (start <= cell + 0.001) previous = i;
            else if (next < 0) next = i;
        }
        var pick = preferNext ? (next >= 0 ? next : previous) : (previous >= 0 ? previous : next);
        return pick >= 0 ? pick : cell;
    }

    /// <summary>The allowed cursor cell for the grid cell <paramref name="cell"/> of a bar: the beat that holds it, otherwise the nearest allowed position.</summary>
    public static int Snap(IReadOnlyList<TabCell> cells, int cell) => Resolve(cells, Math.Clamp(cell, 0, Math.Max(0, cells.Count - 1)));

    /// <summary>The next allowed cell after <paramref name="cell"/> in the bar, or -1.</summary>
    public static int Next(IReadOnlyList<TabCell> cells, int cell) => Allowed(cells).FirstOrDefault(c => c > cell, -1);

    /// <summary>The previous allowed cell before <paramref name="cell"/> in the bar, or -1.</summary>
    public static int Previous(IReadOnlyList<TabCell> cells, int cell) => Allowed(cells).LastOrDefault(c => c < cell, -1);
}
