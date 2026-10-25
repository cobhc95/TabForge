using System.Globalization;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationLayoutBuilder;

namespace TabForge.Views;

// Owns: the staff drawing of one measure: DrawMeasure and the stacked text, octave, beat, tremolo, stem, flag, beam and tuplet marks.
// Does not own: the note heads (StaffNotationDrawing.Noteheads.cs), the layout (StaffNotationLayoutBuilder) or the geometry (StaffNotationGeometry).
// Tests: no dedicated test class.
// Length: over 300 lines because the per-measure engraving steps (beams, stems, flags, tuplets) share one measure layout.

/// <summary>Draws glyphs and the already-resolved geometry of a measure layout; no rhythmic decisions happen here.</summary>
internal static partial class StaffNotationDrawing
{
    /// <summary>A centred text mark claimed in the skyline; the glyph is drawn inside its box.</summary>
    internal static void DrawStackedText(IDrawContext dc, StaffNotationMeasureLayout layout, string text, double size, int? weight,
        double cx, double distance, UiColor colour)
    {
        var ft = MakeText(dc, text, size, colour, weight);
        var h = size * 0.95;
        var top = PlaceMark(layout, cx - ft.Width / 2, cx + ft.Width / 2, h, distance);
        ScoreText.DrawFormatted(dc, ft, cx - ft.Width / 2, top + h / 2 - ft.Height / 2);
    }

    /// <summary>A centred text mark stacked below the staff (after the dynamics row).</summary>
    internal static void DrawStackedBelow(IDrawContext dc, StaffNotationMeasureLayout layout, string text, double size, int? weight,
        double cx, double distance, UiColor colour)
    {
        var ft = MakeText(dc, text, size, colour, weight);
        var h = size * 0.95;
        var top = layout.Skyline.PlaceBelow(cx - ft.Width / 2, cx + ft.Width / 2, h, layout.StaffTop + 4 * StaffGap + distance);
        ScoreText.DrawFormatted(dc, ft, cx - ft.Width / 2, top + h / 2 - ft.Height / 2);
    }

    /// <summary>Draws glyphs and the already-resolved geometry; no rhythmic decisions happen here.</summary>
    internal static void DrawMeasure(
        IDrawContext dc,
        StaffNotationMeasureLayout layout,
        int measureIndex,
        UiColor ink,
        UiColor faint,
        UiColor accent,
        UiColor playColor,
        UiColor paper,
        UiColor staffLineColor,
        LedgerLineMode ledgerLineMode,
        IReadOnlySet<(int bar, int cell, int s)> sounding,
        IReadOnlySet<(int bar, int cell, int s)> struck)
    {
        if (Math.Abs(layout.StaffScale - 1.0) > 0.001)
            dc.PushScale(1, layout.StaffScale, 0, layout.StaffTop);
        var engravingInk = EngravingInkColor(ink, paper);

        DrawLedgerLines(dc, layout, staffLineColor, ledgerLineMode);
        DrawTuplets(dc, layout, engravingInk);   // first: the brackets sit next to the notation, every other mark stacks outside them

        foreach (var beat in layout.Beats)
        {
            if (beat.IsRest)
            {
                DrawRest(dc, beat.Cell, beat.CenterX, layout.StaffTop, engravingInk);
                if (beat.Cell.Fermata) DrawStackedText(dc, layout, layout.IsSecondVoice ? "𝄑" : "𝄐", 12, null, beat.CenterX, 8, engravingInk);   // a held rest keeps its fermata (voice 2: inverted, below)
                continue;
            }

            if (beat.IsDrum)
            {
                DrawDrumHeads(dc, beat, measureIndex, engravingInk, playColor, paper, sounding, struck);
                continue;
            }

            DrawNoteHeads(dc, layout, beat, measureIndex, engravingInk, playColor, paper, sounding, struck);
            DrawGhostBrackets(dc, layout, beat, engravingInk);
            DrawGraceNotes(dc, beat, engravingInk, GhostRoom(layout.StaffTop, beat));
            var harmonicCaption = beat.Notes.Select(n => ScoreMarkText.HarmonicCaption(n.Source.Techniques)).FirstOrDefault(c => c.Length > 0);
            if (!string.IsNullOrEmpty(harmonicCaption) && !layout.HarmonicCaptionsOnTab) // notation only: the caption sits below the staff
                DrawStackedBelow(dc, layout, harmonicCaption, 8.5, 600, beat.CenterX, 8, engravingInk);
            DrawBeatMarks(dc, layout, beat, engravingInk);
            DrawArticulations(dc, layout, beat, engravingInk);
        }

        DrawStemsAndFlags(dc, layout, engravingInk);
        DrawBeams(dc, layout, engravingInk);
        DrawTremoloSlashes(dc, layout, engravingInk);
        DrawOctaveMarkings(dc, layout, engravingInk);
        DrawTies(dc, layout.Ties, engravingInk, layout.ContentLeft);
        DrawHopoSlurs(dc, layout.HopoSlurs, engravingInk);
        DrawSlideStrokes(dc, layout.Slides, engravingInk);
        if (Math.Abs(layout.StaffScale - 1.0) > 0.001) dc.Pop();
    }

