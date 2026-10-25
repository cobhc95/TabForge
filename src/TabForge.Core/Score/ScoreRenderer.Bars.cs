using System.Globalization;
using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the bar furniture the score renderer draws: the tuning block, clef and signatures, tempo, section and volta labels and directions.
// Does not own: the marks over the tab (ScoreRenderer.Marks.cs) or the note heads and stems (ScoreRenderer.cs).
// Tests: no dedicated test class.
// Length: over 300 lines because the bar furniture is drawn as one ordered sequence of labels and signatures that share the bar layout.

// ScoreRenderer: bar furniture (clef, signatures, tempo, section and volta labels, directions).
internal sealed partial class ScoreRenderer
{
    private void DrawTuningBlock(IDrawContext d, TrackModel track, UiColor faint)
    {
        if (!ScoreLayoutEngine.HasStringTuning(track)) return;
        var tuning = track.StringTunings;
        var rowCount = Math.Max(1, (tuning.Count + 1) / 2);
        ScoreText.DrawIn(ScoreTextArea.Header, d, TuningName(tuning), _host.GridLeft, 65, 9, faint, 600);
        for (var stringIndex = 0; stringIndex < tuning.Count; stringIndex++)
        {
            var column = stringIndex / rowCount;
            var row = stringIndex % rowCount;
            ScoreText.DrawIn(ScoreTextArea.Header, d, $"{stringIndex + 1} = {MusicTheoryService.NoteName(tuning[stringIndex])}",
                _host.GridLeft + column * 72, 77 + row * 11, 9, faint);
        }
    }

