using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the dynamics marks (p, mf, f ...) of a bar on the staff or under the TAB and the rows they stack in.
// Does not own: the other bar furniture (ScoreRenderer.Bars.cs) or the marks over the tab (ScoreRenderer.Marks.cs).
// Tests: no dedicated test class.

internal sealed partial class ScoreRenderer
{
    private const double DynamicHeight = 15;

    private bool BarHasDynamic(MeasureModel measure)
    {
        if (_layout.DynamicMarks.Count == 0) return false;
        foreach (var cell in measure.Cells)
            if (_layout.DynamicMarks.ContainsKey(cell)) return true;
        return false;
    }

    /// <summary>
    /// Top of the dynamics row under the staff of one bar: 8 px under the staff, lower when a note, ledger line or
    /// stem of the bar reaches further down (the same extents the palm-mute lane clears).
    /// </summary>
    private double StaffDynamicTop(int measureIndex, double staffBottom)
    {
        var lowest = staffBottom;
        if (measureIndex >= 0 && measureIndex < _layout.StaffLayoutBars)
            for (var v = 0; v < 2; v++)
                if (_layout.CachedStaffLayout(measureIndex, v) is { } cached)
                    foreach (var b in cached.Beats)
                    {
                        foreach (var n in b.Notes) lowest = Math.Max(lowest, n.Y + 4);
                        if (b.IsDrum) lowest = Math.Max(lowest, b.MaxY + 4);
                        if (b.HasStem) lowest = Math.Max(lowest, Math.Max(b.StemStartY, b.StemEndY) + 3);
                        if (b.LowerStemTopY is not null) lowest = Math.Max(lowest, b.LowerStemEndY + 3);
                    }
        return Math.Max(staffBottom + 8, lowest + 5);
    }

    /// <summary>Tab-only: the row sits under the TAB, below the fingering marks and the lyric lines of the bar.</summary>
    private double TabDynamicTop(MeasureModel measure, double tabBottom)
    {
        var extra = 0.0;
        void Scan(IReadOnlyList<TabCell> cells)
        {
            foreach (var cell in cells)
            {
                var lyricRows = string.IsNullOrWhiteSpace(cell.Lyrics) ? 0 : Math.Min(cell.Lyrics.Split('\n').Length, 3);
                extra = Math.Max(extra, ScoreMarkText.FingeringHeight(cell) + lyricRows * 12);
            }
        }
        Scan(measure.Cells);
        if (ScoreLayoutEngine.Voice2HasContent(measure)) Scan(measure.Voice2Cells);
        return tabBottom + 13 + extra + 3;
    }

    /// <summary>
    /// Dynamics markings (ppp..fff) engraved as bold italic letters under the staff (under the TAB when the
    /// notation staff is hidden). A marking appears only where the dynamic changes. The marks come from the layout
    /// (computed once per score generation) and take part in the bar's width and the palm-mute lane stacking.
    /// </summary>
    private void DrawDynamics(IDrawContext d, TrackModel track, MeasureModel measure, int measureIndex,
        StaffNotationMeasureLayout layout, double staffTop, double tabTop, int strings, UiColor ink, bool showStaff)
    {
        if (_layout.DynamicMarks.Count == 0) return;
        double? top = null;
        foreach (var beat in layout.Beats)
        {
            if (!_layout.DynamicMarks.TryGetValue(beat.Cell, out var name)) continue;
            var text = ScoreText.DynamicText(d, name, ink);
            if (showStaff)
            {
                // Stacked under the staff's own ink (heads, ledger notes, stems of this column), then claimed for the marks below it.
                var y = layout.Skyline.PlaceBelow(beat.CenterX - text.Width / 2, beat.CenterX + text.Width / 2, DynamicHeight, staffTop + 4 * _host.StaffGap + 8);
                ScoreText.DrawFormatted(d, text, beat.CenterX - text.Width / 2, y);
                continue;
            }
            var tabBottom = tabTop + (strings - 1) * _host.StringGap;
            top ??= TabDynamicTop(measure, tabBottom);
            var dynY = top.Value;
            // Fingering and lyric rows can push the row past the system's own height (it would print over the next system): stack it above the TAB instead.
            if (dynY + DynamicHeight > tabBottom + 28.0 * _host.Appearance.ScoreSpacing + 2)
                dynY = _sky.PlaceAbove(beat.CenterX - text.Width / 2, beat.CenterX + text.Width / 2, DynamicHeight, tabTop - 13);
            ScoreText.DrawFormatted(d, text, beat.CenterX - text.Width / 2, dynY);
        }
    }
}
