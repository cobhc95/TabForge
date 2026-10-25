using System.Globalization;
using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the per-system drawing: DrawSystem and the measure drawing of the tab lane and of the staff through StaffNotationRenderer.
// Does not own: the bar furniture (ScoreRenderer.Bars.cs), marks (ScoreRenderer.Marks.cs), passages (ScoreRenderer.Passages.cs), the cursor (ScoreRenderer.Cursor.cs), tab links (ScoreRenderer.TabLinks.cs), the page header (ScoreRenderer.Header.cs) or staff engraving (StaffNotationRenderer).
// Tests: no dedicated test class.
// Length: over 300 lines because the system and measure drawing share one set of per-measure geometry and colours across the partial files.

/// <summary>
/// Draws one system of a score: the staff and tab frames, every bar's notation and marks, the passages that span bars, and the cursor,
/// selection and playback highlights. It reads the editor through <see cref="IScoreRenderHost"/> and keeps only the scratch state of the
/// system being drawn.
/// </summary>
internal sealed partial class ScoreRenderer
{
    /// <summary>How much of the hover outline's strength the hovered cell's fill uses (0: only the grey outline box, as the original app draws it).</summary>
    private const double HoverFillShare = 0;

    private readonly IScoreRenderHost _host;
    private readonly ScoreLayoutEngine _layout;
    private readonly StaffNotationRenderer _staff;
    private readonly HashSet<(int bar, int cell, int s)> _noNotes = new();   // what glows when the song is not playing: nothing

    private readonly IColourText _colourText;   // marker colours, hex or named; the app parses them

    internal ScoreRenderer(IScoreRenderHost host, ScoreLayoutEngine layout, StaffNotationRenderer staff, IColourText colourText)
    {
        _host = host;
        _layout = layout;
        _staff = staff;
        _colourText = colourText;
    }

    /// <summary>The colour a bar's marker label is drawn in: the marker's own colour when it parses, else <paramref name="accent"/>.</summary>
    internal UiColor MarkerColour(MarkerModel? marker, UiColor accent) => (marker is null ? null : _colourText.Parse(marker.ColorHex)) ?? accent;

    // ---- what the drawing reads from the editor ----

    internal void DrawSystem(IDrawContext d, TrackModel track, ScoreSystemPosition systemLayout, UiColor ink, UiColor faint, UiColor line, UiColor accent, UiColor cursorColor, UiColor playColor, UiColor errorColor,
        IReadOnlyList<PalmMutePassage> palmMutePassages, IReadOnlyList<FadePassage> fadePassages)
    {
        var system = systemLayout.Index;
        _errorColor = errorColor;
        var staffTop = _host.StaffTop(system);
        var tabTop = _host.TabTop(system);
        var strings = Math.Max(1, track.StringTunings.Count);
        var showStaff = _host.Notation != NotationMode.TabOnly;
        var showTab = _host.Notation != NotationMode.StaffOnly;
        var thin = StaffNotationDrawing.StaffLineStroke(line);   // staff lines and ledger lines share this pen
        var thick = new UiStroke(ink, 1.4);
        var systemRight = systemLayout.X + systemLayout.Width;

        DrawSystemLinesAndClef(d, track, systemLayout, staffTop, tabTop, strings, showStaff, showTab, thin, ink, faint);
        DrawBarlines(d, track, systemLayout, staffTop, tabTop, strings, showStaff, showTab, thick, errorColor);
        DrawFinalBarline(d, track, systemLayout, systemRight, staffTop, tabTop, strings, showStaff, showTab, ink, thick);

        var voltas = new List<VoltaSpan>();
        _textSpill.Clear();
        foreach (var measurePosition in systemLayout.Measures)
        {
            if (!_host.InHorizontalBand(measurePosition)) continue;
            DrawSystemMeasure(d, track, systemLayout, measurePosition, staffTop, tabTop, strings, showStaff, showTab, ink, faint, line, accent, playColor, voltas);
        }
        DrawVoltaBrackets(d, voltas, ink);

        if (showTab || showStaff) DrawPalmMutePassages(d, palmMutePassages, track, systemLayout, tabTop, ink, showTab ? null : staffTop + 4 * _host.StaffGap);
        DrawFadePassages(d, fadePassages, track, systemLayout, staffTop, tabTop, ink, showTab);
    }

