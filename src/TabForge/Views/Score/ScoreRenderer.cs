using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

/// <summary>
/// Draws one system of a score: the staff and tab frames, every bar's notation and marks, the passages that span bars, and the cursor,
/// selection and playback highlights. It reads the editor through <see cref="IScoreRenderHost"/> and keeps only the scratch state of the
/// system being drawn.
/// </summary>
internal sealed partial class ScoreRenderer
{
    private readonly IScoreRenderHost _host;
    private readonly ScoreLayoutEngine _layout;
    private readonly StaffNotationRenderer _staff;

    internal ScoreRenderer(IScoreRenderHost host, ScoreLayoutEngine layout, StaffNotationRenderer staff)
    {
        _host = host;
        _layout = layout;
        _staff = staff;
    }

    // ---- what the drawing reads from the editor ----

    internal void DrawSystem(DrawingContext dc, TrackModel track, ScoreSystemPosition systemLayout, Color ink, Color faint, Color line, Color accent, Color cursorColor, Color playColor, Color errorColor,
        IReadOnlyList<PalmMutePassage> palmMutePassages, IReadOnlyList<FadePassage> fadePassages)
    {
        var system = systemLayout.Index;
        var staffTop = _host.StaffTop(system);
        var tabTop = _host.TabTop(system);
        var strings = Math.Max(1, track.StringTunings.Count);
        var showStaff = _host.Notation != NotationMode.TabOnly;
        var showTab = _host.Notation != NotationMode.StaffOnly;
        var thin = StaffNotationRenderer.StaffLinePen(line);   // staff lines and ledger lines share this pen
        var thick = RenderDraw.Pen(ink, 1.4);
        var systemRight = systemLayout.X + systemLayout.Width;

        if (showStaff) for (var l = 0; l < 5; l++) dc.DrawLine(thin, new Point(systemLayout.X, staffTop + l * _host.StaffGap), new Point(systemRight, staffTop + l * _host.StaffGap));
        if (showTab) for (var s = 0; s < strings; s++) dc.DrawLine(thin, new Point(systemLayout.X, tabTop + s * _host.StringGap), new Point(systemRight, tabTop + s * _host.StringGap));

        if (showStaff) DrawClef(dc, systemLayout.Measures.Count > 0 ? track.Measures[systemLayout.Measures[0].MeasureIndex].Clef : null, _host.GridLeft - 26, staffTop, 22, ink);
        if (showTab)
        {
            if (track.Kind == TrackKind.Drums && DrumMaps.LineNames(track.DrumMapPreset) is { } lineNames)
            {
                // Drum-tab preset: name each line (CC, HH, SD, T1, T2, FT, BD) instead of T-A-B.
                for (var li = 0; li < lineNames.Length; li++)
                    ScoreText.Draw(dc, lineNames[li], _host.GridLeft - 24, tabTop + li * _host.StringGap - 6, 9, ScoreText.Brush(faint), FontWeights.Bold);
            }
            else
            {
                ScoreText.Draw(dc, "T", _host.GridLeft - 22, tabTop + 1, 13, ScoreText.Brush(faint), FontWeights.Bold);
                ScoreText.Draw(dc, "A", _host.GridLeft - 22, tabTop + 17, 13, ScoreText.Brush(faint), FontWeights.Bold);
                ScoreText.Draw(dc, "B", _host.GridLeft - 22, tabTop + 33, 13, ScoreText.Brush(faint), FontWeights.Bold);
            }
        }

        foreach (var measurePosition in systemLayout.Measures)
        {
            if (!_host.InHorizontalBand(measurePosition)) continue;
            var measureIndex = measurePosition.MeasureIndex;
            var state = _layout.BarStateFor(measureIndex);
            var barPen = state.Marked ? RenderDraw.Pen(errorColor, 1.6) : thick;

            // Snap vertical bar lines to the pixel grid so they render as crisp 1 px lines.
            var barX = Math.Round(measurePosition.X) + 0.5;
            if (showStaff) dc.DrawLine(barPen, new Point(barX, staffTop - 4), new Point(barX, staffTop + 4 * _host.StaffGap + 4));
            if (showTab) dc.DrawLine(barPen, new Point(barX, tabTop - 4), new Point(barX, tabTop + (strings - 1) * _host.StringGap + 4));
            if (track.Measures[measureIndex].IsDoubleBar)
            {
                var doubleX = Math.Round(measurePosition.X + measurePosition.Width) + 3.5;
                if (showStaff) dc.DrawLine(thick, new Point(doubleX, staffTop - 4), new Point(doubleX, staffTop + 4 * _host.StaffGap + 4));
                if (showTab) dc.DrawLine(thick, new Point(doubleX, tabTop - 4), new Point(doubleX, tabTop + (strings - 1) * _host.StringGap + 4));
            }
        }
        var finalBarX = Math.Round(systemRight) + 0.5;
        // The song's last bar ends on a thin + thick double line; other systems end on one line.
        var endsSong = systemLayout.Measures.Count > 0 && systemLayout.LastMeasure == track.Measures.Count - 1;
        var finalPen = endsSong ? RenderDraw.Pen(ink, 3.0) : thick;
        var finalThin = RenderDraw.Pen(ink, 1.0);
        if (showStaff)
        {
            dc.DrawLine(finalPen, new Point(finalBarX, staffTop - 4), new Point(finalBarX, staffTop + 4 * _host.StaffGap + 4));
            if (endsSong) dc.DrawLine(finalThin, new Point(finalBarX - 5, staffTop - 4), new Point(finalBarX - 5, staffTop + 4 * _host.StaffGap + 4));
        }
        if (showTab)
        {
            dc.DrawLine(finalPen, new Point(finalBarX, tabTop - 4), new Point(finalBarX, tabTop + (strings - 1) * _host.StringGap + 4));
            if (endsSong) dc.DrawLine(finalThin, new Point(finalBarX - 5, tabTop - 4), new Point(finalBarX - 5, tabTop + (strings - 1) * _host.StringGap + 4));
        }

        var voltas = new List<VoltaSpan>();
        _textSpill.Clear();
        foreach (var measurePosition in systemLayout.Measures)
        {
            if (!_host.InHorizontalBand(measurePosition)) continue;
            var measureIndex = measurePosition.MeasureIndex;
            var measure = track.Measures[measureIndex];
            var editingCells = CellsFor(measure);
            var x = measurePosition.X;
            var measureWidth = measurePosition.Width;
            var slots = _host.SlotsFor(measureIndex);
            var slotWidth = measureWidth / Math.Max(1, slots);
            var warp = _layout.WarpFor(track, measureIndex);
            double SlotX(double s) => x + warp.Fraction(s) * measureWidth;

            // Titles, endings and beat text ride above the tallest stem of the bar (drum chords have long stems).
            PrepareMarkSkyline(track, measure, measureIndex, x, staffTop, slotWidth, slots, showStaff, tabTop);
            _barRight = x + measureWidth;
            foreach (var spill in _textSpill) _sky.Claim(spill);   // a long text of the previous bar runs on into this one: bar number, tempo and title stack above it
            DrawBarStaffAnnotations(dc, track, measure, measureIndex, x, measureWidth, staffTop, ink, tabTop + 2.5 * _host.StringGap);

            // standard repeat barlines on both staves: thick line, thin line and two dots (start ||:,
            // end :||), with the play count above the end repeat.
            if (measure.RepeatStart || measure.RepeatEnd)
            {
                var inkBrush = ScoreText.Brush(ink);
                var heavy = RenderDraw.Pen(ink, 3.2);
                var light = RenderDraw.Pen(ink, 1);
                void RepeatSign(double top, double bottom, bool start)
                {
                    var edge = start ? Math.Round(x) + 1.6 : Math.Round(x + measureWidth) - 1.6;
                    var thin = start ? edge + 4.5 : edge - 4.5;
                    var dots = start ? thin + 4.5 : thin - 4.5;
                    dc.DrawLine(heavy, new Point(edge, top), new Point(edge, bottom));
                    dc.DrawLine(light, new Point(thin, top), new Point(thin, bottom));
                    var mid = (top + bottom) / 2; var gap = Math.Max(4, (bottom - top) / 6);
                    dc.DrawEllipse(inkBrush, null, new Point(dots, mid - gap), 1.9, 1.9);
                    dc.DrawEllipse(inkBrush, null, new Point(dots, mid + gap), 1.9, 1.9);
                }
                foreach (var start in new[] { true, false })
                {
                    if (start ? !measure.RepeatStart : !measure.RepeatEnd) continue;
                    if (showStaff) RepeatSign(staffTop, staffTop + 4 * _host.StaffGap, start);
                    if (showTab) RepeatSign(tabTop, tabTop + (strings - 1) * _host.StringGap, start);
                }
                if (measure.RepeatEnd && measure.RepeatCount > 2)
                    ScoreText.DrawIn(ScoreTextArea.BarInfo, dc, $"x{measure.RepeatCount}", x + measureWidth - 22, (showStaff ? staffTop : tabTop) - 16, 10, inkBrush, FontWeights.Bold);
            }

            // Opt-in playing-bar band: behind everything that follows (selection, notes, marks).
            if (_host.PlayingBarBand(track, systemLayout, measurePosition) is { } playingBar)
                dc.DrawRectangle(playingBar.Brush, null, playingBar.Rect);

            var isError = _layout.BarStateFor(measureIndex).Marked;
            if (isError && measureIndex != _host.SelectedMeasure)
            {
                var tint = ScoreText.Brush(Color.FromArgb(38, errorColor.R, errorColor.G, errorColor.B));
                dc.DrawRectangle(tint, null, new Rect(x + 1, staffTop - 6, measureWidth - 2, (showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap) - staffTop + 18));
            }

            // Selection range overlay
            if (_host.HasSelection)
            {
                var (m1, c1, m2, c2) = _host.SelectionRange();
                if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
                if (measureIndex >= m1 && measureIndex <= m2 && editingCells.Count > 0)
                {
                    var from = measureIndex == m1 ? c1 : 0;
                    var to = measureIndex == m2 ? c2 : slots - 1;
                    var startSlots = CellStartSlots(measure, from, editingCells);
                    var endCell = editingCells[Math.Clamp(to, 0, editingCells.Count - 1)];
                    var endSlots = Math.Min(slots, CellStartSlots(measure, to, editingCells) + Math.Max(1, MusicTime.CellSlots(endCell)));
                    var sx = SlotX(startSlots);
                    var sw = Math.Max(4, SlotX(endSlots) - sx);
                    var selected = _host.Appearance.SelectionColor;
                    var selectionAlpha = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(_host.Appearance.SelectionHighlightIntensity, 0, 1)), 0, 255);
                    var selBrush = ScoreText.Brush(Color.FromArgb(selectionAlpha, selected.R, selected.G, selected.B));
                    dc.DrawRectangle(selBrush, RenderDraw.Pen(selected, 1), new Rect(sx, staffTop - 8, sw, (showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap) - staffTop + 22));
                }
            }

