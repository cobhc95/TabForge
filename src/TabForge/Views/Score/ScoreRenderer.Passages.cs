using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

// ScoreRenderer: the fade and palm-mute passages that span bars.
// Owns: drawing of those two passage kinds. Does not own: passage detection (ScorePassages) or the bar drawing. Tests: render identity, TestTabEditorRenderInvariance.

internal sealed partial class ScoreRenderer
{
    private void DrawFadePassages(DrawingContext dc, IReadOnlyList<FadePassage> passages,
        TrackModel track, ScoreSystemPosition systemLayout, double staffTop, double tabTop, Color ink,
        bool showTab)
    {
        if (passages.Count == 0 || systemLayout.Measures.Count == 0) return;
        var layout = _layout.GetLayout(track);
        var phraseInk = StaffNotationRenderer.EngravingInkColor(ink,
            _host.Appearance.DarkPaper ? _host.Appearance.DarkPaperColor : _host.Appearance.LightPaperColor);
        var pen = RenderDraw.Pen(phraseInk, 0.9);
        const double halfOpening = 3.5;
        var baseLineY = showTab ? tabTop - 8 : staffTop + 4 * _host.StaffGap + 20;
        foreach (var passage in passages)
        {
            var firstSystem = layout.SystemForMeasure(passage.FirstMeasure);
            var lastSystem = layout.SystemForMeasure(passage.LastMeasure);
            var system = systemLayout.Index;
            if (system < firstSystem || system > lastSystem) continue;

            var continuesFromPreviousSystem = system > firstSystem;
            var continuesIntoNextSystem = system < lastSystem;
            var startX = continuesFromPreviousSystem
                ? systemLayout.X + 2
                : CellCenterX(passage.FirstMeasure, passage.FirstStartSlots);
            var endX = continuesIntoNextSystem
                ? systemLayout.X + systemLayout.Width - 2
                : CellBoundaryX(passage.LastMeasure, passage.LastEndSlots);
            if (endX <= startX + 1) continue;

            var lineY = baseLineY;
            if (!showTab)
                foreach (var mp in systemLayout.Measures)   // notation only: the wedge goes under the dynamics row of the bars it spans
                    if (mp.MeasureIndex >= passage.FirstMeasure && mp.MeasureIndex <= passage.LastMeasure && mp.MeasureIndex < track.Measures.Count && BarHasDynamic(track.Measures[mp.MeasureIndex]))
                        lineY = Math.Max(lineY, StaffDynamicTop(mp.MeasureIndex, staffTop + 4 * _host.StaffGap) + DynamicHeight + halfOpening + 6);
            if (!showTab)
                foreach (var mp in systemLayout.Measures)   // ...and under a harmonic caption (F.B., A.H.) printed below the staff
                    if (mp.MeasureIndex >= passage.FirstMeasure && mp.MeasureIndex <= passage.LastMeasure && mp.MeasureIndex < track.Measures.Count
                        && track.Measures[mp.MeasureIndex].Cells.Any(c => c.Notes.Any(n => ScoreMarkText.HarmonicCaption(n.Techniques).Length > 0)))
                    { lineY = Math.Max(lineY, baseLineY + 14) + 8; break; }

            var startHalf = continuesFromPreviousSystem || passage.IsFadeOut ? halfOpening : 0;
            var endHalf = continuesIntoNextSystem || !passage.IsFadeOut ? halfOpening : 0;
            dc.DrawLine(pen, new Point(startX, lineY - startHalf), new Point(endX, lineY - endHalf));
            dc.DrawLine(pen, new Point(startX, lineY + startHalf), new Point(endX, lineY + endHalf));
        }
    }