    /// <summary>The staff and string lines across the system, the clef, and the T-A-B letters (or the drum line names) in front of the TAB.</summary>
    private void DrawSystemLinesAndClef(IDrawContext d, TrackModel track, ScoreSystemPosition systemLayout, double staffTop, double tabTop, int strings, bool showStaff, bool showTab, UiStroke thin, UiColor ink, UiColor faint)
    {
        var systemRight = systemLayout.X + systemLayout.Width;
        if (showStaff) for (var l = 0; l < 5; l++) d.Line(thin, systemLayout.X, staffTop + l * _host.StaffGap, systemRight, staffTop + l * _host.StaffGap);
        if (showTab) for (var s = 0; s < strings; s++) d.Line(thin, systemLayout.X, tabTop + s * _host.StringGap, systemRight, tabTop + s * _host.StringGap);

        if (showStaff) DrawClef(d, ScoreClefKey.SystemClef(track, systemLayout.Measures.Count > 0 ? track.Measures[systemLayout.Measures[0].MeasureIndex].Clef : null), track.Kind == TrackKind.Drums ? systemLayout.X + (systemLayout.Measures.Count > 0 && track.Measures[systemLayout.Measures[0].MeasureIndex].RepeatStart ? 17 : 7) : _host.GridLeft - 26, staffTop, 22, ink);   // the narrow percussion clef sits inside the staff, between the opening barline (or its repeat dots) and the signatures of the bar
        if (!showTab) return;
        if (track.Kind == TrackKind.Drums && DrumMaps.LineNames(track.DrumMapPreset) is { } lineNames)
        {
            // Drum-tab preset: name each line (CC, HH, SD, T1, T2, FT, BD) instead of T-A-B.
            for (var li = 0; li < lineNames.Length; li++)
                ScoreText.Draw(d, lineNames[li], _host.GridLeft - 24, tabTop + li * _host.StringGap - 6, 9, faint, 700);
        }
        else
        {
            ScoreText.Draw(d, "T", _host.GridLeft - 22, tabTop + 1, 13, faint, 700);
            ScoreText.Draw(d, "A", _host.GridLeft - 22, tabTop + 17, 13, faint, 700);
            ScoreText.Draw(d, "B", _host.GridLeft - 22, tabTop + 33, 13, faint, 700);
        }
    }

    /// <summary>The red staff and string lines of marked bars, the barline at the left of every bar and the double bar line.</summary>
    private void DrawBarlines(IDrawContext d, TrackModel track, ScoreSystemPosition systemLayout, double staffTop, double tabTop, int strings, bool showStaff, bool showTab, UiStroke thick, UiColor errorColor)
    {
        foreach (var measurePosition in systemLayout.Measures)
        {
            if (!_host.InHorizontalBand(measurePosition)) continue;
            var measureIndex = measurePosition.MeasureIndex;
            // The reference marks an incomplete or overfull bar by drawing its staff and string lines red (the bar number is red too, see DrawBarLabels).
            if (!EditingBar(track, measureIndex) && _layout.BarStateFor(track, measureIndex).Marked)
            {
                var red = StaffNotationDrawing.StaffLineStroke(errorColor);
                var (left, right) = (measurePosition.X, measurePosition.X + measurePosition.Width);
                if (showStaff) for (var l = 0; l < 5; l++) d.Line(red, left, staffTop + l * _host.StaffGap, right, staffTop + l * _host.StaffGap);
                if (showTab) for (var s = 0; s < strings; s++) d.Line(red, left, tabTop + s * _host.StringGap, right, tabTop + s * _host.StringGap);
            }

            // Snap vertical bar lines to the pixel grid so they render as crisp 1 px lines.
            var barX = Math.Round(measurePosition.X) + 0.5;
            if (showStaff) d.Line(thick, barX, staffTop - 4, barX, staffTop + 4 * _host.StaffGap + 4);
            if (showTab) d.Line(thick, barX, tabTop - 4, barX, tabTop + (strings - 1) * _host.StringGap + 4);
            if (track.Measures[measureIndex].IsDoubleBar)
            {
                var doubleX = Math.Round(measurePosition.X + measurePosition.Width) + 3.5;
                if (showStaff) d.Line(thick, doubleX, staffTop - 4, doubleX, staffTop + 4 * _host.StaffGap + 4);
                if (showTab) d.Line(thick, doubleX, tabTop - 4, doubleX, tabTop + (strings - 1) * _host.StringGap + 4);
            }
        }
    }