            // Playback: retain the configurable beat tint, then draw the active note's remaining
            // duration ahead of the independent, lightweight caret overlay.
            if (measureIndex == _host.PlaybackMeasure && _host.PlaybackCell >= 0 && measure.Cells.Count > 0)
            {
                var beatCell = measure.Cells[Math.Clamp(_host.PlaybackCell, 0, measure.Cells.Count - 1)];
                var beatStart = CellStartSlots(measure, _host.PlaybackCell);
                var beatEnd = Math.Min(slots, beatStart + MusicTime.CellSlots(beatCell));
                var px = SlotX(beatStart);
                var beatWidth = Math.Max(0, SlotX(beatEnd) - px);
                var bandBottom = showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap;
                var bandRect = new Rect(px, staffTop - 8, beatWidth, bandBottom - staffTop + 22);
                if (_host.Appearance.HighlightPlayedBeat && beatWidth > 0 && _host.Appearance.DurationGlowOpacity > 0)
                {
                    // The elapsed beat tint stops at the caret; the overlay shades only the
                    // remaining duration. This prevents the two independently rendered layers
                    // from stacking opacity over the same part of the beat.
                    var playedSlots = Math.Clamp(_host.PlaybackFraction * slots, beatStart, beatEnd);
                    var playedWidth = Math.Max(0, SlotX(playedSlots) - px);
                    var tintAlpha = PlaybackGlowIntensity.ScaleAlpha(_host.Appearance.HighlightBackground.A, _host.Appearance.DurationGlowOpacity);
                    if (playedWidth > 0 && tintAlpha > 0)
                    {
                        var tint = Color.FromArgb(tintAlpha, _host.Appearance.HighlightBackground.R, _host.Appearance.HighlightBackground.G, _host.Appearance.HighlightBackground.B);
                        dc.DrawRectangle(ScoreText.Brush(tint), null,
                            new Rect(px, bandRect.Y, Math.Min(beatWidth, playedWidth), bandRect.Height));
                    }
                }

            }

