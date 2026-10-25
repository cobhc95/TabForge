using TabForge.Platform;
using System.Globalization;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// ScoreRenderer: the edit cursor and cell highlight rectangles.
// Owns: cursor/highlight geometry. Does not own: drawing the cursor. Tests: render identity, TestTabEditorRenderInvariance.

internal sealed partial class ScoreRenderer
{
    /// <summary>
    /// The edit cursor box, also used for the hover outline: centred on the beat head (the same x the fret number or notehead
    /// is drawn at) and as wide as its glyph, never the beat's duration span. A cell that is not a beat (a stale or empty slot)
    /// shows at the nearest allowed beat.
    /// </summary>
    internal static UiRect CursorRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, MeasureWarp warp, double measureWidth, double y, double height)
    {
        if (cellIndex < 0 || cellIndex >= cells.Count || !CursorPositions.IsBeatCell(cells[cellIndex]))
        {
            var allowed = CursorPositions.Allowed(cells);
            if (!allowed.Contains(cellIndex))
                cellIndex = allowed.LastOrDefault(c => c <= cellIndex, allowed[0]);
        }
        var cell = cellIndex >= 0 && cellIndex < cells.Count ? cells[cellIndex] : null;
        var digits = cell is null || cell.Notes.Count == 0 ? 1 : cell.Notes.Max(n => n.Fret.ToString().Length);
        var width = Math.Max(14, 7 * digits + 6);
        var centre = measureX + warp.CenterFraction(CellStartSlots(measure, cellIndex, cells)) * measureWidth;
        return new UiRect(centre - width / 2, y, width, height);
    }

    /// <summary>
    /// The hover outline: the cursor box, widened to the cell's duration span when the cell holds notes or a rest. Its left edge stays
    /// on the cursor box; its right edge is the span's end, never past the bar. An empty cell keeps the cursor box.
    /// </summary>
    internal static UiRect HoverRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, MeasureWarp warp, double measureWidth, double y, double height)
    {
        var box = CursorRect(measure, cells, cellIndex, measureX, warp, measureWidth, y, height);
        var cell = cellIndex >= 0 && cellIndex < cells.Count ? cells[cellIndex] : null;
        if (cell is null || (cell.Notes.Count == 0 && !cell.IsRest)) return box;
        // The beat's own length from where it is drawn: a note shorter than a sixteenth ends at the next note's column.
        var right = Math.Min(measureX + measureWidth, measureX + warp.Fraction(CellStartSlots(measure, cellIndex, cells) + MusicTime.CellSlots(cell)) * measureWidth);
        return new UiRect(box.X, y, Math.Max(box.Width, right - box.X), height);
    }

    internal static UiRect CellHighlightRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, double slotWidth, double y, double height) =>
        CellHighlightRect(measure, cells, cellIndex, measureX, slotWidth, null, 0, y, height);

    internal static UiRect CellHighlightRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, double slotWidth, MeasureWarp? warp, double measureWidth, double y, double height)
    {
        var cell = cellIndex >= 0 && cellIndex < cells.Count ? cells[cellIndex] : null;
        var widthSlots = cell is not null && (cell.Notes.Count > 0 || cell.IsRest)
            ? Math.Max(1, MusicTime.CellSlotsRounded(cell)) : 1;
        var startSlots = CellStartSlots(measure, cellIndex, cells);
        if (warp is not null && measureWidth > 0)
        {
            var left = measureX + warp.Fraction(startSlots) * measureWidth;
            var right = measureX + warp.Fraction(startSlots + widthSlots) * measureWidth;
            return new UiRect(left + 1, y, Math.Max(6, right - left - 2), height);
        }
        return new UiRect(measureX + startSlots * slotWidth + 1, y,
            Math.Max(slotWidth, widthSlots * slotWidth) - 2, height);
    }

}
