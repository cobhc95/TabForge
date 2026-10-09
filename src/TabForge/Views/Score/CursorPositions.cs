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

    /// <summary>Allowed cursor cells in ascending order; never empty. <paramref name="barSlots"/> is the bar's length in slots (0 = the cell count); a full bar has no append slot.</summary>
    public static List<int> Allowed(IReadOnlyList<TabCell> cells, int barSlots = 0)
    {
        var allowed = new List<int>();
        for (var i = 0; i < cells.Count; i++) if (IsBeatCell(cells[i])) allowed.Add(i);
        var end = End(cells);
        var append = (int)Math.Ceiling(end - 0.001);
        // An overfull bar keeps an empty spot after its last beat once its cells reach it (RightTarget pads them), as GP5.
        var capacity = barSlots > 0 && end <= barSlots + 0.001 ? Math.Min(barSlots, cells.Count) : cells.Count;
        if (end < capacity - 0.001 && append < capacity && !allowed.Contains(append)) allowed.Add(append);
        if (allowed.Count == 0) allowed.Add(0);
        allowed.Sort();
        return allowed;
    }

    /// <summary>
    /// Where the bar's beats end in slots, played one after another (see <see cref="Starts"/>).
    /// </summary>
    public static double End(IReadOnlyList<TabCell> cells)
    {
        var starts = Starts(cells);
        var end = 0.0;
        for (var i = 0; i < cells.Count; i++) if (starts[i] >= 0) end = starts[i] + MusicTime.CellSlots(cells[i]);
        return end;
    }

    /// <summary>
    /// Where each beat of the bar starts in slots, played one after another (-1 for a cell that is not a beat): a beat that starts inside an
    /// earlier one's span (an overfull bar) begins where that one ends. A grid beat less than one slot after that end sits on the cell the grid
    /// rounded a tuplet's end up to, and a written beat after a tuplet's off-grid end sits on the empty spot the grid placed later: both begin
    /// at the exact end (GP5 adds triplets to the bar until their true lengths fill it: quiet GP5, work/gp5diff/i4micro i11, verify3/micro6 x12, x13).
    /// </summary>
    public static double[] Starts(IReadOnlyList<TabCell> cells)
    {
        var starts = new double[cells.Count];
        var end = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            if (!IsBeatCell(cells[i])) { starts[i] = -1; continue; }
            var start = ScoreEditCommands.BeatStart(cells[i], i);
            var offGrid = Math.Abs(end - Math.Round(end)) > 0.001 && (cells[i].Notes.Count > 0 || cells[i].WrittenRest);
            if (cells[i].RhythmicPosition is null && start > end && (start - end < 1 - 0.001 || offGrid)) start = end;
            starts[i] = Math.Max(end, start);
            end = starts[i] + MusicTime.CellSlots(cells[i]);
        }
        return starts;
    }

    /// <summary>
    /// Where the cell is drawn, in slots, as the staff layout places it: its written position, else its cell, or where the beat before ends when
    /// that is later (a beat that starts inside an earlier one's span in an overfull bar). A cell that is not a beat stays at its index.
    /// </summary>
    public static double DrawnStart(IReadOnlyList<TabCell> cells, int index)
    {
        static double Written(TabCell cell) => cell.RhythmicPosition is { } exact && double.IsFinite(exact) ? Math.Max(0, exact) : -1;
        if (index < 0 || index >= cells.Count) return Math.Max(0, index);
        if (Written(cells[index]) is >= 0 and var at) return at;
        if (!IsBeatCell(cells[index])) return index;
        var end = 0.0;
        for (var i = 0; i < index; i++)
        {
            if (!IsBeatCell(cells[i])) continue;
            var start = Written(cells[i]) is >= 0 and var w ? w : Math.Max(i, end);
            end = Math.Max(end, start + MusicTime.CellSlots(cells[i]));
        }
        return Math.Max(index, end);
    }

    /// <summary>The allowed cell for a click at a slot position: the beat it lies inside, otherwise the nearest allowed position.</summary>
    public static int Resolve(IReadOnlyList<TabCell> cells, double slotPosition, int barSlots = 0)
    {
        var allowed = Allowed(cells, barSlots);
        var starts = Starts(cells);
        foreach (var index in allowed)
        {
            if (!IsBeatCell(cells[index])) continue;
            var start = starts[index];
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
    public static int Snap(IReadOnlyList<TabCell> cells, int cell, int barSlots = 0) => Resolve(cells, Math.Clamp(cell, 0, Math.Max(0, cells.Count - 1)), barSlots);

    /// <summary>
    /// Where Shift+Right extends a selection inside the bar, or -1 for the next bar: the next written beat. The empty spot is not selectable, so from
    /// the last written beat a selection goes on into the next bar or stays at the song's end (quiet GP5, work/gp5diff/i4micro3 i19-i21).
    /// </summary>
    public static int NextSelectable(List<TabCell> cells, int cell, int barSlots)
    {
        var next = Next(cells, cell, barSlots);
        return next >= 0 && next < cells.Count && IsBeatCell(cells[next]) && next != EmptySpot(cells) ? next : -1;
    }

    /// <summary>
    /// Where Shift+Right (<paramref name="direction"/> 1) or Shift+Left moves a selection end at <paramref name="cell"/>: the cell, or -1 for the next or
    /// previous bar, and whether the range now takes in the empty spot. From the last written beat Shift+Right goes onto the empty spot (the end stays on
    /// that beat), from there into the next bar (at the song's end it stays); Shift+Left from the spot goes back onto the beat (quiet GP5:
    /// work/gp5diff/verify3/micro6 x10, b5runs b01 / b03 / b08, %TEMP%/tf-k6gp/k6 k01-k03).
    /// </summary>
    public static (int Step, bool OnSpot) SelectionStep(List<TabCell> cells, int cell, int barSlots, int direction, bool onSpot, bool lastBar)
    {
        if (direction < 0) return (onSpot ? cell : Previous(cells, cell, barSlots), false);
        if (NextSelectable(cells, cell, barSlots) is >= 0 and var next) return (next, false);
        return Next(cells, cell, barSlots) >= 0 && (!onSpot || lastBar) ? (cell, true) : (-1, false);
    }

    /// <summary>The next allowed cell after <paramref name="cell"/> in the bar, or -1.</summary>
    public static int Next(IReadOnlyList<TabCell> cells, int cell, int barSlots = 0) => Allowed(cells, barSlots).FirstOrDefault(c => c > cell, -1);

    /// <summary>
    /// The empty spot of a bar, where the next note goes: its first placeholder rest (a fill rest with no note after it, see
    /// <see cref="WritingDuration"/>), or -1 when the bar has none. A bar of rests only has none: its rests may be written ones (R).
    /// </summary>
    public static int EmptySpot(IReadOnlyList<TabCell> cells)
    {
        if (!cells.Any(c => c.Notes.Count > 0 || c.WrittenRest)) return -1;
        for (var i = 0; i < cells.Count; i++)
            if (cells[i].IsRest && !cells[i].WrittenRest && cells[i].Notes.Count == 0 && WritingDuration.Applies(cells, cells[i]) && !After(cells, i, c => c.WrittenRest)) return i;
        return -1;
    }

    private static bool After(IReadOnlyList<TabCell> cells, int at, Func<TabCell, bool> test)
    {
        for (var i = at + 1; i < cells.Count; i++) if (test(cells[i])) return true;
        return false;
    }

    /// <summary>
    /// Right at <paramref name="cell"/> of the last bar adds a bar when Right has nowhere to go in it, an empty last bar too (GP5: Right in
    /// a new song's empty bar makes bar 2).
    /// </summary>
    public static bool AtEnd(List<TabCell> cells, int cell, int barSlots) => RightTarget(cells, cell, barSlots) < 0;

    /// <summary>
    /// Where Right goes inside the bar, or -1 for the next bar (GP5): from the empty spot (or a fill rest after it) to the next bar, so the
    /// automatic fill rests are skipped; from the last beat of an overfull bar to the empty spot after it, which <paramref name="pad"/>
    /// makes room for by adding empty cells.
    /// </summary>
    public static int RightTarget(List<TabCell> cells, int cell, int barSlots, bool pad = false)
    {
        var spot = EmptySpot(cells);
        if (spot >= 0 && cell >= spot) return -1;
        var next = Next(cells, cell, barSlots);
        if (next >= 0 || barSlots <= 0) return next;
        var end = End(cells);
        var append = (int)Math.Ceiling(end - 0.001);
        if (end <= barSlots + 0.001 || cell >= append || append >= Services.InputLimits.MaxCellsPerMeasure) return -1;   // a bar holds at most that many cells: then the next bar
        while (pad && cells.Count <= append) cells.Add(new TabCell());
        return append;
    }

    /// <summary>Where Left from the next bar lands: the empty spot, else the last allowed cell.</summary>
    public static int BarEndCursor(IReadOnlyList<TabCell> cells, int barSlots) => EmptySpot(cells) is >= 0 and var spot ? spot : Allowed(cells, barSlots).Last();

    /// <summary>End (GP5): the last written beat before the empty spot; the first cell of a bar with nothing written.</summary>
    public static int LastWritten(IReadOnlyList<TabCell> cells, int barSlots)
    {
        var spot = EmptySpot(cells);
        var allowed = Allowed(cells, barSlots).Where(c => c < cells.Count && IsBeatCell(cells[c]) && (spot < 0 || c < spot)).ToList();
        return allowed.Count > 0 ? allowed[^1] : Allowed(cells, barSlots)[0];
    }

    /// <summary>End from <paramref name="cell"/> (GP5): the last written beat, but the empty spot after it stays put (quiet GP5 micro6 x09, x11).</summary>
    public static int EndTarget(IReadOnlyList<TabCell> cells, int cell, int barSlots)
    {
        var last = LastWritten(cells, barSlots);
        return cell > last && Allowed(cells, barSlots).Contains(cell) ? cell : last;
    }

    /// <summary>The previous allowed cell before <paramref name="cell"/> in the bar, or -1.</summary>
    public static int Previous(IReadOnlyList<TabCell> cells, int cell, int barSlots = 0) => Allowed(cells, barSlots).LastOrDefault(c => c < cell, -1);
}
