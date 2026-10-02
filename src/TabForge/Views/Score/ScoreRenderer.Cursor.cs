using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

// ScoreRenderer: the edit cursor and cell highlight rectangles.
// Owns: cursor/highlight geometry. Does not own: drawing the cursor. Tests: render identity, TestTabEditorRenderInvariance.

internal sealed partial class ScoreRenderer
{
    /// <summary>
    /// Shared horizontal bounds for the edit cursor and note hover. A populated cell occupies its
    /// actual rhythmic duration; empty cells retain the one-slot cursor width.
    /// </summary>
    /// <summary>
    /// The edit cursor box: always centred on the beat head (the same x the fret number or notehead is drawn at) and as wide as its glyph,
    /// never the beat's duration span. A cell that is not a beat (a stale or empty slot) shows at the nearest allowed beat.
    /// </summary>
    internal static Rect CursorRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
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
        return new Rect(centre - width / 2, y, width, height);
    }

    internal static Rect CellHighlightRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
        double measureX, double slotWidth, double y, double height) =>
        CellHighlightRect(measure, cells, cellIndex, measureX, slotWidth, null, 0, y, height);

    internal static Rect CellHighlightRect(MeasureModel measure, IReadOnlyList<TabCell> cells, int cellIndex,
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
            return new Rect(left + 1, y, Math.Max(6, right - left - 2), height);
        }
        return new Rect(measureX + startSlots * slotWidth + 1, y,
            Math.Max(slotWidth, widthSlots * slotWidth) - 2, height);
    }

}
