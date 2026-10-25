using TabForge.Models;

namespace TabForge.Views.Score;

// Owns: the editor's click hit rule: the bar, beat and string under a page point (system row, bar by x, slot by the bar's warp, beat snapped by CursorPositions, string by row).
// Does not own: the page metrics (passed in by the host) or what the click then does.
// Tests: ScorePreviewEditorTests (Avalonia), ScoreHitRuleTests.
internal static class ScoreHitRule
{
    /// <param name="cellsFor">The beats the cursor may sit on in a bar (the active voice).</param>
    internal static (int Measure, int Cell, int String) HitTest(ScoreLayoutEngine layoutEngine, TrackModel track, double x, double y,
        double headerHeight, double systemHeight, Func<int, double> tabTop, double stringGap,
        Func<int, int> slotsFor, Func<MeasureModel, IReadOnlyList<TabCell>> cellsFor)
    {
        var system = Math.Max(0, (int)((y - headerHeight) / systemHeight));
        var layout = layoutEngine.GetLayout(track);
        system = Math.Clamp(system, 0, layout.SystemCount - 1);
        var row = layout.Systems[system];
        var position = row.Measures.FirstOrDefault(item => x >= item.X && x < item.X + item.Width);
        if (position.Width <= 0)
            position = x < row.Measures[0].X ? row.Measures[0] : row.Measures[^1]; // the clef / key / time signature area before the first beat belongs to the first bar
        var measure = position.MeasureIndex;
        var localX = Math.Clamp(x - position.X, 0, Math.Max(0, position.Width - 0.001));
        var slot = layoutEngine.WarpFor(track, measure).SlotAt(localX / Math.Max(1e-6, position.Width));
        var slots = slotsFor(measure);
        var gridCell = Math.Clamp((int)slot, 0, slots - 1);
        var cells = cellsFor(track.Measures[measure]);
        var cell = double.IsFinite(slot) ? CursorPositions.Resolve(cells, slot, slots) : gridCell;
        var stringIndex = Math.Clamp((int)Math.Round((y - tabTop(system)) / stringGap), 0, Math.Max(0, track.StringTunings.Count - 1));
        return (measure, cell, stringIndex);
    }
}