    /// <summary>The note heads of a beat (dead, harmonic, open or solid), with the playing halo, augmentation dots and accidentals.</summary>
    private static void DrawNoteHeads(IDrawContext dc, StaffNotationMeasureLayout layout, StaffNotationBeat beat, int measureIndex, UiColor engravingInk, UiColor playColor, UiColor paper, IReadOnlySet<(int bar, int cell, int s)> sounding, IReadOnlySet<(int bar, int cell, int s)> struck)
    {
        foreach (var note in beat.Notes)
        {
            var isSounding = sounding.Contains((measureIndex, beat.CellIndex, note.Source.StringIndex));
            var isStruck = struck.Contains((measureIndex, beat.CellIndex, note.Source.StringIndex));
            var notePlaybackColor = playColor == default ? Palette.Get("FollowHighlightText") : playColor;
            var noteColour = isSounding ? notePlaybackColor : engravingInk;

            if (isSounding)
                dc.Ellipse(new UiColor(ScorePlayedChip.HaloAlpha(ScorePlayedChip.IsLight(paper), isStruck),
                        notePlaybackColor.R, notePlaybackColor.G, notePlaybackColor.B), (UiStroke?)null, note.X, note.Y, isStruck ? 11 : 9.4, isStruck ? 9 : 7.6);

            var open = NormalizeDuration(beat.Cell.DurationDenominator) <= 2;
            dc.PushRotate(-20, note.X, note.Y);
            if (note.Source.Dead)
            {
                dc.PushRotate(20, note.X, note.Y);   // the reference's x head stands upright, in the notehead's place
                DrawDeadNoteHead(dc, note.X, note.Y, noteColour);
                dc.Pop();
            }
            else if (HasHarmonic(note.Source.Techniques))
            {
                var (headX, headY) = (note.X, note.Y); // a diamond notehead; the lambda captures these, not the loop's note, so other notes allocate no closure
                var d = dc.Paths.BuildPath(g =>
                {
                    g.BeginFigure(new UiPoint(headX - HeadRadiusX, headY), true, true);
                    g.LineTo(new UiPoint(headX, headY - HeadRadiusY - 1.2));
                    g.LineTo(new UiPoint(headX + HeadRadiusX, headY));
                    g.LineTo(new UiPoint(headX, headY + HeadRadiusY + 1.2));
                });
                dc.Path(noteColour, new UiStroke(noteColour, 1.2), d); // The standard harmonics are solid diamonds
            }
            else if (open)
            {
                // Mask the staff line under hollow heads, as a notation glyph should.
                dc.Ellipse(paper, (UiStroke?)null, note.X, note.Y, HeadRadiusX, HeadRadiusY);
                dc.Ellipse((UiColor?)null, new UiStroke(noteColour, 1.35), note.X, note.Y, HeadRadiusX, HeadRadiusY);
            }
            else dc.Ellipse(noteColour, (UiStroke?)null, note.X, note.Y, HeadRadiusX, HeadRadiusY);
            dc.Pop();

            DrawAugmentationDots(dc, beat.Cell, note, noteColour);
            if (note.Accidental is not null)
                DrawCentered(dc, note.Accidental, AccidentalX(layout, beat, note),
                    note.Y, 15, noteColour);
        }
    }