            // Hover: a faint outline shows which beat a click would act on, without looking like the
            // edit cursor, the selection or the playhead.
            if (measureIndex == _host.HoverMeasure && _host.HoverCell >= 0 && _host.HoverCell < editingCells.Count && !_host.PlaybackActive)
            {
                var hoverRect = CellHighlightRect(measure, editingCells, _host.HoverCell, x, slotWidth, warp, measureWidth,
                    staffTop - 4, (showTab ? tabTop + (strings - 1) * _host.StringGap : staffTop + 4 * _host.StaffGap) - staffTop + 12);
                var hoverAlpha = (byte)Math.Clamp(Math.Round(255 * Math.Clamp(_host.Appearance.HoverHighlightIntensity, 0, 1)), 0, 255);
                dc.DrawRectangle(null, RenderDraw.Pen(Color.FromArgb(hoverAlpha, _host.Appearance.HoverColor.R, _host.Appearance.HoverColor.G, _host.Appearance.HoverColor.B), 1), hoverRect);
            }

            // Cursor (dimmed while the transport runs so the green playhead is the tracker)
            if (measureIndex == _host.SelectedMeasure && !_host.HideCursor)
            {
                var cy = showTab ? tabTop + _host.SelectedString * _host.StringGap - 8 : staffTop;
                var height = showTab ? 16 : 4 * _host.StaffGap;
                var cursorAlpha = _host.PlaybackActive ? (byte)70 : (byte)255;
                var cursorPen = RenderDraw.Pen(Color.FromArgb(cursorAlpha, cursorColor.R, cursorColor.G, cursorColor.B), _host.PlaybackActive ? 1.1 : 1.6);
                dc.DrawRectangle(null, cursorPen,
                    CursorRect(measure, editingCells, _host.SelectedCell, x, warp, measureWidth, cy, height));
            }