    /// <summary>The line that ends the system: thin + thick at the end of the song, one line elsewhere.</summary>
    private void DrawFinalBarline(IDrawContext d, TrackModel track, ScoreSystemPosition systemLayout, double systemRight, double staffTop, double tabTop, int strings, bool showStaff, bool showTab, UiColor ink, UiStroke thick)
    {
        var finalBarX = Math.Round(systemRight) + 0.5;
        // The song last bar ends on a thin + thick double line; other systems end on one line.
        var endsSong = systemLayout.Measures.Count > 0 && systemLayout.LastMeasure == track.Measures.Count - 1;
        var finalPen = endsSong ? new UiStroke(ink, 3.0) : thick;
        var finalThin = new UiStroke(ink, 1.0);
        if (showStaff)
        {
            d.Line(finalPen, finalBarX, staffTop - 4, finalBarX, staffTop + 4 * _host.StaffGap + 4);
            if (endsSong) d.Line(finalThin, finalBarX - 5, staffTop - 4, finalBarX - 5, staffTop + 4 * _host.StaffGap + 4);
        }
        if (showTab)
        {
            d.Line(finalPen, finalBarX, tabTop - 4, finalBarX, tabTop + (strings - 1) * _host.StringGap + 4);
            if (endsSong) d.Line(finalThin, finalBarX - 5, tabTop - 4, finalBarX - 5, tabTop + (strings - 1) * _host.StringGap + 4);
        }
    }

    /// <summary>One bar of a system: its marks, repeat signs, overlays (playing band, loop, selection, playback, hover), cursor, both voices and bar labels.</summary>
    private void DrawSystemMeasure(IDrawContext d, TrackModel track, ScoreSystemPosition systemLayout, ScoreMeasurePosition measurePosition, double staffTop, double tabTop, int strings, bool showStaff, bool showTab,
        UiColor ink, UiColor faint, UiColor line, UiColor accent, UiColor playColor, List<VoltaSpan> voltas)
    {
        var measureIndex = measurePosition.MeasureIndex;
        var measure = track.Measures[measureIndex];
        var editingCells = CellsFor(measure);
        var x = measurePosition.X;
        var measureWidth = measurePosition.Width;
        var slots = _host.SlotsFor(measureIndex);
        var slotWidth = measureWidth / Math.Max(1, slots);
        var warp = _layout.WarpFor(track, measureIndex);

        // Titles, endings and beat text ride above the tallest stem of the bar (drum chords have long stems).
        PrepareMarkSkyline(track, measure, measureIndex, x, staffTop, slotWidth, slots, showStaff, tabTop);
        _barRight = x + measureWidth;
        foreach (var spill in _textSpill) _sky.Claim(spill);   // a long text of the previous bar runs on into this one: bar number, tempo and title stack above it
        DrawBarStaffAnnotations(d, track, measure, measureIndex, x, measureWidth, staffTop, ink, tabTop + 2.5 * _host.StringGap);
        DrawRepeatSigns(d, measure, x, measureWidth, staffTop, tabTop, strings, showStaff, showTab, ink);

        // Opt-in playing-bar band: behind everything that follows (selection, notes, marks).
        if (_host.PlayingBarBand(track, systemLayout, measurePosition) is { } playingBar)
            d.Rect(playingBar.Colour, null, playingBar.Rect.X, playingBar.Rect.Y, playingBar.Rect.Width, playingBar.Rect.Height);

        DrawRangeOverlays(d, measureIndex, editingCells, x, measureWidth, warp, slots, staffTop, tabTop, strings, showTab);
        DrawPlaybackBeat(d, measure, measureIndex, x, measureWidth, warp, slots, staffTop, tabTop, strings, showTab);
        DrawHoverOutline(d, measure, measureIndex, editingCells, x, measureWidth, warp, staffTop, tabTop, strings, showTab);

        // Cursor: a solid yellow box on the TAB lane (hidden while the song plays; the staff box is drawn in DrawMeasure)
        if (measureIndex == _host.SelectedMeasure && showTab && ScoreCursorBox.Shown(_host) && !_host.HasSelection)
            { var box = CursorRect(measure, editingCells, _host.SelectedCell, x, warp, measureWidth, tabTop + _host.SelectedString * _host.StringGap - 8, 16); ScoreCursorBox.DrawTab(d, _host, box); }

        DrawMeasure(d, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, slotWidth, slots, strings, ink, faint, line, accent, playColor, showStaff, showTab,
            measure.Cells, inactiveVoice: _host.Project?.GrayInactiveVoice == true && _host.ActiveVoiceIndex != 0);
        if (ScoreLayoutEngine.Voice2HasContent(measure))
            DrawMeasure(d, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, slotWidth, slots, strings, ink, faint, line, accent, playColor, showStaff, showTab,
                measure.Voice2Cells, inactiveVoice: _host.Project?.GrayInactiveVoice == true && _host.ActiveVoiceIndex != 1);
        // Bar-level texts stack last, outside everything the notes and their marks claimed.
        DrawBarLabels(d, track, measure, measureIndex, x, measureWidth, staffTop, ink, faint, accent, voltas);
    }