    /// <summary>Ghost notes: one pair of brackets around each cluster of touching ghost heads.</summary>
    private static void DrawGhostBrackets(IDrawContext dc, StaffNotationMeasureLayout layout, StaffNotationBeat beat, UiColor engravingInk)
    {
        foreach (var c in GhostClusters(layout, beat)) // ghost notes: one pair of brackets around each cluster of touching ghost heads
        {
            // The 13 px bracket is stretched vertically (a bigger font would also widen it into the heads and accidentals).
            dc.PushScale(1, c.Half / GhostInkHalf, 0, c.InkY);
            Draw(dc, "(", c.L - HeadRadiusX - GhostOpenGap - c.Pad, c.InkY - GhostInkCentre, 13, engravingInk);   // closer to the heads: clears accidentals and the previous beat's stem
            Draw(dc, ")", c.R + HeadRadiusX + 1 + c.Pad, c.InkY - GhostInkCentre, 13, engravingInk);
            dc.Pop();
        }
    }

    /// <summary>Accent, staccato, tenuto and fermata marks of a beat.</summary>
    private static void DrawArticulations(IDrawContext dc, StaffNotationMeasureLayout layout, StaffNotationBeat beat, UiColor engravingInk)
    {
        if (beat.Cell.Accent != 0)
        {
            // One mark above the notes: ">" accent, "^" heavy accent (marcato).
            DrawStackedText(dc, layout, beat.Cell.Accent == 2 ? "^" : ">", 10, 700, beat.CenterX, 8, engravingInk);
        }
        if ((beat.Cell.Staccato || beat.Cell.Tenuto) && beat.Notes.Count > 0)
        {
            // Beside the notehead on the side away from the stem, tenuto stacked outside the dot.
            var below = !beat.HasStem || beat.StemUp;
            var headY = below ? beat.MaxY + 9 : beat.MinY - 9;
            var step = below ? 6.0 : -6.0;
            var dotPen = new UiStroke(engravingInk, 1.6);
            if (beat.Cell.Staccato) { dc.Ellipse(engravingInk, (UiStroke?)null, beat.CenterX, headY, 1.5, 1.5); headY += step; }
            if (beat.Cell.Tenuto) dc.Line(dotPen, beat.CenterX - 3.5, headY, beat.CenterX + 3.5, headY);
        }
        if (beat.Cell.Fermata) DrawStackedText(dc, layout, layout.IsSecondVoice ? "𝄑" : "𝄐", 12, null, beat.CenterX, 8, engravingInk);   // two voices: upright above and inverted below, as engraved
    }

    internal static void DrawOctaveMarkings(IDrawContext dc, StaffNotationMeasureLayout layout, UiColor colour)
    {
        var linePen = new UiStroke(colour, 0.9);
        StaffNotationBeat? first = null;
        StaffNotationBeat? last = null;
        void Finish()
        {
            if (first is null || last is null) return;
            var left = first.CenterX - 5;
            var right = last.CenterX + Math.Max(7, last.DurationSlots * layout.SlotWidth / 2);
            var label = first.Cell.OctaveShiftSemitones switch
            {
                12 => "8va", -12 => "8vb", 24 => "15ma", _ => "15mb"
            };
            var labelLeft = Math.Max(left, layout.OctaveLabelMinX);
            var labelWidth = MakeText(dc, label, 8.5, colour, 600).Width;
            var boxTop = PlaceMark(layout, left - 2, Math.Max(right, labelLeft + labelWidth), 20, 6) + 3;   // the caption (drawn 1.5 px above the line row, taller than its box) is inside the claim
            var y = boxTop + 13;
            Draw(dc, label, labelLeft, boxTop - 1.5, 8.5, colour, 600);
            dc.Line(linePen, left, y, right, y);
            dc.Line(linePen, left, y, left, y + 4);
            dc.Line(linePen, right, y, right, y + 4);
            first = null;
            last = null;
        }
        foreach (var beat in layout.Beats)
        {
            if (beat.Cell.OctaveShiftSemitones is not (-24 or -12 or 12 or 24)) continue;
            var contiguous = last is not null && last.Cell.OctaveShiftSemitones == beat.Cell.OctaveShiftSemitones &&
                            beat.StartSlots <= last.StartSlots + last.DurationSlots + PositionEpsilon;
            if (!contiguous) Finish();
            first ??= beat;
            last = beat;
        }
        Finish();
    }

