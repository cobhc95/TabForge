using TabForge.Models;

namespace TabForge.Services;

// Owns: the model half of the selection-wide edits (delete beat, insert/delete beats, tenuto over a range).
// Does not own: which beats are selected (SelectionModel / the editor).
// Tests: TestSelectionWideEdits.
public static partial class EditCommands
{
    /// <summary>Delete (beat): clears the notes of every selected beat; the beat itself stays.</summary>
    public static void ClearBeats(IReadOnlyList<TabCell> cells, bool toRest = false)
    {
        // With the rest fill on, a beat that held notes (or a rest) becomes a rest of the same length.
        foreach (var cell in cells) { var was = cell.Notes.Count > 0 || cell.IsRest; cell.Notes.Clear(); cell.IsRest = toRest && was; }
    }

    /// <summary>Tenuto over a selection: on unless every beat already has it.</summary>
    public static void ToggleTenuto(IReadOnlyList<TabCell> cells)
    {
        var value = !cells.All(cell => cell.Tenuto);
        foreach (var cell in cells) cell.Tenuto = value;
    }

    /// <summary>Inserts <paramref name="beat"/> at <paramref name="at"/>, shifting the rest of the bar right; only empty trailing cells fall off; a full bar keeps its notes and overflows.</summary>
    public static void InsertBeatAt(List<TabCell> cells, int at, int slots, TabCell beat)
    {
        at = Math.Clamp(at, 0, Math.Max(0, slots - 1));
        for (var i = at; i < cells.Count; i++)
            if (cells[i].RhythmicPosition is { } position) cells[i].RhythmicPosition = position + 1;
        cells.Insert(Math.Min(at, cells.Count), beat);
        while (cells.Count > slots && cells[^1].Notes.Count == 0) cells.RemoveAt(cells.Count - 1);
        while (cells.Count < slots) cells.Add(new TabCell());
    }

    /// <summary>Deletes the beats <paramref name="first"/>..<paramref name="last"/> (with the slots the last one spans), shifting the rest of the bar left.</summary>
    public static void DeleteBeatsAt(List<TabCell> cells, int first, int last, int slots)
    {
        first = Math.Max(0, first);
        last = Math.Max(first, last);
        var tail = last < cells.Count ? Math.Max(1, MusicTime.CellSlotsRounded(cells[last])) : 1;
        var count = last + tail - first;
        // Positioned beats after the range move left by the time removed, not by the cell count (short notes and tuplets hold less than a slot per cell), so they keep distinct onsets.
        var stop = Math.Min(first + count, cells.Count);
        var onsets = BarGrid.Onsets(cells);
        var span = stop < cells.Count && first < stop ? Math.Min(count, Math.Max(0, onsets[stop] - onsets[first])) : count;
        for (var i = stop; i < cells.Count; i++)
            if (cells[i].RhythmicPosition is { } position) cells[i].RhythmicPosition = Math.Max(0, position - span);
        for (var i = 0; i < count && first < cells.Count; i++) cells.RemoveAt(first);
        // No positioned beat starts inside the beat before it: it follows that beat's end by its real length (a tuplet's remaining members included).
        var end = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation) continue;
            if (cell.RhythmicPosition is { } at && at < end - 1e-6) cell.RhythmicPosition = end;
            end = Math.Max(end, cell.RhythmicPosition ?? Math.Max(i, end)) + MusicTime.CellSlots(cell);
        }
        while (cells.Count < slots) cells.Add(new TabCell());
    }

    /// <summary>The tidy pass after beats left a voice (Delete and Cut on a selection): empty cells past the bar's <paramref name="slots"/> go, and a
    /// voice left with only automatic rests becomes one whole-bar rest (<paramref name="wholeRest"/>) or empty. A rest the user wrote stays.</summary>
    public static void TidyAfterRemoval(List<TabCell> cells, int slots, bool wholeRest)
    {
        while (cells.Count > slots && cells[^1] is { Notes.Count: 0, IsRest: false, HasAnnotation: false }) cells.RemoveAt(cells.Count - 1);
        if (cells.Any(c => c.IsRest) && !cells.Any(c => c.IsRest && c.WrittenRest)) BarFill.ResetEmptyVoice(cells, slots, wholeRest);
    }
}