    private void DrawPalmMutePassages(DrawingContext dc, IReadOnlyList<PalmMutePassage> passages,
        TrackModel track, ScoreSystemPosition systemLayout, double tabTop, Color ink, double? belowStaffBottom = null)
    {
        var system = systemLayout.Index;
        if (systemLayout.Measures.Count == 0) return;

        var phraseInk = StaffNotationRenderer.EngravingInkColor(ink,
            _host.Appearance.DarkPaper ? _host.Appearance.DarkPaperColor : _host.Appearance.LightPaperColor);
        var phraseBrush = ScoreText.Brush(phraseInk);
        const double terminalTickLength = 5.0;
        var pen = RenderDraw.DashedPen(phraseInk, 1.0, 5.0, 2.5);
        var endPen = RenderDraw.Pen(phraseInk, 1.0);
        // The reference puts "P.M. - - |" under the notation staff; without a staff it stays above the TAB.
        var lineY = belowStaffBottom is { } bottom ? bottom + 32 : tabTop - 12;
        var baseLineY = lineY;
        var labelY = lineY - 10;
        foreach (var passage in passages)
        {
            var layout = _layout.GetLayout(track);
            var passageFirstSystem = layout.SystemForMeasure(passage.FirstMeasure);
            var passageLastSystem = layout.SystemForMeasure(passage.LastMeasure);
            if (system < passageFirstSystem || system > passageLastSystem) continue;

            var continuesFromPreviousSystem = system > passageFirstSystem;
            var continuesIntoNextSystem = system < passageLastSystem;
            var startX = continuesFromPreviousSystem
                ? systemLayout.X + 2
                : CellCenterX(passage.FirstMeasure, passage.FirstStartSlots);
            var endX = continuesIntoNextSystem
                ? systemLayout.X + systemLayout.Width - 2
                : CellBoundaryX(passage.LastMeasure, passage.LastEndSlots);

            lineY = baseLineY;
            labelY = lineY - 10;
            if (belowStaffBottom is not null && _layout.HasStaffLayouts)
            {
                // Keep the dashed line under the lowest notehead of the bars it spans (ledger-line notes).
                var lowest = double.NegativeInfinity;
                foreach (var mp in systemLayout.Measures)
                {
                    if (mp.MeasureIndex < passage.FirstMeasure || mp.MeasureIndex > passage.LastMeasure || mp.MeasureIndex >= _layout.StaffLayoutBars) continue;
                    if (BarHasDynamic(track.Measures[mp.MeasureIndex]))
                        lowest = Math.Max(lowest, StaffDynamicTop(mp.MeasureIndex, belowStaffBottom.Value) + DynamicHeight);   // the P.M. lane clears the dynamics row
                    for (var v = 0; v < 2; v++)
                        if (_layout.CachedStaffLayout(mp.MeasureIndex, v) is { } cachedLayout)
                            foreach (var b in cachedLayout.Beats)
                            {
                                foreach (var n in b.Notes) lowest = Math.Max(lowest, n.Y + (n.Source.Ghost ? 9 : 0));   // a ghost note's brackets reach below its head
                                if (b.HasStem) lowest = Math.Max(lowest, Math.Max(b.StemStartY, b.StemEndY) + 3);
                                if (b.LowerStemTopY is not null) lowest = Math.Max(lowest, b.LowerStemEndY + 3);
                            }
                }
                if (lowest > double.NegativeInfinity && lowest + 11 > lineY) { lineY = lowest + 11; labelY = lineY - 10; }
            }

            if (!continuesFromPreviousSystem)
            {
                var labelWidth = ScoreText.MakeTextIn(ScoreTextArea.Technique, "P.M.", 9, phraseBrush).Width;
                ScoreText.DrawCenteredIn(ScoreTextArea.Technique, dc, "P.M.", startX - labelWidth / 2 - 2, labelY, 9, phraseBrush);
            }

            var lineStart = continuesFromPreviousSystem ? startX : startX + 2;
            if (endX > lineStart + 1)
                dc.DrawLine(pen, new Point(lineStart, lineY), new Point(endX, lineY));
            // Keep the closing hook tied to the actual final muted-note boundary even for a short
            // passage whose dashed extender is only a few pixels long.
            if (!continuesIntoNextSystem && endX > startX)
                dc.DrawLine(endPen, new Point(endX, lineY - terminalTickLength),
                    new Point(endX, lineY + terminalTickLength));
        }
    }
}