    /// <summary>Wavy arpeggio line / straight colour arrow left of a chord, trill "tr" + wave, wah +/o, above-staff marks.</summary>
    internal static void DrawBeatMarks(IDrawContext dc, StaffNotationMeasureLayout layout, StaffNotationBeat beat, UiColor colour)
    {
        var techniques = beat.Cell.Notes.SelectMany(n => n.Techniques).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (beat.Notes.Count > 0 && (techniques.Contains("ArpeggioDown") || techniques.Contains("ArpeggioUp") ||
                                     techniques.Contains("BrushDown") || techniques.Contains("BrushUp")))
        {
            var wavy = techniques.Contains("ArpeggioDown") || techniques.Contains("ArpeggioUp");
            var down = techniques.Contains("ArpeggioDown") || techniques.Contains("BrushDown");
            var x = beat.CenterX - 13 - (beat.Notes.Any(n => n.Accidental is not null) ? 8 : 0);
            var y1 = beat.Notes.Min(n => n.Y) - 4;
            var y2 = Math.Max(beat.Notes.Max(n => n.Y) + 4, y1 + 9);
            var pen = new UiStroke(colour, 1.0);
            if (wavy)
            {
                var g = dc.Paths.BuildPath(c =>
                {
                    c.BeginFigure(new UiPoint(x, y1), false, false);
                    for (var y = y1; y < y2; y += 3.5) c.LineTo(new UiPoint(x + (((int)((y - y1) / 3.5) % 2) == 0 ? 1.8 : -1.8), y + 1.75));
                });
                dc.Path((UiColor?)null, pen, g);
            }
            else dc.Line(pen, x, y1, x, y2);
            var tipY = down ? y2 + 1.5 : y1 - 1.5;
            var dir = down ? 1 : -1;
            var head = dc.Paths.BuildPath(c =>
            {
                c.BeginFigure(new UiPoint(x, tipY), true, true);
                c.LineTo(new UiPoint(x - 2.6, tipY - dir * 4.2));
                c.LineTo(new UiPoint(x + 2.6, tipY - dir * 4.2));
            });
            dc.Path(colour, (UiStroke?)null, head);
        }
        if (techniques.Contains("Trill"))
        {
            var trWidth = MakeText(dc, "tr", 9, colour, 600).Width;
            var trTop = PlaceMark(layout, beat.CenterX - 5 - trWidth / 2, beat.CenterX + 4 + 8 * 2.6 + 1, 15, 6);
            DrawCentered(dc, "tr", beat.CenterX - 5, trTop + 4.5, 9, colour, 600);
            var (trillX, y) = (beat.CenterX + 4, trTop + 12); // captured instead of the parameter, so beats without a trill allocate no closure
            var g = dc.Paths.BuildPath(c =>
            {
                c.BeginFigure(new UiPoint(trillX, y), false, false);
                for (var i = 1; i <= 8; i++) c.LineTo(new UiPoint(trillX + i * 2.6, y + (i % 2 == 0 ? 0 : -2)));
            });
            dc.Path((UiColor?)null, new UiStroke(colour, 0.9), g);
        }
        if (techniques.Contains("WahClose") || techniques.Contains("WahOpen"))
        {
            DrawStackedText(dc, layout, techniques.Contains("WahClose") ? "+" : "o", 10, 700, beat.CenterX, 8, colour);
            // The words appear where the pedal turns on, not on every beat of a run.
            var at = layout.Beats.ToList().IndexOf(beat);
            var previousHasWah = at > 0 && layout.Beats[at - 1].Cell.Notes.Any(n => n.Techniques.Contains("WahClose") || n.Techniques.Contains("WahOpen"));
            if (!previousHasWah) DrawStackedBelow(dc, layout, "Wah-wah on", 8.5, null, beat.CenterX, 8, colour);
        }
        if (techniques.Contains("Tapping") || techniques.Contains("LeftTap"))
        {
            // Tapped notes carry a "+" above the notation.
            DrawStackedText(dc, layout, "+", 10, 700, beat.CenterX, 8, colour);
        }
    }

