using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the score selection's drawing geometry: the beat cells a cell range covers and their duration span in slots.
// Does not own: the selection state (EditorSelectionState), the range test (ScoreEditorGeometry) or the slot positions (CursorPositions).
// Tests: TabForge.Avalonia.Tests.ScoreSelectionDrawingTests.

/// <summary>The selected range as drawing geometry only; the selection state and its snapping stay in EditorSelectionState.</summary>
internal static class ScoreSelectionGeometry
{
    /// <summary>
    /// The first and last beat cells of a range inside one bar. Each end is clamped to the bar and moved onto the beat that holds it;
    /// a range end on an empty cell takes the beat before it and an end of -1 (a whole bar) the bar's last beat, never a single empty slot.
    /// </summary>
    internal static (int First, int Last) BeatCells(IReadOnlyList<TabCell> cells, int from, int to)
    {
        if (cells.Count == 0) return (0, 0);
        var first = Math.Clamp(from, 0, cells.Count - 1);
        if (!CursorPositions.IsBeatCell(cells[first])) first = CursorPositions.SnapToBeat(cells, first, preferNext: true);
        var last = to < 0 ? cells.Count - 1 : Math.Clamp(to, 0, cells.Count - 1);
        if (!CursorPositions.IsBeatCell(cells[last])) last = CursorPositions.SnapToBeat(cells, last, preferNext: false);
        if (last < first) last = first;
        return (first, last);
    }

    /// <summary>The range's span in slots: from where the first beat is drawn to the end of the last beat's own duration.</summary>
    internal static (double Start, double End) SpanSlots(IReadOnlyList<TabCell> cells, int from, int to)
    {
        if (cells.Count == 0) return (0, 1);
        var (first, last) = BeatCells(cells, from, to);
        return (CursorPositions.DrawnStart(cells, first), CursorPositions.DrawnStart(cells, last) + MusicTime.CellSlots(cells[last]));
    }

    /// <summary>True when the bar and cell lie inside the inclusive range, whichever end was set first.</summary>
    internal static bool Selected(int m1, int c1, int m2, int c2, int measure, int cell)
    {
        if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
        return ScoreEditorGeometry.IsInRange(measure, cell, m1, c1, m2, c2);
    }
}