            DrawMeasure(dc, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, slotWidth, slots, strings, ink, faint, line, accent, playColor, showStaff, showTab,
                measure.Cells, inactiveVoice: _host.Project?.GrayInactiveVoice == true && _host.ActiveVoiceIndex != 0);
            if (ScoreLayoutEngine.Voice2HasContent(measure))
                DrawMeasure(dc, track, measure, measureIndex, x, measureWidth, staffTop, tabTop, slotWidth, slots, strings, ink, faint, line, accent, playColor, showStaff, showTab,
                    measure.Voice2Cells, inactiveVoice: _host.Project?.GrayInactiveVoice == true && _host.ActiveVoiceIndex != 1);
            // Bar-level texts stack last, outside everything the notes and their marks claimed.
            DrawBarLabels(dc, track, measure, measureIndex, x, measureWidth, staffTop, ink, faint, accent, voltas);
        }
        DrawVoltaBrackets(dc, voltas, ink);

        if (showTab || showStaff) DrawPalmMutePassages(dc, palmMutePassages, track, systemLayout, tabTop, ink, showStaff ? staffTop + 4 * _host.StaffGap : null);
        DrawFadePassages(dc, fadePassages, track, systemLayout, staffTop, tabTop, ink, showTab);
    }

    private void DrawMeasure(DrawingContext dc, TrackModel track, MeasureModel measure, int measureIndex, double x, double measureWidth, double staffTop, double tabTop, double slotWidth, int slots, int strings, Color ink, Color faint, Color staffLine, Color accent, Color playColor, bool showStaff, bool showTab,
        IReadOnlyList<TabCell> cells, bool inactiveVoice)
    {
        // A simile bar shows only its sign (the notes behind it are a copy kept for playback and export).
        if (SimileHidesNotes(measure, measureIndex)) cells = Array.Empty<TabCell>();
        if (inactiveVoice)
        {
            var gray = Color.FromRgb(0x6F, 0x7A, 0x89);
            ink = gray;
            faint = Color.FromRgb(0x5D, 0x67, 0x75);
        }
        _bendLabelBoxes.Clear();
        _drawnBendLabels.Clear();
        _pendingBendLabels.Clear();
        var bg = _host.Appearance.DarkPaper ? _host.Appearance.DarkPaperColor : _host.Appearance.LightPaperColor;
        var numerator = measure.TimeSigNum ?? _host.Project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _host.Project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _host.Project?.KeySignature ?? 0;
        var layout = _layout.StaffLayoutFor(track, measure, measureIndex, slots, x, staffTop, slotWidth,
            numerator, denominator, keySignature, cells);
        layout.Skyline = _sky;
        layout.FirstVoice = layout.IsSecondVoice ? _layout.CachedStaffLayout(measureIndex, 0) : null;
        {
            var signatureRight = double.NegativeInfinity;
            if (_host.Notation != NotationMode.TabOnly && _host.Project is not null)
            {
                var afterSignatures = x + 28 + (ScoreClefKey.ClefChanges(track, measureIndex) ? ScoreClefKey.ClefChangeWidth : 0) + (_layout.KeySignatureChanges(track, measureIndex) ? _layout.KeySignatureWidth(track, measureIndex) : 0);
                if (_layout.TimeSignatureShown(track, measureIndex)) afterSignatures += _layout.TimeSignatureWidth(measure) + 4;
                if (afterSignatures > x + 28) signatureRight = afterSignatures;
            }
            layout.ContentLeft = signatureRight;
        }
        if (showStaff) DrawDynamics(dc, track, measure, measureIndex, layout, staffTop, tabTop, strings, ink, showStaff);   // nearest the staff: claims its row first
        if (showStaff)
            _staff.DrawMeasure(dc, layout, measureIndex, ink, faint, accent, playColor, bg, staffLine, _host.Appearance.LedgerLines,
                _host.SoundingNotes, _host.StruckNotes);
        if (showStaff && layout.Beats.Count == 0 && !layout.IsSecondVoice && !ScoreLayoutEngine.Voice2HasContent(measure) && !measure.SimileOneBar && !measure.SimileTwoBar)
            StaffNotationRenderer.DrawWholeBarRest(dc, x + measureWidth / 2, staffTop, ink);   // an empty bar reads as a whole-bar rest
        if (showTab)
            DrawTabSlides(dc, track, measureIndex, layout, cells, strings, tabTop, ink);

        foreach (var beat in layout.Beats)
        {
            var cell = beat.Cell;
            var i = beat.CellIndex;
            var cx = beat.CenterX;
            if (cell.IsRest && cell.Notes.Count == 0)
            {
                if (showTab && !showStaff && !layout.IsSecondVoice) // The reference shows the rest only on the staff when notation is visible
                {
                    // A fret of the other voice at the same beat sits at the rest's height: the rest moves up, clear of the topmost such fret.
                    var restY = tabTop + (strings - 1) * _host.StringGap / 2.0 - 9;
                    var otherCells = ReferenceEquals(cells, measure.Voice2Cells) ? measure.Cells : measure.Voice2Cells;
                    var restAt = cell.RhythmicPosition ?? i;
                    var sameBeat = otherCells.Where((o, k) => o.Notes.Count > 0 && Math.Abs((o.RhythmicPosition ?? k) - restAt) < 0.01).SelectMany(o => o.Notes).ToList();
                    if (sameBeat.Count > 0)
                        restY = Math.Min(restY, tabTop + sameBeat.Min(n => n.StringIndex) * _host.StringGap - 27);
                    ScoreText.DrawCentered(dc, StaffNotationRenderer.RestGlyph(cell), cx, restY, 14, ScoreText.Brush(faint));
                }
                // A rest can still carry a chord name, beat text and a fermata (a held rest).
                if (!string.IsNullOrWhiteSpace(cell.ChordName))
                {
                    var restChordFt = ScoreText.MakeTextIn(ScoreTextArea.Chord, cell.ChordName!, 10, ScoreText.Brush(accent), FontWeights.SemiBold);
                    StackTextAbove(dc, restChordFt, 10, cx - restChordFt.Width / 2, staffTop, 14);
                }
                if (!string.IsNullOrWhiteSpace(cell.Text))
                {
                    var restTextFt = ScoreText.MakeTextIn(ScoreTextArea.Lyrics, cell.Text!, ScoreMarkText.BeatTextSize, ScoreText.Brush(ScoreMarkText.BeatTextColor(ink, faint)));
                    StackTextAbove(dc, restTextFt, ScoreMarkText.BeatTextSize, Math.Clamp(cx - restTextFt.Width / 2, x + 2, Math.Max(x + 2, x + measureWidth - restTextFt.Width - 2)), staffTop, 14);
                }
                if (cell.Fermata && !showStaff && !layout.FermataSharedWithFirstVoice(beat)) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, dc, "𝄐", cx, _sky.PlaceAbove(cx - 6, cx + 6, 14, tabTop - 10), 12, ScoreText.Brush(ink));   // tab only: one fermata per onset
                continue;
            }

            {
                    var techniqueLabel = showTab ? ScoreMarkText.DrawnTechniqueLabel(cell.Notes, !showStaff) : "";
                    if (!string.IsNullOrWhiteSpace(cell.ChordName))
                    {
                        var chordFt = ScoreText.MakeTextIn(ScoreTextArea.Chord, cell.ChordName!, 10, ScoreText.Brush(accent), FontWeights.SemiBold);
                        StackTextAbove(dc, chordFt, 10, cx - chordFt.Width / 2, staffTop, 14);
                    }
            // The reference vibrato: a wavy line along the note's duration, above the staff and above the TAB.
            var vibratoWide = cell.Notes.Any(n => n.Techniques.Contains("WideVibrato"));
            if (vibratoWide || cell.Notes.Any(n => n.Techniques.Contains("Vibrato")))
            {
                var right = beat.CenterX + Math.Max(18, beat.DurationSlots * (measureWidth / Math.Max(1, slots)) * 0.8);
                right = Math.Max(Math.Min(right, x + measureWidth - 3), beat.CenterX + 8);   // the line ends inside its bar (and so inside the page)
                // Stack above whatever already sits over the note instead of drawing across it:
                // staff: above the chord-name / text lanes when present; TAB: above the technique label
                // and the P.M. lane.
                var staffLane = staffTop - 16;
                if (showStaff)
                {
                    // Stacked above the staff's marks for this column (the wavy line is about 8 px tall).
                    var vibTop = _sky.PlaceAbove(cx - 6, right, vibratoWide ? 9 : 7, staffTop - 8);
                    staffLane = vibTop + (vibratoWide ? 4.5 : 3.5);
                }
                var pm = cell.Notes.Any(note => note.Techniques.Any(ScoreMarkText.IsPalmMute));
                var tabLane = tabTop - 12;
                if (pm && !showStaff) tabLane = tabTop - 30;
                if (ScoreMarkText.DrawnTechniqueLabel(cell.Notes, !showStaff).Length > 0) tabLane = tabTop - (pm && !showStaff ? 34 : 20) - 9;
                if (showStaff) DrawVibratoLine(dc, cx - 6, right, staffLane, vibratoWide, ScoreText.Brush(ink));
                if (showTab) DrawVibratoLine(dc, cx - 6, right, tabLane, vibratoWide, ScoreText.Brush(ink));
            }
            // Mix Table point (F10): a red marker with a white core above the beat dot.
            if (cell.Mix is not null)
            {
                var mixY = _sky.PlaceAbove(cx - 4.2, cx + 4.2, 8.4, staffTop - 24) + 4.2;
                dc.DrawEllipse(ScoreText.Brush(Color.FromRgb(0xE0, 0x3B, 0x3B)), null, new Point(cx, mixY), 4.2, 4.2);
                dc.DrawEllipse(ScoreText.Brush(Colors.White), null, new Point(cx, mixY), 1.5, 1.5);
            }
                    if (!string.IsNullOrWhiteSpace(cell.Text))
                    {
                        // Beat text stacks above the staff's marks like every other text; the bar's title and tempo stack above it.
                        var textFt = ScoreText.MakeTextIn(ScoreTextArea.Lyrics, cell.Text!, ScoreMarkText.BeatTextSize, ScoreText.Brush(ScoreMarkText.BeatTextColor(ink, faint)));
                        StackTextAbove(dc, textFt, ScoreMarkText.BeatTextSize, Math.Clamp(cx - textFt.Width / 2, x + 2, Math.Max(x + 2, x + measureWidth - textFt.Width - 2)), staffTop, 14);
                    }
                    if (cell.Fermata && !showStaff && !layout.FermataSharedWithFirstVoice(beat)) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, dc, "𝄐", cx, _sky.PlaceAbove(cx - 6, cx + 6, 14, tabTop - 10), 12, ScoreText.Brush(ink));   // tab only: one fermata per onset
                    if (cell.Accent != 0 && !showStaff)
                    {
                        // With a notation staff the accent is engraved above the note there; tab-only shows it here.
                        var accentY = _sky.PlaceAbove(cx - 5, cx + 5, 12, tabTop - 10);
                        ScoreText.DrawCenteredIn(ScoreTextArea.Technique, dc, cell.Accent == 2 ? "^" : ">", cx, accentY, 11, ScoreText.Brush(ink), FontWeights.Bold);
                    }
                    // With a notation staff the renderer engraves staccato / tenuto beside the note head; tab-only shows them here.
                    if (cell.Staccato && !showStaff) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, dc, "•", cx, _sky.PlaceAbove(cx - 4, cx + 4, 10, tabTop - 10), 10, ScoreText.Brush(ink));
                    if (cell.Tenuto && !showStaff) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, dc, "—", cx, _sky.PlaceAbove(cx - 5, cx + 5, 10, tabTop - 10), 10, ScoreText.Brush(ink));
                    if (cell.IsGrace && !cell.Notes.Any(n => n.IsGraceNote))
                    {
                        var grFt = ScoreText.MakeTextIn(ScoreTextArea.Technique, "gr", 8, ScoreText.Brush(faint));
                        StackTextAbove(dc, grFt, 8, cx - grFt.Width / 2, staffTop, 14);
                    }

                    // Lyrics sit under the TAB staff, one line per row (the reference layout).
                    if (!string.IsNullOrWhiteSpace(cell.Lyrics))
                    {
                        var lines = cell.Lyrics.Split('\n');
                        // The row starts under the fingering of every beat the text runs across, not only its own.
                        var lyricWidth = lines.Max(l => ScoreText.MakeTextIn(ScoreTextArea.Lyrics, l, 10, ScoreText.Brush(ink)).Width);
                        var fingeringUnder = ScoreMarkText.FingeringHeight(cell);
                        foreach (var other in layout.Beats)
                            if (Math.Abs(other.CenterX - cx) < lyricWidth / 2 + 8) fingeringUnder = Math.Max(fingeringUnder, ScoreMarkText.FingeringHeight(other.Cell));
                        for (var li = 0; li < Math.Min(lines.Length, 3); li++)
                            ScoreText.DrawCenteredIn(ScoreTextArea.Lyrics, dc, lines[li], cx, tabTop + (strings - 1) * _host.StringGap + 13 + fingeringUnder + li * 14.5, 10, ScoreText.Brush(ink));
                    }

                    foreach (var note in cell.Notes)
                    {
                        if (note.StringIndex < 0 || note.StringIndex >= strings) continue;
                        var isSounding = _host.SoundingNotes.Contains((measureIndex, i, note.StringIndex));
                        var isStruck = _host.StruckNotes.Contains((measureIndex, i, note.StringIndex));
                        // The reference prints no fret number for a tied-to note in the TAB (the tie is in the notation); the selected beat keeps it so it can still be edited.
                        // Tab only: there is no notation to show the tie, so the number stays.
                        var tiedTo = (note.Tied || cell.IsTied) && !note.IsGraceNote && track.Kind != TrackKind.Drums && _host.Notation != NotationMode.TabOnly && !(measureIndex == _host.SelectedMeasure && i == _host.SelectedCell);
                        if (showTab && !tiedTo)
                        {
                            var sy = tabTop + note.StringIndex * _host.StringGap;
                            var label = note.Dead ? "X"
                                : track.Kind == TrackKind.Drums ? DrumMaps.For(track, note.MidiValue > 0 ? note.MidiValue : note.Fret).Label
                                : note.Fret.ToString(CultureInfo.InvariantCulture);
                            if (track.Kind == TrackKind.Drums)
                            {
                                // Several drum sounds on one line at one beat (e.g. two cymbals) share one label instead of overprinting.
                                var sameLine = cell.Notes.Where(o => o.StringIndex == note.StringIndex).ToList();
                                if (sameLine[0] != note) continue;
                                if (sameLine.Count > 1)
                                    label = string.Join("\n", sameLine.Select(o => DrumMaps.For(track, o.MidiValue > 0 ? o.MidiValue : o.Fret).Label));
                            }
                            if (note.Ghost && !note.Dead) label = "(" + label + ")"; // The reference: a ghost note is the fret in brackets
                            var isGraceNote = note.IsGraceNote && cell.Notes.Any(other => !other.IsGraceNote);
                            var brush = ScoreText.Brush(ink);
                            var ft = ScoreText.MakeTextIn(ScoreTextArea.Fret, label, isGraceNote ? _host.FretFontSize - 3 : label.Contains('\n') ? _host.FretFontSize - 4 : _host.FretFontSize, brush, FontWeights.Normal, "Consolas");
                            // a grace fret sits small, just before the main fret: clear of the widest main fret number (two digits, brackets) by a gap of at least 2.4 px
                            var mainChars = isGraceNote ? cell.Notes.Where(o => !o.IsGraceNote).Max(o => Math.Max(1, o.Fret.ToString().Length) + (o.Ghost && !o.Dead ? 2 : 0)) : 0;
                            var gx = isGraceNote ? cx - Math.Max(13, mainChars * _host.FretFontSize * 0.55 / 2 + ft.Width / 2 + 3.6 + (ScoreMarkText.GraceTransitionShown(track, cell, note) ? 6 : 0)) : cx;   // room for the line / arc to the main note
                            var chipWidth = ft.Width + 2;
                            var chipHeight = Math.Max(14, ft.Height + 2);

                            if (!isGraceNote) DrawTabHopoSlur(dc, note, measureIndex, i, cx, sy, ink, ReferenceEquals(cells, measure.Voice2Cells) ? 1 : 0);

                            if (isSounding)
                            {
                                // Exact note being played: a bright pill behind the fret number.
                                var glow = ScoreText.Brush(Color.FromArgb(isStruck ? (byte)90 : (byte)46, playColor.R, playColor.G, playColor.B));
                                dc.DrawRoundedRectangle(glow, RenderDraw.Pen(playColor, isStruck ? 1.8 : 1.0),
                                    new Rect(gx - chipWidth / 2 - 2, sy - chipHeight / 2 - 2, chipWidth + 4, chipHeight + 4), 4, 4);
                                if (isStruck)
                                    dc.DrawRoundedRectangle(null, RenderDraw.Pen(playColor, 1.0),
                                        new Rect(gx - chipWidth / 2 - 5, sy - chipHeight / 2 - 3, chipWidth + 10, chipHeight + 6), 5, 5);
                            }
                            else
                            {
                                dc.DrawRectangle(ScoreText.Brush(bg), null,
                                    new Rect(gx - chipWidth / 2, sy - chipHeight / 2, chipWidth, chipHeight));
                            }
                            TabForge.Visualization.Draw.DrawText(dc, ft, new Point(gx - ft.Width / 2, sy - ft.Height / 2));
                            if (isGraceNote && ScoreMarkText.GraceTransitionShown(track, cell, note)) DrawTabGraceTransition(dc, cell, note, gx, ft.Width, cx, sy, tabTop, ink);
                        }
                    }
                    if (techniqueLabel.Length > 0)
                    {
                        // One complete, width-reserved annotation per beat prevents chord techniques
                        // from being overprinted and keeps simultaneous marks together.
                        var hasPalmMute = cell.Notes.Any(note => note.Techniques.Any(ScoreMarkText.IsPalmMute));
                        var techniqueY = tabTop - (hasPalmMute && !showStaff ? 34 : 20);
                        var techniqueFt = ScoreText.MakeTextIn(ScoreTextArea.Technique, techniqueLabel, 9, ScoreText.Brush(ink),
                            techniqueLabel.Split(' ').Contains("T") ? FontWeights.SemiBold : FontWeights.Normal);
                        // Between the staff and the TAB the label stacks upward from the TAB, clear of whatever the staff side claimed.
                        if (showStaff) techniqueY = _sky.PlaceAbove(cx - techniqueFt.Width / 2, cx + techniqueFt.Width / 2, 11, tabTop - 9) - 1;
                        else techniqueY = _sky.PlaceAbove(cx - techniqueFt.Width / 2, cx + techniqueFt.Width / 2, 11, tabTop - 9);
                        TabForge.Visualization.Draw.DrawText(dc, techniqueFt, new Point(cx - techniqueFt.Width / 2, techniqueY));
                    }
                    if (showTab)
                    {
                        // Voice 2's marks under the TAB go below voice 1's at the same beat.
                        var voiceOneUnder = 0.0;
                        if (layout.IsSecondVoice && _layout.CachedStaffLayout(measureIndex, 0) is { } voiceOne)
                            foreach (var other in voiceOne.Beats)
                                if (Math.Abs(other.CenterX - cx) < 14) voiceOneUnder = Math.Max(voiceOneUnder, ScoreMarkText.FingeringExtent(other.Cell) > 0 ? ScoreMarkText.FingeringExtent(other.Cell) - 8 : 0);
                        DrawTabBeatMarks(dc, track, layout, beat, x + measureWidth, tabTop, strings, ink, showStaff, voiceOneUnder);
                    }
            }
        }
        if (showTab) DrawLetRingSpans(dc, layout, x + measureWidth, tabTop, ink, showStaff);
        if (!showStaff) DrawDynamics(dc, track, measure, measureIndex, layout, staffTop, tabTop, strings, ink, showStaff);
        FlushBendLabels(dc);
    }

    // ---- row stacking: one skyline per bar, shared by both voices; every mark claims its box and the next is placed outside it ----
    private readonly MarkSkyline _sky = new();

    private readonly List<Rect> _textSpill = new();   // the parts of stacked texts that run past their bar's right edge, claimed again in the next bar

    private double _barRight;

    /// <summary>The page header: title, artist, authors and the tuning block.</summary>
    internal void DrawHeader(DrawingContext dc, SongProject project, TrackModel track, Color ink, Color faint)
    {
        ScoreText.DrawCenteredIn(ScoreTextArea.Header, dc, project.Title, _host.HeaderCentreX, 8, 22, ScoreText.Brush(ink), FontWeights.SemiBold, "Segoe UI");
        if (!string.IsNullOrWhiteSpace(project.Artist))
            ScoreText.DrawCenteredIn(ScoreTextArea.Header, dc, project.Artist, _host.HeaderCentreX, 34, 12, ScoreText.Brush(faint));
        var wordsBy = string.IsNullOrWhiteSpace(project.LyricsAuthor) ? "" : $"Words by {project.LyricsAuthor}";
        var musicBy = string.IsNullOrWhiteSpace(project.MusicAuthor) ? "" : $"Music by {project.MusicAuthor}";
        if (wordsBy.Length > 0) ScoreText.DrawIn(ScoreTextArea.Header, dc, wordsBy, _host.GridLeft, 51, 9, ScoreText.Brush(faint));
        if (musicBy.Length > 0)
        {
            var text = ScoreText.MakeTextIn(ScoreTextArea.Header, musicBy, 9, ScoreText.Brush(faint));
            ScoreText.DrawIn(ScoreTextArea.Header, dc, musicBy, _host.GridLeft + _host.GridWidth - text.Width, 51, 9, ScoreText.Brush(faint));
        }
        DrawTuningBlock(dc, track, faint);
    }

    private List<TabCell> CellsFor(MeasureModel measure) => measure.CellsForVoice(_host.ActiveVoiceIndex);

}