    /// <summary>Tremolo picking: 1-3 slashes across the stem (above the head when the note has no stem).</summary>
    internal static void DrawTremoloSlashes(IDrawContext dc, StaffNotationMeasureLayout layout, UiColor colour)
    {
        var pen = new UiStroke(colour, 1.7);
        foreach (var beat in layout.Beats)
        {
            var count = ScoreMarkText.TremoloSlashCount(beat.Cell);
            if (count == 0 || beat.IsRest || beat.IsDrum) continue;
            double cx, cy;
            if (beat.HasStem) { cx = beat.StemX; cy = (beat.StemStartY + beat.StemEndY) / 2 + (beat.Flags > 0 ? (beat.StemUp ? 3 : -3) : 0); }
            else { cx = beat.CenterX; cy = beat.MinY - 12; }
            for (var k = 0; k < count; k++)
            {
                var y = cy + (k - (count - 1) / 2.0) * 3.4;
                dc.Line(pen, cx - 4.8, y + 2.2, cx + 4.8, y - 2.2);
            }
        }
    }

    internal static void DrawStemsAndFlags(IDrawContext dc, StaffNotationMeasureLayout layout, UiColor colour)
    {
        var stemPen = new UiStroke(colour, 0.9);
        foreach (var beat in layout.Beats)
        {
            if (!beat.HasStem || beat.IsDrum) continue;   // a percussion staff draws its stems below
            dc.Line(stemPen, beat.StemX, beat.StemStartY, beat.StemX, beat.StemEndY);
            if (beat.LowerStemTopY is { } lowerTop && beat.BeamGroupIndex < 0)
                dc.Line(stemPen, beat.CenterX - 5, lowerTop, beat.CenterX - 5, beat.LowerStemEndY);
            if (beat.BeamGroupIndex < 0) DrawFlags(dc, beat, colour);
        }
        foreach (var stem in layout.DrumStems)
        {
            dc.Line(stemPen, stem.StemX, stem.StemStartY, stem.StemX, stem.StemEndY);
            if (stem.BeamGroupIndex < 0) DrawFlags(dc, stem, colour);
        }
    }

    internal static void DrawFlags(IDrawContext dc, StaffNotationBeat beat, UiColor colour)
    {
        for (var f = 0; f < beat.Flags; f++)
        {
            var y = beat.StemUp ? beat.StemEndY + f * FlagGap : beat.StemEndY - f * FlagGap;
            dc.Path(colour, (UiStroke?)null, ScorePaths.FlagGeometry(dc.Paths, beat.StemX, y, beat.StemUp));
        }
    }

    internal static void DrawBeams(IDrawContext dc, StaffNotationMeasureLayout layout, UiColor colour)
    {
        var pen = new UiStroke(colour, BeamThickness);
        foreach (var beam in layout.BeamSegments)
            dc.Line(pen, beam.X1, beam.Y1, beam.X2, beam.Y2);
    }

