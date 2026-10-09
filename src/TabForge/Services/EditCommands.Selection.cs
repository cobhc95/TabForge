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
        for (var i = Math.Min(first + count, cells.Count); i < cells.Count; i++)
            if (cells[i].RhythmicPosition is { } position) cells[i].RhythmicPosition = Math.Max(0, position - count);
        for (var i = 0; i < count && first < cells.Count; i++) cells.RemoveAt(first);
        while (cells.Count < slots) cells.Add(new TabCell());
    }
}