    private static string TuningName(IReadOnlyList<int> tuning)
    {
        if (tuning.SequenceEqual(new[] { 62, 57, 53, 48, 43, 36 })) return "Dropped C Tuning";
        if (tuning.SequenceEqual(new[] { 64, 59, 55, 50, 45, 38 })) return "Drop D Tuning";
        if (tuning.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40 })) return "Standard Tuning";
        return "Tuning";
    }

    /// <summary>
    /// A simile bar shows only its sign, as in the reference. While the edit cursor or the selection is inside it (and the
    /// transport is stopped) its notes are shown, so what the user edits there is never invisible.
    /// </summary>
    private bool SimileHidesNotes(MeasureModel measure, int measureIndex)
    {
        if (!measure.SimileOneBar && !measure.SimileTwoBar) return false;
        if (_host.HideCursor || _host.PlaybackActive) return true;
        if (measureIndex == _host.SelectedMeasure) return false;
        if (!_host.HasSelection) return true;
        var (m1, _, m2, _) = _host.SelectionRange();
        return measureIndex < Math.Min(m1, m2) || measureIndex > Math.Max(m1, m2);
    }

    /// <summary>A volta bracket waiting for the end of the system, so all bars of one ending share one height.</summary>
    private readonly record struct VoltaSpan(int Measure, double X, double Right, double Top, string Label, bool OpenStart, bool OpenEnd);

    /// <summary>Starts the bar's skyline: the notation ink of both voices (heads, accidentals, stems, beams, grace notes) is claimed first.</summary>
    private void PrepareMarkSkyline(TrackModel track, MeasureModel measure, int measureIndex, double x, double staffTop, double slotWidth, int slots, bool showStaff, double tabTop)
    {
        _sky.Clear();
        // With a TAB, the palm-mute label and dashed line take their lane just above the strings; vibrato, accents, technique labels and let ring stack above it, one row each.
        if (_host.Notation != NotationMode.StaffOnly)
            for (var v = 0; v < (ScoreLayoutEngine.Voice2HasContent(measure) ? 2 : 1); v++)
            {
                var cells = v == 0 ? measure.Cells : measure.Voice2Cells;
                for (var i = 0; i < cells.Count; i++)
                {
                    if (!cells[i].Notes.Any(ScoreMarkText.ShowsPalmMute)) continue;
                    var cx = CellCenterX(measureIndex, i, v);   // the beat's stretch of the lane, from the "P.M." label left of it to its end
                    _sky.Claim(cx - 26, Math.Min(cx + slotWidth * MusicTime.CellSlots(cells[i]) + 2, x + slotWidth * slots), tabTop - 24, tabTop - 10);
                }
            }
        if (!showStaff)
        {
            foreach (var fade in _layout.FadePassages)
                if (measureIndex >= fade.FirstMeasure && measureIndex <= fade.LastMeasure) { _sky.Claim(x, x + slotWidth * slots, tabTop - 13, tabTop - 3); break; }   // the fade wedge sits just above the strings
            return;
        }
        var numerator = measure.TimeSigNum ?? _host.Project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _host.Project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _host.Project?.KeySignature ?? 0;
        if (SimileHidesNotes(measure, measureIndex)) return;   // only the sign is drawn: hidden notes claim no space
        // Both voices without a per-bar array (this runs for the playing system on every playback tick).
        for (var v = 0; v < (ScoreLayoutEngine.Voice2HasContent(measure) ? 2 : 1); v++)
        {
            var cells = v == 0 ? measure.Cells : measure.Voice2Cells;
            var layout = _layout.StaffLayoutFor(track, measure, measureIndex, slots, x, staffTop, slotWidth, numerator, denominator, keySignature, DrawnCells(measure, cells, slots));
            layout.Skyline = _sky;
            StaffNotationRenderer.SeedSkyline(layout);
        }
    }

    /// <summary>Text drawn from its left edge, stacked above the staff (<paramref name="distance"/> = gap between the staff top and the row's bottom).</summary>
    private double StackTextAbove(IDrawContext d, in UiText ft, double size, double x, double staffTop, double distance)
    {
        var h = size * 1.2;
        var top = _sky.PlaceAbove(x, x + ft.Width, h, staffTop - distance);
        if (x + ft.Width > _barRight + 0.5 && x < _barRight) _textSpill.Add(new UiRect(_barRight, top, x + ft.Width - _barRight, h));
        ScoreText.DrawFormatted(d, ft, x, top - size * 0.1);
        return top;
    }

    /// <summary>Key and time signatures, simile marks: drawn on the staff itself, no stacking.</summary>
    private void DrawBarStaffAnnotations(IDrawContext d, TrackModel track, MeasureModel measure, int measureIndex, double x, double measureWidth, double staffTop, UiColor ink, double tabMiddle)
    {
        if (_host.Project is null) return;
        var showStaffHere = _host.Notation != NotationMode.TabOnly;
        var noteX = x + 28;
        if (ScoreClefKey.ClefChanges(track, measureIndex))
        {
            if (showStaffHere && _layout.GetLayout(track).SystemForMeasure(measureIndex) == _layout.GetLayout(track).SystemForMeasure(measureIndex - 1)) DrawClef(d, measure.Clef, x + 8, staffTop, 15, ink);   // the new clef, smaller, as the reference does
            noteX += ScoreClefKey.ClefChangeWidth;
        }
        if (_layout.KeySignatureChanges(track, measureIndex))
        {
            var key = measure.KeySignature ?? _host.Project.KeySignature;
            if (showStaffHere) DrawKeySignature(d, measure.Clef, key, _layout.PreviousKeySignature(track, measureIndex), noteX, staffTop, ink);
            noteX += _layout.KeySignatureWidth(track, measureIndex);
        }
        if (_layout.TimeSignatureShown(track, measureIndex) && showStaffHere)
            DrawTimeSignature(d, measure, noteX, staffTop, ink);
        if (measure.SimileOneBar) DrawSimileSign(d, x + measureWidth / 2, showStaffHere ? staffTop + 2 * _host.StaffGap : tabMiddle, 1, ink);
        if (measure.SimileTwoBar) DrawSimileSign(d, x + measureWidth / 2, showStaffHere ? staffTop + 2 * _host.StaffGap : tabMiddle, 2, ink);
    }

    /// <summary>The repeat-bar sign (slash with a dot each side; two slashes for "repeat two bars"), drawn as vector shapes: no font carries it.</summary>
    private void DrawSimileSign(IDrawContext d, double cx, double cy, int bars, UiColor ink)
    {
        var pen = new UiStroke(ink, 2.6);
        const double half = 5.5;
        var gap = bars == 2 ? 4.0 : 0.0;
        for (var i = 0; i < bars; i++)
        {
            var sx = cx + (bars == 2 ? (i == 0 ? -gap : gap) : 0);
            d.Line(pen, sx - half * 0.55, cy + half, sx + half * 0.55, cy - half);
        }
        var dotX = (bars == 2 ? gap : 0) + 7.5;
        d.Ellipse(ink, null, cx - dotX, cy - 3.5, 1.7, 1.7);
        d.Ellipse(ink, null, cx + dotX, cy + 3.5, 1.7, 1.7);
    }

    /// <summary>Whether two bars carry the same alternate ending (one bracket continues across them).</summary>
    private static bool SameEnding(MeasureModel a, MeasureModel b) =>
        (a.AlternateEnding > 0 || a.AlternateEndingMask != 0) && a.AlternateEnding == b.AlternateEnding && a.AlternateEndingMask == b.AlternateEndingMask;

    /// <summary>True for the bar the edit cursor is in: its red marks wait until the cursor leaves it, as in the reference.</summary>
    private bool EditingBar(TrackModel track, int measureIndex)
        => measureIndex >= 0 && measureIndex < track.Measures.Count && !_host.HideCursor && measureIndex == _host.SelectedMeasure;

    /// <summary>Tempo, bar number, swing symbol, section title, directions and the volta row: stacked last, outside the notation and its marks.</summary>
    private void DrawBarLabels(IDrawContext d, TrackModel track, MeasureModel measure, int measureIndex, double x, double measureWidth, double staffTop,
        UiColor ink, UiColor faint, UiColor accent, List<VoltaSpan> voltas)
    {
        var marker = _layout.MarkerForMeasure(measureIndex);
        var sectionLabel = marker?.Title ?? measure.SectionName;
        var sectionColor = MarkerColour(marker, accent);
        var tempoText = _host.Project is null ? null : _layout.TempoText(measure, measureIndex);
        double tempoRight = x + 2;
        // The reference colours the number of a bar whose rhythm does not add up (overfull or short) red; such a bar always shows it.
        var marked = !EditingBar(track, measureIndex) && _layout.BarStateFor(track, measureIndex).Marked;
        if (marked || (_host.Appearance.ShowBarNumbers && measureIndex % Math.Max(1, _host.Appearance.BarNumberFrequency) == 0))
        {
            var numberText = ScoreText.MakeTextIn(d, ScoreTextArea.BarInfo, (measureIndex + 1).ToString(), 9, marked ? _errorColor : accent);
            StackTextAbove(d, numberText, 9, x + 2, staffTop, 14);
            tempoRight = x + 2 + numberText.Width + 6;
        }
        if (tempoText is not null)
        {
            var tempoFt = ScoreText.MakeTextIn(d, ScoreTextArea.BarInfo, tempoText, ScoreMarkText.TempoSize, accent, 700);
            var at = Math.Max(x + 22, tempoRight);
            StackTextAbove(d, tempoFt, ScoreMarkText.TempoSize, at, staffTop, 14);
            tempoRight = at + tempoFt.Width + 8;
        }
        var feel = TripletFeels.Effective(measure);
        if (feel != TripletFeels.None)
        {
            // The reference prints the swing symbol right after the tempo, on the tempo row.
            var swing = ScoreText.MakeTextIn(d, ScoreTextArea.BarInfo, ScoreMarkText.SwingSymbol(feel), 9, faint);
            StackTextAbove(d, swing, 9, Math.Max(x + 22, tempoRight), staffTop, 14);
        }
        if (measure.MidBarTempos is { Count: > 0 } midTempos)
        {
            // Tempo changes inside the bar sit over the beat where they start (a change at the bar start follows the bar's own tempo mark).
            var warp = _layout.WarpFor(track, measureIndex);
            foreach (var point in midTempos)
            {
                var midFt = ScoreText.MakeTextIn(d, ScoreTextArea.BarInfo, $"♩ = {point.Tempo}", ScoreMarkText.TempoSize, accent, 700);
                var midX = point.Slot <= 0 ? Math.Max(x + 22, tempoRight) : x + warp.Fraction(point.Slot) * measureWidth;
                midX = Math.Min(midX, x + measureWidth - midFt.Width - 1);
                StackTextAbove(d, midFt, ScoreMarkText.TempoSize, midX, staffTop, 14);
            }
        }
        double titleRight = x + 2;
        if (_host.Appearance.ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionLabel))
        {
            var titleFt = ScoreText.MakeTextIn(d, ScoreTextArea.BarInfo, sectionLabel!, 10, sectionColor, 700);
            StackTextAbove(d, titleFt, 10, x + 2, staffTop, 30);
            titleRight = x + 2 + titleFt.Width + 10;
        }
        if (!string.IsNullOrWhiteSpace(measure.Directions))
        {
            var dirTop = _sky.PlaceAbove(titleRight, x + measureWidth - 4, 16, staffTop - 30);
            DrawDirections(d, measure.Directions, titleRight, x + measureWidth - 4, dirTop + 4, ink);
        }
        if (measure.AlternateEnding > 0 || measure.AlternateEndingMask != 0)
        {
            // The reference volta bracket: a line over the ending's bars, a label and a hook at its start, a hook at its end.
            // Its row is stacked like everything else; all bars of one ending share the highest row (drawn at the end of the system).
            var top = _sky.PlaceAbove(x + 1, x + measureWidth - 1, 15, staffTop - 44, claim: false);
            var track2 = track.Measures;
            var openStart = measureIndex > 0 && SameEnding(measure, track2[measureIndex - 1]);
            var openEnd = measureIndex + 1 < track2.Count && SameEnding(measure, track2[measureIndex + 1]);
            voltas.Add(new VoltaSpan(measureIndex, x, x + measureWidth, top, measure.EndingLabel, openStart, openEnd));
        }
        if (measure.FreeTime) ScoreText.DrawIn(ScoreTextArea.BarInfo, d, "free", x + measureWidth - 34, staffTop - 10, 8.5, faint);
    }

    /// <summary>Draws the volta brackets collected for the system; the bars of one ending share the highest row.</summary>
    private void DrawVoltaBrackets(IDrawContext d, List<VoltaSpan> voltas, UiColor ink)
    {
        if (voltas.Count == 0) return;
        var pen = new UiStroke(ink, 1.0);
        var brush = ink;
        for (var i = 0; i < voltas.Count; i++)
        {
            var run = i;
            var top = voltas[i].Top;
            while (run + 1 < voltas.Count && voltas[run].OpenEnd && voltas[run + 1].Measure == voltas[run].Measure + 1)
            { run++; top = Math.Min(top, voltas[run].Top); }
            for (var k = i; k <= run; k++)
            {
                var v = voltas[k];
                var y = top + 1;
                d.Line(pen, v.X + 1, y, v.Right - 1, y);
                if (!v.OpenStart)
                {
                    d.Line(pen, v.X + 1, y, v.X + 1, y + 11);
                    ScoreText.DrawIn(ScoreTextArea.BarInfo, d, v.Label, v.X + 4, y + 1.5, 9, brush);
                }
                if (!v.OpenEnd) d.Line(pen, v.Right - 1, y, v.Right - 1, y + 11);
            }
            i = run;
        }
    }

    /// <summary>Navigation directions above a bar: segno / coda signs after the bar's title, Fine / D.C. / D.S. texts right-aligned.</summary>
    private void DrawDirections(IDrawContext d, string directions, double leftX, double rightX, double y, UiColor ink)
    {
        var brush = ink;
        var x = rightX;
        foreach (var raw in directions.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (text, isSign, atLeft) = ScoreMarkText.DirectionText(raw);
            var ft = ScoreText.MakeTextIn(d, ScoreTextArea.BarInfo, text, isSign ? 16 : 10, brush, isSign ? 400 : 600);
            if (atLeft)
            {
                ScoreText.DrawFormatted(d, ft, leftX, y - 4);
                leftX += ft.Width + 6;
            }
            else
            {
                x -= ft.Width;
                ScoreText.DrawFormatted(d, ft, x, isSign ? y - 4 : y);
                x -= 8;
            }
        }
    }

    /// <summary>Large engraved numerals: numerator in the upper half of the staff, denominator in the lower half.</summary>
    private void DrawTimeSignature(IDrawContext d, MeasureModel measure, double x, double staffTop, UiColor ink)
    {
        var (num, den) = _layout.TimeSignatureParts(measure);
        var width = _layout.TimeSignatureWidth(measure);
        var brush = ink;
        DrawCenteredV(d, num, x + width / 2, staffTop + _host.StaffGap, 19, brush, 700);
        DrawCenteredV(d, den, x + width / 2, staffTop + 3 * _host.StaffGap, 19, brush, 700);
    }

    private static void DrawCenteredV(IDrawContext d, string text, double cx, double cy, double size, UiColor brush, int? weight = null, string font = "Segoe UI")
    {
        var ft = ScoreText.MakeText(d, text, size, brush, weight, font);
        ScoreText.DrawFormatted(d, ft, cx - ft.Width / 2, cy - ft.Height / 2);
    }

    private void DrawKeySignature(IDrawContext d, string clef, int signature, int previous, double x, double staffTop, UiColor color)
    {
        var (naturals, count) = ScoreClefKey.KeySignatureGlyphs(previous, signature);
        if (naturals + count == 0) return;
        var isBass = clef.Contains("F", StringComparison.OrdinalIgnoreCase) ||
                     clef.Contains("Bass", StringComparison.OrdinalIgnoreCase);
        var isAlto = clef.Contains("C", StringComparison.OrdinalIgnoreCase) ||
                     clef.Contains("Alto", StringComparison.OrdinalIgnoreCase) ||
                     clef.Contains("Tenor", StringComparison.OrdinalIgnoreCase);
        var sharpOffsets = isBass
            ? new[] { 9.0, 22.5, 4.5, 18.0, 31.5, 13.5, 27.0 }
            : isAlto ? new[] { 18.0, 4.5, 22.5, 9.0, 27.0, 13.5, 31.5 }
            : new[] { 0.0, 13.5, -4.5, 9.0, 22.5, 4.5, 18.0 };
        var flatOffsets = isBass
            ? new[] { 27.0, 13.5, 31.5, 18.0, 36.0, 22.5, 40.5 }
            : isAlto ? new[] { 4.5, 18.0, 0.0, 13.5, 27.0, 9.0, 22.5 }
            : new[] { 18.0, 4.5, 22.5, 9.0, 27.0, 13.5, 31.5 };
        var offsets = signature > 0 ? sharpOffsets : flatOffsets;
        var symbol = signature > 0 ? "♯" : "♭";
        var brush = color;
        var slot = 0;
        if (naturals > 0)
        {
            var old = previous > 0 ? sharpOffsets : flatOffsets;
            var first = Math.Sign(previous) == Math.Sign(signature) ? Math.Abs(signature) : 0;
            for (var i = first; i < first + naturals; i++, slot++)
                DrawCenteredV(d, "♮", x + slot * 10.5 + 4, staffTop + old[i], 15, brush, null, "Segoe UI Symbol");
        }
        for (var i = 0; i < count; i++, slot++)
            DrawCenteredV(d, symbol, x + slot * 10.5 + 4 + (naturals > 0 ? 3 : 0), staffTop + offsets[i], 15, brush, null, "Segoe UI Symbol");
    }

    /// <summary>True when <paramref name="cells"/> hold nothing but the rests the rest fill puts into an empty bar of <paramref name="slots"/> (or nothing at all).</summary>
    /// <paramref name="fills"/> keeps each bar length's fill so a repaint allocates nothing (the playing system is drawn on every playback tick).
    internal static bool OnlyFillRests(IReadOnlyList<TabCell> cells, int slots, Dictionary<int, List<TabCell>>? fills = null)
    {
        if (slots <= 0) return false;
        var anyRest = false;
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.Notes.Count > 0 || c.HasAnnotation || c.Fermata || c.Mix is not null || c.IsTied || c.RhythmicPosition is not null || c.Tuplet.Item1 > 0) return false;
            anyRest |= c.IsRest;
        }
        if (!anyRest) return true;
        var fill = RestFill(slots, fills);
        for (var i = 0; i < Math.Max(cells.Count, fill.Count); i++)
        {
            var rest = i < cells.Count && cells[i].IsRest;
            if (rest != (i < fill.Count && fill[i].IsRest)) return false;
            if (rest && (cells[i].DurationDenominator != fill[i].DurationDenominator || cells[i].Dots != fill[i].Dots)) return false;
        }
        return true;
    }

    // Rest fill of an empty bar, per bar length. Key: the bar length in slots (the slots argument of OnlyFillRests). Ceiling: one list per
    // distinct bar length in the score, filled on first use and never trimmed. Thread: the UI thread, as all score drawing.
    private readonly Dictionary<int, List<TabCell>> _fillRests = new();

    /// <summary>The rests the fill puts into an empty bar of <paramref name="slots"/>, kept in <paramref name="fills"/> when given.</summary>
    private static List<TabCell> RestFill(int slots, Dictionary<int, List<TabCell>>? fills)
    {
        if (fills is not null && fills.TryGetValue(slots, out var fill)) return fill;
        fill = new List<TabCell>();
        BarFill.FillCells(fill, slots, always: true);
        if (fills is not null) fills[slots] = fill;
        return fill;
    }

    /// <summary>
    /// The cells drawn for a voice. By default the reference's rule: a first voice holding only the rest fill's rests draws nothing (written rests
    /// still draw). With <see cref="ScoreAppearance.DrawEmptyBarRests"/> such a voice draws the fill itself (one whole-bar rest in an empty bar),
    /// also when the bar holds no rest cell at all. The skyline pass and the drawing both use this, so they share one cached staff layout per bar.
    /// </summary>
    private IReadOnlyList<TabCell> DrawnCells(MeasureModel measure, IReadOnlyList<TabCell> cells, int slots)
    {
        if (!ReferenceEquals(cells, measure.Cells) || ScoreLayoutEngine.Voice2HasContent(measure) || !OnlyFillRests(cells, slots, _fillRests)) return cells;
        return _host.Appearance.DrawEmptyBarRests ? RestFill(slots, _fillRests) : Array.Empty<TabCell>();
    }

    private static bool HasVibrato(TabCell cell)
    {
        for (var i = 0; i < cell.Notes.Count; i++)
            if (cell.Notes[i].Techniques.Contains("Vibrato") || cell.Notes[i].Techniques.Contains("WideVibrato")) return true;
        return false;
    }

    /// <summary>Where the vibrato line of <paramref name="beat"/> ends when the next beat of its voice (in this bar or the first of the next bar on the same line) also has vibrato; null otherwise.</summary>
    private double? VibratoRunsOnTo(int measureIndex, StaffNotationMeasureLayout layout, StaffNotationBeat beat, int voice)
    {
        var beats = layout.Beats;
        for (var i = 0; i < beats.Count - 1; i++)
            if (ReferenceEquals(beats[i], beat)) return HasVibrato(beats[i + 1].Cell) ? beats[i + 1].CenterX - 6 : null;
        var track = _host.Track;
        if (track is null || measureIndex + 1 >= track.Measures.Count) return null;
        var scoreLayout = _layout.GetLayout(track);
        if (scoreLayout.SystemForMeasure(measureIndex + 1) != scoreLayout.SystemForMeasure(measureIndex)) return null;
        var next = VoiceCells(track.Measures[measureIndex + 1], voice);
        for (var c = 0; c < next.Count; c++)
            if (next[c].Notes.Count > 0 || next[c].IsRest) return HasVibrato(next[c]) ? CellCenterX(measureIndex + 1, c, voice) - 6 : null;
        return null;
    }

    // Wavy vibrato line (a sawtooth-sine polyline), thicker and taller for wide vibrato.
    private static void DrawVibratoLine(IDrawContext d, double left, double right, double y, bool wide, UiColor ink)
    {
        if (right <= left + 4) return;
        var amplitude = wide ? 3.2 : 2.0;
        var period = wide ? 8.0 : 6.5;
        var geometry = d.Paths.BuildPath(g =>
        {
            // The phase follows the page x, so the pieces of one run of vibrato notes join into a single wave.
            g.BeginFigure(new UiPoint(left, y - Math.Sin(left / period * 2 * Math.PI) * amplitude), false, false);
            for (var px = left + 1; px <= right; px += 1)
                g.LineTo(new UiPoint(px, y - Math.Sin(px / period * 2 * Math.PI) * amplitude), smoothJoin: true);
        });
        d.Path(null, new UiStroke(ink, wide ? 2.2 : 1.5, RoundJoins: true), geometry);
    }

    /// <summary>Draws a clef with its left edge at <paramref name="x"/>; <paramref name="size"/> 22 is the system clef, about 15 the mid-system change.</summary>
    private void DrawClef(IDrawContext d, string? clef, double x, double staffTop, double size, UiColor ink)
    {
        var (shape, eightBelow) = ScoreClefKey.ClefShapeOf(clef);
        var scale = size / 22.0;
        var brush = ink;
        // Offsets of the glyph box from the staff top at size 22, so the clef's curl / dots / centre sit on the right line.
        // Anchor: the staff line the clef marks (G, F, C or middle line, counted from the top); a smaller clef keeps that line, not the staff top.
        var (glyph, dy, anchor) = shape switch
        {
            ClefShape.Bass => ("\U0001D122", -8.0, 1.0),
            ClefShape.Alto => ("\U0001D121", -3.0, 2.0),
            ClefShape.Tenor => ("\U0001D121", -12.0, 1.0),
            ClefShape.Percussion => ("\U0001D125", 3.0, 2.0),
            _ => ("\U0001D11E", -6.0, 3.0)
        };
        var line = anchor * 9.0;   // at size 22 the offsets above were set against the 9 px staff gap
        ScoreText.Draw(d, glyph, x, staffTop + line - (line - dy) * scale, size, brush);
        if (eightBelow && shape is ClefShape.Treble or ClefShape.Bass)
            ScoreText.DrawCentered(d, "8", x + 7 * scale, staffTop + 4 * _host.StaffGap + 8, 8 * scale + 1, brush);
    }
}