    internal static void DrawTuplets(IDrawContext dc, StaffNotationMeasureLayout layout, UiColor colour)
    {
        var sky = layout.Skyline;
        foreach (var tuplet in layout.TupletGroups)
        {
            var first = tuplet.Beats[0];
            var last = tuplet.Beats[^1];
            var up = first.StemUp;
            var numberText = tuplet.Numerator.ToString(CultureInfo.InvariantCulture);
            if (tuplet.IsBeamed)
            {
                var beam = layout.BeamGroups[tuplet.BeamGroupIndex];
                var edgeMin = double.PositiveInfinity;
                var edgeMax = double.NegativeInfinity;
                foreach (var beat in tuplet.Beats)
                {
                    var y = beam.BaseYAt(beat.StemX);
                    edgeMin = Math.Min(edgeMin, y);
                    edgeMax = Math.Max(edgeMax, y);
                }
                // The reference brackets beamed tuplets too: the number sits in a gap of the bracket line. The whole
                // bracket claims its box, stacked outside the beams, stems and heads of the beats it spans.
                var x0 = first.StemX - 2; var x1 = last.StemX + 2;
                var mid = (first.StemX + last.StemX) / 2;
                var tick = up ? 4.0 : -4.0;
                var blockTop = up ? sky.PlaceAbove(x0, x1, 15, edgeMin - 1) : sky.PlaceBelow(x0, x1, 15, edgeMax + 1);
                var numberY = up ? blockTop + 4.5 : blockTop + 10.5;
                var lineY = up ? blockTop + 10.5 : blockTop + 4.5;
                var bracketPen = new UiStroke(colour, 0.9);
                // An incomplete group (fewer beats than its number) or one too short for the line on both sides of the
                // number: the reference shows the number alone.
                if (tuplet.Beats.Count >= tuplet.Numerator && x1 - x0 >= 24)
                {
                    dc.Line(bracketPen, x0, lineY, Math.Max(x0, mid - 8), lineY);
                    dc.Line(bracketPen, Math.Min(x1, mid + 8), lineY, x1, lineY);
                    dc.Line(bracketPen, x0, lineY, x0, lineY + tick);
                    dc.Line(bracketPen, x1, lineY, x1, lineY + tick);
                }
                DrawCentered(dc, numberText, mid, numberY, 11, colour, 600);
            }
            else
            {
                var top = tuplet.Beats.Min(b => b.MinY);
                var bottom = tuplet.Beats.Max(b => b.MaxY);
                var left = first.CenterX;
                var right = last.CenterX;
                if (right - left < 10) { left -= 5; right += 5; }
                var blockTop = up ? sky.PlaceAbove(left, right, 16, top - 1) : sky.PlaceBelow(left, right, 16, bottom + 1);
                var lineY = up ? blockTop + 11 : blockTop + 5;
                var tickY = up ? lineY + 5 : lineY - 5;
                var pen = new UiStroke(colour, 1);
                if (tuplet.Beats.Count > 1 && tuplet.Beats.Count >= tuplet.Numerator)   // an incomplete group: the number alone, as in the reference
                {
                    dc.Line(pen, left, lineY, right, lineY);
                    dc.Line(pen, left, lineY, left, tickY);
                    dc.Line(pen, right, lineY, right, tickY);
                }
                DrawCentered(dc, numberText, (left + right) / 2, up ? blockTop + 4.5 : blockTop + 11.5, 11, colour, 600);
            }
        }
    }

    /// <summary>The pen for staff lines AND ledger lines: the staff-line colour (which already carries the staff-line opacity), one thickness.</summary>
    internal static UiStroke StaffLineStroke(UiColor staffLineColor) => new(staffLineColor, StaffLineThickness);

    internal static UiColor EngravingInkColor(UiColor ink, UiColor paper)
    {
        const double soften = 0.14;
        static byte Mix(byte foreground, byte background, double amount)
            => (byte)Math.Round(foreground * (1 - amount) + background * amount);
        return new UiColor(ink.A,
            Mix(ink.R, paper.R, soften), Mix(ink.G, paper.G, soften), Mix(ink.B, paper.B, soften));
    }

    internal static void DrawLedgerLines(IDrawContext dc, StaffNotationMeasureLayout layout,
        UiColor staffLineColor, LedgerLineMode mode)
    {
        if (mode == LedgerLineMode.Hidden) return;
        // Exactly the staff lines' pen: ledger lines are an extension of the staff and always match it.
        var pen = StaffLineStroke(staffLineColor);
        foreach (var line in LedgerLineSegments(layout, mode))
            dc.Line(pen, line.X1, line.Y, line.X2, line.Y);
    }

    internal static void DrawTies(IDrawContext dc, IReadOnlyList<StaffNotationTie> ties, UiColor colour, double contentLeft = double.NegativeInfinity)
    {
        foreach (var tie in ties)
        {
            // A stub reaching back over the key / time signature is left to the previous bar's outgoing stub.
            var edge = tie.TowardLeft ? Math.Max(tie.X2, contentLeft + 2) : tie.X2;
            if (tie.IsStub && tie.TowardLeft && edge > tie.X1 - 13) continue;
            if (tie.IsStub) DrawTieStub(dc, tie.X1, tie.Y1, edge, tie.TowardLeft, tie.Above, colour);
            else DrawTie(dc, tie.X1, tie.Y1, tie.X2, tie.Y2, tie.Above, colour, tie.StartInset, tie.EndInset);
        }
    }