    /// <summary>
    /// Standard repeat barlines on both staves: thick line, thin line and two dots (start ||:,
    /// end :||), with the play count above the end repeat.
    /// </summary>
    private void DrawRepeatSigns(IDrawContext d, MeasureModel measure, double x, double measureWidth, double staffTop, double tabTop, int strings, bool showStaff, bool showTab, UiColor ink)
    {
        if (!(measure.RepeatStart || measure.RepeatEnd)) return;
        var inkBrush = ink;
        var heavy = new UiStroke(ink, 3.2);
        var light = new UiStroke(ink, 1);
        void RepeatSign(double top, double bottom, bool start)
        {
            var edge = start ? Math.Round(x) + 1.6 : Math.Round(x + measureWidth) - 1.6;
            var thin = start ? edge + 4.5 : edge - 4.5;
            var dots = start ? thin + 4.5 : thin - 4.5;
            d.Line(heavy, edge, top, edge, bottom);
            d.Line(light, thin, top, thin, bottom);
            var mid = (top + bottom) / 2; var gap = Math.Max(4, (bottom - top) / 6);
            d.Ellipse(ink, null, dots, mid - gap, 1.9, 1.9);
            d.Ellipse(ink, null, dots, mid + gap, 1.9, 1.9);
        }
        foreach (var start in new[] { true, false })
        {
            if (start ? !measure.RepeatStart : !measure.RepeatEnd) continue;
            if (showStaff) RepeatSign(staffTop, staffTop + 4 * _host.StaffGap, start);
            if (showTab) RepeatSign(tabTop, tabTop + (strings - 1) * _host.StringGap, start);
        }
        if (measure.RepeatEnd && measure.RepeatCount > 2)
            ScoreText.DrawIn(ScoreTextArea.BarInfo, d, $"x{measure.RepeatCount}", x + measureWidth - 22, (showStaff ? staffTop : tabTop) - 16, 10, inkBrush, 700);
    }

    private static double SlotX(MeasureWarp warp, double x, double measureWidth, double s) => x + warp.Fraction(s) * measureWidth;

    /// <summary>The loop tint (a light wash, no selection look), the mirrored bars and the selection range overlay on top.</summary>
    private void DrawRangeOverlays(IDrawContext d, int measureIndex, IReadOnlyList<TabCell> editingCells, double x, double measureWidth, MeasureWarp warp, int slots, double staffTop, double tabTop, int strings, bool showTab)
    {
        void RangeBand((int m1, int c1, int m2, int c2) range, UiColor fill, UiStroke? outline)
        {
            var (m1, c1, m2, c2) = range;
            if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
            if (measureIndex < m1 || measureIndex > m2 || editingCells.Count == 0) return;
            var from = measureIndex == m1 ? c1 : 0;
            var to = measureIndex == m2 ? c2 : CursorPositions.LastCell(editingCells, slots);
            var (startSlots, endSlots) = ScoreSelectionGeometry.SpanSlots(editingCells, from, to);
            var sx = SlotX(warp, x, measureWidth, startSlots);
            var sw = Math.Max(2, SlotX(warp, x, measureWidth, endSlots) - sx);
            d.Rect(fill, outline, sx, staffTop - 8, sw, (showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap) - staffTop + 22);
        }
        if (_host.LoopTint is { } tint) RangeBand(tint, _host.Appearance.LoopTintColor, null);
        if (_host.MirrorBars is { } mirror)
        {
            foreach (var (first, last) in mirror)
            {
                if (measureIndex < first || measureIndex > last) continue;
                var shade = _host.Appearance.SelectionColor;
                RangeBand((measureIndex, 0, measureIndex, CursorPositions.LastCell(editingCells, slots)), SelectionWash(shade), new UiStroke(shade, 1));
                break;
            }
        }
        if (_host.HasSelection)
        {
            var selected = _host.Appearance.SelectionColor;
            RangeBand(_host.SelectionRange(), SelectionWash(selected), new UiStroke(selected, 1));
        }
    }