    internal static void DrawTie(IDrawContext dc, double x1, double y1, double x2, double y2, bool above, UiColor colour, double startInset = 5, double endInset = 5)
    {
        var dir = above ? -1.0 : 1.0;
        var y1b = y1 + dir * 6;
        var y2b = y2 + dir * 6;
        var (sx, c1, c2, ex, span) = ArcShape(x1, x2, startInset, endInset);
        var bow = Math.Clamp(span * 0.10 + 4, 5, 14);
        var geometry = dc.Paths.BuildPath(c =>
        {
            c.BeginFigure(new UiPoint(sx, y1b), true, false);
            c.BezierTo(new UiPoint(c1, y1b + dir * bow), new UiPoint(c2, y2b + dir * bow), new UiPoint(ex, y2b));
        });
        dc.Path((UiColor?)null, new UiStroke(colour, 1.2), geometry);
    }

    /// <summary>Half a tie: from the note to <paramref name="edgeX"/> (the barline or the system edge), ending level at the arc's
    /// peak, so the halves on both sides of a barline join into one arc.</summary>
    internal static void DrawTieStub(IDrawContext dc, double x, double y, double edgeX, bool towardLeft, bool above, UiColor colour)
    {
        var dir = above ? -1.0 : 1.0;
        var sign = towardLeft ? -1.0 : 1.0;
        var yb = y + dir * 6;
        var sx = x + sign * 5;
        var span = Math.Max(8, sign * (edgeX - sx)) * sign;
        var peak = yb + dir * 7;
        var geometry = dc.Paths.BuildPath(c =>
        {
            c.BeginFigure(new UiPoint(sx, yb), true, false);
            c.BezierTo(new UiPoint(sx + span * 0.3, yb + dir * 4.5), new UiPoint(sx + span * 0.65, peak), new UiPoint(sx + span, peak));
        });
        dc.Path((UiColor?)null, new UiStroke(colour, 1.2), geometry);
    }

    internal static void DrawSlideStrokes(IDrawContext dc, IReadOnlyList<StaffNotationSlideStroke> slides, UiColor colour)
    {
        if (slides.Count == 0) return;
        var pen = new UiStroke(colour, 1.1);
        foreach (var slide in slides) dc.Line(pen, slide.X1, slide.Y1, slide.X2, slide.Y2);
    }

    internal static void DrawHopoSlurs(IDrawContext dc, IReadOnlyList<StaffNotationSlur> slurs, UiColor colour)
    {
        var pen = new UiStroke(colour, 1.1);
        foreach (var slur in slurs)
        {
            var direction = slur.StemsUp ? 1.0 : -1.0;
            var (sx, c1, c2, ex, span) = ArcShape(slur.X1, slur.X2, slur.StartInset, slur.EndInset);
            var start = new UiPoint(sx, slur.Y1 + direction * 6);
            var end = new UiPoint(ex, slur.Y2 + direction * 6);
            var bow = Math.Clamp(span * 0.10 + 4, 5, 12);
            var geometry = dc.Paths.BuildPath(c =>
            {
                c.BeginFigure(start, true, false);
                c.BezierTo(new UiPoint(c1, start.Y + direction * bow), new UiPoint(c2, end.Y + direction * bow), end);
            });
            dc.Path((UiColor?)null, pen, geometry);
        }
    }

    internal static void DrawCentered(IDrawContext dc, string text, double cx, double cy, double size, UiColor colour,
        int? weight = null)
    {
        var formatted = MakeText(dc, text, size, colour, weight);
        ScoreText.DrawFormatted(dc, formatted, cx - formatted.Width / 2, cy - formatted.Height / 2);
    }

    internal static void Draw(IDrawContext dc, string text, double x, double y, double size, UiColor colour,
        int? weight = null) => ScoreText.DrawFormatted(dc, MakeText(dc, text, size, colour, weight), x, y);

    internal static UiText MakeText(IDrawContext dc, string text, double size, UiColor colour, int? weight) =>
        ScoreText.MakeText(dc, text, size, colour, weight, "Segoe UI Symbol");

}