    /// <summary>
    /// Playback: retain the configurable beat tint, then draw the remaining duration of the
    /// active note ahead of the independent, lightweight caret overlay.
    /// </summary>
    private void DrawPlaybackBeat(IDrawContext d, MeasureModel measure, int measureIndex, double x, double measureWidth, MeasureWarp warp, int slots, double staffTop, double tabTop, int strings, bool showTab)
    {
        if (!(measureIndex == _host.PlaybackMeasure && _host.PlaybackCell >= 0 && measure.Cells.Count > 0)) return;
        var beatCell = measure.Cells[Math.Clamp(_host.PlaybackCell, 0, measure.Cells.Count - 1)];
        var beatStart = CellStartSlots(measure, _host.PlaybackCell);
        var beatEnd = Math.Min(slots, beatStart + MusicTime.CellSlots(beatCell));
        var px = SlotX(warp, x, measureWidth, beatStart);
        var beatWidth = Math.Max(0, SlotX(warp, x, measureWidth, beatEnd) - px);
        var bandBottom = showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap;
        var bandRect = new UiRect(px, staffTop - 8, beatWidth, bandBottom - staffTop + 22);
        if (_host.Appearance.HighlightPlayedBeat && beatWidth > 0 && _host.Appearance.DurationGlowOpacity > 0)
        {
            // The elapsed beat tint stops at the caret; the overlay shades only the
            // remaining duration. This prevents the two independently rendered layers
            // from stacking opacity over the same part of the beat.
            var playedSlots = Math.Clamp(_host.PlaybackFraction * slots, beatStart, beatEnd);
            var playedWidth = Math.Max(0, SlotX(warp, x, measureWidth, playedSlots) - px);
            var tintAlpha = PlaybackGlowIntensity.ScaleAlpha(_host.Appearance.HighlightBackground.A, _host.Appearance.DurationGlowOpacity);
            if (playedWidth > 0 && tintAlpha > 0)
            {
                d.Rect(_host.Appearance.HighlightBackground with { A = tintAlpha }, null, px, bandRect.Y, Math.Min(beatWidth, playedWidth), bandRect.Height);
            }
        }
    }

    /// <summary>
    /// Hover: a faint outline shows which beat a click would act on, without looking like the
    /// edit cursor, the selection or the playhead.
    /// Shown playing or stopped; hidden while a range is selected or dragged out. The fill and outline share the hover colour.
    /// </summary>
    private void DrawHoverOutline(IDrawContext d, MeasureModel measure, int measureIndex, IReadOnlyList<TabCell> editingCells, double x, double measureWidth, MeasureWarp warp, double staffTop, double tabTop, int strings, bool showTab)
    {
        if (!(measureIndex == _host.HoverMeasure && _host.HoverCell >= 0 && _host.HoverCell < editingCells.Count && _host.Appearance.ShowNoteOutline
            && !_host.HasSelection)) return;
        var hoverRect = HoverRect(measure, editingCells, _host.HoverCell, x, warp, measureWidth,
            staffTop - 4, (showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap) - staffTop + 12);
        var hoverAlpha = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(_host.Appearance.HoverHighlightIntensity, 0, 1)), 0, 255);
        var hoverColour = _host.Appearance.HoverColor;
        // The fill is a light wash (a share of the outline strength) so the cell outline reads as the grey box.
        var fillAlpha = (byte)Math.Round(hoverAlpha * HoverFillShare);
        d.Rect(hoverColour with { A = fillAlpha }, new UiStroke(hoverColour with { A = hoverAlpha }, 1), hoverRect.X, hoverRect.Y, hoverRect.Width, hoverRect.Height);
    }

    /// <summary>The selection colour at the user's highlight intensity (its fill; the outline keeps the solid colour).</summary>
    private UiColor SelectionWash(UiColor colour) =>
        colour with { A = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(_host.Appearance.SelectionHighlightIntensity, 0, 1)), 0, 255) };

    // ---- row stacking: one skyline per bar, shared by both voices; every mark claims its box and the next is placed outside it ----
    private readonly MarkSkyline _sky = new();

    private readonly List<UiRect> _textSpill = new();   // the parts of stacked texts that run past their bar's right edge, claimed again in the next bar

    private double _barRight;
    private UiColor _errorColor;   // this system's red for marked bars (theme-dependent)

    private List<TabCell> CellsFor(MeasureModel measure) => measure.CellsForVoice(_host.ActiveVoiceIndex);}
