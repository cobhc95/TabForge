using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationLayoutBuilder;

namespace TabForge.Views;

/// <summary>Draws glyphs and the already-resolved geometry of a measure layout; no rhythmic decisions happen here.</summary>
internal static class StaffNotationDrawing
{
    /// <summary>A centred text mark claimed in the skyline; the glyph is drawn inside its box.</summary>
    internal static void DrawStackedText(DrawingContext dc, StaffNotationMeasureLayout layout, string text, double size, FontWeight? weight,
        double cx, double distance, Brush brush)
    {
        var ft = MakeText(text, size, brush, weight);
        var h = size * 0.95;
        var top = PlaceMark(layout, cx - ft.Width / 2, cx + ft.Width / 2, h, distance);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, top + h / 2 - ft.Height / 2));
    }

    /// <summary>A centred text mark stacked below the staff (after the dynamics row).</summary>
    internal static void DrawStackedBelow(DrawingContext dc, StaffNotationMeasureLayout layout, string text, double size, FontWeight? weight,
        double cx, double distance, Brush brush)
    {
        var ft = MakeText(text, size, brush, weight);
        var h = size * 0.95;
        var top = layout.Skyline.PlaceBelow(cx - ft.Width / 2, cx + ft.Width / 2, h, layout.StaffTop + 4 * StaffGap + distance);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, top + h / 2 - ft.Height / 2));
    }

    /// <summary>Draws glyphs and the already-resolved geometry; no rhythmic decisions happen here.</summary>
    internal static void DrawMeasure(
        DrawingContext dc,
        StaffNotationMeasureLayout layout,
        int measureIndex,
        Color ink,
        Color faint,
        Color accent,
        Color playColor,
        Color paper,
        Color staffLineColor,
        LedgerLineMode ledgerLineMode,
        IReadOnlySet<(int bar, int cell, int s)> sounding,
        IReadOnlySet<(int bar, int cell, int s)> struck)
    {
        if (Math.Abs(layout.StaffScale - 1.0) > 0.001)
            dc.PushTransform(new ScaleTransform(1, layout.StaffScale, 0, layout.StaffTop));
        var engravingInk = EngravingInkColor(ink, paper);
        var inkBrush = RenderDraw.Solid(engravingInk);
        var faintBrush = RenderDraw.Solid(faint);
        var paperBrush = RenderDraw.Solid(paper);

        DrawLedgerLines(dc, layout, staffLineColor, ledgerLineMode);
        DrawTuplets(dc, layout, inkBrush);   // first: the brackets sit next to the notation, every other mark stacks outside them

        foreach (var beat in layout.Beats)
        {
            if (beat.IsRest)
            {
                DrawRest(dc, beat.Cell, beat.CenterX, layout.StaffTop, inkBrush);
                if (beat.Cell.Fermata) DrawStackedText(dc, layout, layout.IsSecondVoice ? "𝄑" : "𝄐", 12, null, beat.CenterX, 8, inkBrush);   // a held rest keeps its fermata (voice 2: inverted, below)
                continue;
            }

            if (beat.IsDrum)
            {
                DrawDrumHeads(dc, beat, measureIndex, engravingInk, playColor, paper, sounding, struck);
                continue;
            }

            foreach (var note in beat.Notes)
            {
                var isSounding = sounding.Contains((measureIndex, beat.CellIndex, note.Source.StringIndex));
                var isStruck = struck.Contains((measureIndex, beat.CellIndex, note.Source.StringIndex));
                var notePlaybackColor = playColor == default ? Color.FromRgb(0x3F, 0xB9, 0x50) : playColor;
                var playBrush = RenderDraw.Solid(notePlaybackColor);
                var noteBrush = isSounding
                    ? playBrush
                    : RenderDraw.Solid(engravingInk);

                if (isSounding)
                    dc.DrawEllipse(RenderDraw.Solid(Color.FromArgb(isStruck ? (byte)85 : (byte)42,
                            notePlaybackColor.R, notePlaybackColor.G, notePlaybackColor.B)),
                        null, new Point(note.X, note.Y), isStruck ? 11 : 9.4, isStruck ? 9 : 7.6);

                var open = NormalizeDuration(beat.Cell.DurationDenominator) <= 2;
                dc.PushTransform(new RotateTransform(-20, note.X, note.Y));
                if (note.Source.Dead)
                {
                    dc.PushTransform(new RotateTransform(20, note.X, note.Y));   // the reference's x head stands upright, in the notehead's place
                    DrawDeadNoteHead(dc, note.X, note.Y, noteBrush);
                    dc.Pop();
                }
                else if (HasHarmonic(note.Source.Techniques))
                {
                    // Harmonics use a diamond notehead (hollow for open durations).
                    var d = new StreamGeometry();
                    using (var g = d.Open())
                    {
                        g.BeginFigure(new Point(note.X - HeadRadiusX, note.Y), true, true);
                        g.LineTo(new Point(note.X, note.Y - HeadRadiusY - 1.2), true, false);
                        g.LineTo(new Point(note.X + HeadRadiusX, note.Y), true, false);
                        g.LineTo(new Point(note.X, note.Y + HeadRadiusY + 1.2), true, false);
                    }
                    d.Freeze();
                    dc.DrawGeometry(noteBrush, RenderDraw.Pen(noteBrush, 1.2), d); // The standard harmonics are solid diamonds
                }
                else if (open)
                {
                    // Mask the staff line under hollow heads, as a notation glyph should.
                    dc.DrawEllipse(paperBrush, null, new Point(note.X, note.Y), HeadRadiusX, HeadRadiusY);
                    dc.DrawEllipse(null, RenderDraw.Pen(noteBrush, 1.35), new Point(note.X, note.Y), HeadRadiusX, HeadRadiusY);
                }
                else dc.DrawEllipse(noteBrush, null, new Point(note.X, note.Y), HeadRadiusX, HeadRadiusY);
                dc.Pop();

                DrawAugmentationDots(dc, beat.Cell, note, noteBrush);
                if (note.Accidental is not null)
                    DrawCentered(dc, note.Accidental, AccidentalX(layout, beat, note),
                        note.Y, 15, noteBrush);
            }

            foreach (var c in GhostClusters(layout, beat)) // ghost notes: one pair of brackets around each cluster of touching ghost heads
            {
                // The 13 px bracket is stretched vertically (a bigger font would also widen it into the heads and accidentals).
                dc.PushTransform(new ScaleTransform(1, c.Half / GhostInkHalf, 0, c.InkY));
                Draw(dc, "(", c.L - HeadRadiusX - GhostOpenGap - c.Pad, c.InkY - GhostInkCentre, 13, inkBrush);   // closer to the heads: clears accidentals and the previous beat's stem
                Draw(dc, ")", c.R + HeadRadiusX + 1 + c.Pad, c.InkY - GhostInkCentre, 13, inkBrush);
                dc.Pop();
            }
            DrawGraceNotes(dc, beat, inkBrush, GhostRoom(layout.StaffTop, beat));
            var harmonicCaption = beat.Notes.Select(n => ScoreMarkText.HarmonicCaption(n.Source.Techniques)).FirstOrDefault(c => c.Length > 0);
            if (!string.IsNullOrEmpty(harmonicCaption) && !layout.HarmonicCaptionsOnTab) // notation only: the caption sits below the staff
                DrawStackedBelow(dc, layout, harmonicCaption, 8.5, FontWeights.SemiBold, beat.CenterX, 8, inkBrush);
            DrawBeatMarks(dc, layout, beat, inkBrush);

            var articulationY = layout.StaffTop + 4 * StaffGap + 4;
            if (beat.Cell.Accent != 0)
            {
                // One mark above the notes: ">" accent, "^" heavy accent (marcato).
                DrawStackedText(dc, layout, beat.Cell.Accent == 2 ? "^" : ">", 10, FontWeights.Bold, beat.CenterX, 8, inkBrush);
            }
            if ((beat.Cell.Staccato || beat.Cell.Tenuto) && beat.Notes.Count > 0)
            {
                // Beside the notehead on the side away from the stem, tenuto stacked outside the dot.
                var below = !beat.HasStem || beat.StemUp;
                var headY = below ? beat.MaxY + 9 : beat.MinY - 9;
                var step = below ? 6.0 : -6.0;
                var dotPen = RenderDraw.Pen(inkBrush, 1.6);
                if (beat.Cell.Staccato) { dc.DrawEllipse(inkBrush, null, new Point(beat.CenterX, headY), 1.5, 1.5); headY += step; }
                if (beat.Cell.Tenuto) dc.DrawLine(dotPen, new Point(beat.CenterX - 3.5, headY), new Point(beat.CenterX + 3.5, headY));
            }
            if (beat.Cell.Fermata) DrawStackedText(dc, layout, layout.IsSecondVoice ? "𝄑" : "𝄐", 12, null, beat.CenterX, 8, inkBrush);   // two voices: upright above and inverted below, as engraved
        }

        DrawStemsAndFlags(dc, layout, inkBrush);
        DrawBeams(dc, layout, inkBrush);
        DrawTremoloSlashes(dc, layout, inkBrush);
        DrawOctaveMarkings(dc, layout, inkBrush);
        DrawTies(dc, layout.Ties, inkBrush, layout.ContentLeft);
        DrawHopoSlurs(dc, layout.HopoSlurs, inkBrush);
        DrawSlideStrokes(dc, layout.Slides, inkBrush);
        if (Math.Abs(layout.StaffScale - 1.0) > 0.001) dc.Pop();
    }

    internal static void DrawOctaveMarkings(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var linePen = RenderDraw.Pen(brush, 0.9);
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
            var labelWidth = MakeText(label, 8.5, brush, FontWeights.SemiBold).Width;
            var boxTop = PlaceMark(layout, left - 2, Math.Max(right, labelLeft + labelWidth), 20, 6) + 3;   // the caption (drawn 1.5 px above the line row, taller than its box) is inside the claim
            var y = boxTop + 13;
            Draw(dc, label, labelLeft, boxTop - 1.5, 8.5, brush, FontWeights.SemiBold);
            dc.DrawLine(linePen, new Point(left, y), new Point(right, y));
            dc.DrawLine(linePen, new Point(left, y), new Point(left, y + 4));
            dc.DrawLine(linePen, new Point(right, y), new Point(right, y + 4));
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

    /// <summary>Small slashed grace notes just before the main note (stem up, slash across the stem).</summary>
    internal static void DrawGraceNotes(DrawingContext dc, StaffNotationBeat beat, Brush brush, double ghostRoom)
    {
        if (beat.GraceNotes.Count == 0) return;
        var pen = RenderDraw.Pen(brush, 0.8);
        var x = beat.CenterX - GraceOffset(beat, ghostRoom);
        foreach (var grace in GraceDrawOrder(beat)) // latest grace nearest the main note, so they read left to right in time
        {
            var rx = HeadRadiusX * 0.72; var ry = HeadRadiusY * 0.72;
            dc.PushTransform(new RotateTransform(-20, x, grace.Y));
            dc.DrawEllipse(brush, null, new Point(x, grace.Y), rx, ry);
            dc.Pop();
            var stemX = x + rx * 0.92;
            dc.DrawLine(pen, new Point(stemX, grace.Y), new Point(stemX, grace.Y - 17));
            dc.DrawLine(pen, new Point(stemX - 4, grace.Y - 9), new Point(stemX + 4.5, grace.Y - 14));
            x -= 9;
        }
    }

    /// <summary>Wavy arpeggio line / straight brush arrow left of a chord, trill "tr" + wave, wah +/o, above-staff marks.</summary>
    internal static void DrawBeatMarks(DrawingContext dc, StaffNotationMeasureLayout layout, StaffNotationBeat beat, Brush brush)
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
            var pen = RenderDraw.Pen(brush, 1.0);
            if (wavy)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x, y1), false, false);
                    for (var y = y1; y < y2; y += 3.5) c.LineTo(new Point(x + (((int)((y - y1) / 3.5) % 2) == 0 ? 1.8 : -1.8), y + 1.75), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
            else dc.DrawLine(pen, new Point(x, y1), new Point(x, y2));
            var tipY = down ? y2 + 1.5 : y1 - 1.5;
            var dir = down ? 1 : -1;
            var head = new StreamGeometry();
            using (var c = head.Open())
            {
                c.BeginFigure(new Point(x, tipY), true, true);
                c.LineTo(new Point(x - 2.6, tipY - dir * 4.2), true, false);
                c.LineTo(new Point(x + 2.6, tipY - dir * 4.2), true, false);
            }
            head.Freeze();
            dc.DrawGeometry(brush, null, head);
        }
        if (techniques.Contains("Trill"))
        {
            var trWidth = MakeText("tr", 9, brush, FontWeights.SemiBold).Width;
            var trTop = PlaceMark(layout, beat.CenterX - 5 - trWidth / 2, beat.CenterX + 4 + 8 * 2.6 + 1, 15, 6);
            DrawCentered(dc, "tr", beat.CenterX - 5, trTop + 4.5, 9, brush, FontWeights.SemiBold);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                var y = trTop + 12;
                c.BeginFigure(new Point(beat.CenterX + 4, y), false, false);
                for (var i = 1; i <= 8; i++) c.LineTo(new Point(beat.CenterX + 4 + i * 2.6, y + (i % 2 == 0 ? 0 : -2)), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, RenderDraw.Pen(brush, 0.9), g);
        }
        if (techniques.Contains("WahClose") || techniques.Contains("WahOpen"))
        {
            DrawStackedText(dc, layout, techniques.Contains("WahClose") ? "+" : "o", 10, FontWeights.Bold, beat.CenterX, 8, brush);
            // The words appear where the pedal turns on, not on every beat of a run.
            var at = layout.Beats.ToList().IndexOf(beat);
            var previousHasWah = at > 0 && layout.Beats[at - 1].Cell.Notes.Any(n => n.Techniques.Contains("WahClose") || n.Techniques.Contains("WahOpen"));
            if (!previousHasWah) DrawStackedBelow(dc, layout, "Wah-wah on", 8.5, null, beat.CenterX, 8, brush);
        }
        if (techniques.Contains("Tapping") || techniques.Contains("LeftTap"))
        {
            // Tapped notes carry a "+" above the notation.
            DrawStackedText(dc, layout, "+", 10, FontWeights.Bold, beat.CenterX, 8, brush);
        }
    }

    /// <summary>Tremolo picking: 1-3 slashes across the stem (above the head when the note has no stem).</summary>
    internal static void DrawTremoloSlashes(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var pen = RenderDraw.Pen(brush, 1.7);
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
                dc.DrawLine(pen, new Point(cx - 4.8, y + 2.2), new Point(cx + 4.8, y - 2.2));
            }
        }
    }

    internal static void DrawDeadNoteHead(DrawingContext dc, double x, double y, Brush brush)
    {
        var pen = RenderDraw.RoundPen(brush, 1.8);
        dc.DrawLine(pen, new Point(x - 3.8, y - 3.8), new Point(x + 3.8, y + 3.8));
        dc.DrawLine(pen, new Point(x - 3.8, y + 3.8), new Point(x + 3.8, y - 3.8));
    }

    internal static void DrawStemsAndFlags(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var stemPen = RenderDraw.Pen(brush, 0.9);
        foreach (var beat in layout.Beats)
        {
            if (!beat.HasStem) continue;
            dc.DrawLine(stemPen, new Point(beat.StemX, beat.StemStartY), new Point(beat.StemX, beat.StemEndY));
            if (beat.LowerStemTopY is { } lowerTop && beat.BeamGroupIndex < 0)
                dc.DrawLine(stemPen, new Point(beat.CenterX - 5, lowerTop), new Point(beat.CenterX - 5, beat.LowerStemEndY));
            if (beat.BeamGroupIndex < 0) DrawFlags(dc, beat, brush);
        }
    }

    internal static void DrawFlags(DrawingContext dc, StaffNotationBeat beat, Brush brush)
    {
        for (var f = 0; f < beat.Flags; f++)
        {
            var y = beat.StemUp ? beat.StemEndY + f * BeamGap : beat.StemEndY - f * BeamGap;
            dc.DrawGeometry(brush, null, FlagGeometry(beat.StemX, y, beat.StemUp));
        }
    }

    internal static void DrawBeams(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var pen = RenderDraw.Pen(brush, BeamThickness);
        foreach (var beam in layout.BeamSegments)
            dc.DrawLine(pen, new Point(beam.X1, beam.Y1), new Point(beam.X2, beam.Y2));
    }

    internal static void DrawTuplets(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
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
                var bracketPen = RenderDraw.Pen(brush, 0.9);
                // An incomplete group (fewer beats than its number) or one too short for the line on both sides of the
                // number: the reference shows the number alone.
                if (tuplet.Beats.Count >= tuplet.Numerator && x1 - x0 >= 24)
                {
                    dc.DrawLine(bracketPen, new Point(x0, lineY), new Point(Math.Max(x0, mid - 8), lineY));
                    dc.DrawLine(bracketPen, new Point(Math.Min(x1, mid + 8), lineY), new Point(x1, lineY));
                    dc.DrawLine(bracketPen, new Point(x0, lineY), new Point(x0, lineY + tick));
                    dc.DrawLine(bracketPen, new Point(x1, lineY), new Point(x1, lineY + tick));
                }
                DrawCentered(dc, numberText, mid, numberY, 11, brush, FontWeights.SemiBold);
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
                var pen = RenderDraw.Pen(brush, 1);
                if (tuplet.Beats.Count > 1 && tuplet.Beats.Count >= tuplet.Numerator)   // an incomplete group: the number alone, as in the reference
                {
                    dc.DrawLine(pen, new Point(left, lineY), new Point(right, lineY));
                    dc.DrawLine(pen, new Point(left, lineY), new Point(left, tickY));
                    dc.DrawLine(pen, new Point(right, lineY), new Point(right, tickY));
                }
                DrawCentered(dc, numberText, (left + right) / 2, up ? blockTop + 4.5 : blockTop + 11.5, 11, brush, FontWeights.SemiBold);
            }
        }
    }

    /// <summary>Rests are drawn as vector shapes (not font glyphs): full-size, black, the same on every machine.</summary>
    internal static void DrawRest(DrawingContext dc, TabCell cell, double cx, double staffTop, Brush brush)
    {
        var duration = NormalizeDuration(cell.DurationDenominator);
        var g = StaffGap;
        var restPen = RenderDraw.RoundPen(brush, 1.7);
        double dotY;
        if (duration <= 1)
        {
            dc.DrawRectangle(brush, null, new Rect(cx - 5.5, staffTop + g, 11, g * 0.5)); // hangs from the 4th line
            dotY = staffTop + g * 1.6;
        }
        else if (duration == 2)
        {
            dc.DrawRectangle(brush, null, new Rect(cx - 5.5, staffTop + 2 * g - g * 0.5, 11, g * 0.5)); // sits on the middle line
            dotY = staffTop + 2 * g - g * 0.9;
        }
        else if (duration == 4)
        {
            var y0 = staffTop + g * 1.0;
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                c.BeginFigure(new Point(cx - 2.2, y0), false, false);
                c.LineTo(new Point(cx + 2.6, y0 + 4.6), true, true);
                c.LineTo(new Point(cx - 2.6, y0 + 9.2), true, true);
                c.LineTo(new Point(cx + 2.4, y0 + 13.4), true, true);
                c.BezierTo(new Point(cx - 3.6, y0 + 13.0), new Point(cx - 4.2, y0 + 17.4), new Point(cx + 0.4, y0 + 18.0), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, restPen, geometry);
            dotY = y0 + 7;
        }
        else
        {
            // Eighth and shorter: a slanted stem with one flag dot per beam count.
            var flags = duration switch { 8 => 1, 16 => 2, 32 => 3, _ => 4 };
            var top = staffTop + g * 1.1;
            var bottom = top + g * (1.5 + 0.9 * (flags - 1));
            var stemTopX = cx + 2.8 + 0.6 * (flags - 1);
            var stemBottomX = cx - 2.2 - 0.6 * (flags - 1);
            dc.DrawLine(restPen, new Point(stemTopX, top), new Point(stemBottomX, bottom));
            for (var f = 0; f < flags; f++)
            {
                var fy = top + g * 0.8 * (f + 1) - 1;
                var fx = stemTopX - (stemTopX - stemBottomX) * (fy - top) / (bottom - top);
                dc.DrawEllipse(brush, null, new Point(fx - 2.3, fy), 1.9, 1.9);
                dc.DrawLine(RenderDraw.RoundPen(brush, 1.2), new Point(fx - 2.3, fy), new Point(fx + 1.6, fy - 2.2));
            }
            dotY = top + g * 0.6;
        }
        for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++)
            dc.DrawEllipse(brush, null, new Point(cx + 8 + d * 4, dotY), 1.4, 1.4);
    }

    internal static void DrawDrumHeads(DrawingContext dc, StaffNotationBeat beat, int measureIndex,
        Color ink, Color playColor, Color paper, IReadOnlySet<(int bar, int cell, int s)> sounding,
        IReadOnlySet<(int bar, int cell, int s)> struck)
    {
        var isSounding = false;
        var isStruck = false;
        foreach (var note in beat.Cell.Notes)
        {
            var key = (measureIndex, beat.CellIndex, note.StringIndex);
            if (sounding.Contains(key)) isSounding = true;
            if (struck.Contains(key)) isStruck = true;
        }
        var play = playColor == default ? Color.FromRgb(0x3F, 0xB9, 0x50) : playColor;
        var pen = RenderDraw.Pen(isSounding ? play : ink, 1.4);
        var fill = RenderDraw.Solid(isSounding ? play : ink);
        foreach (var note in beat.Cell.Notes)
        {
            // Each sound at its own staff position from the track's drum map.
            var entry = beat.DrumMap?.Invoke(note.MidiValue > 0 ? note.MidiValue : note.Fret);
            var y = entry is null ? beat.MinY : beat.StaffTop + entry.StaffStep * StaffGap / 2;
            var x = beat.CenterX;
            if (isSounding)
                dc.DrawEllipse(RenderDraw.Solid(Color.FromArgb(isStruck ? (byte)85 : (byte)42, play.R, play.G, play.B)),
                    null, new Point(x, y), isStruck ? 10 : 8.5, isStruck ? 8 : 6.8);
            switch (entry?.Head ?? "x")
            {
                case "normal":
                    dc.DrawEllipse(fill, null, new Point(x, y), HeadRadiusX, HeadRadiusY);
                    break;
                case "diamond":
                    var d = new StreamGeometry();
                    using (var g = d.Open())
                    {
                        g.BeginFigure(new Point(x - 4.5, y), true, true);
                        g.LineTo(new Point(x, y - 4.5), true, false); g.LineTo(new Point(x + 4.5, y), true, false); g.LineTo(new Point(x, y + 4.5), true, false);
                    }
                    d.Freeze();
                    dc.DrawGeometry(RenderDraw.Solid(paper), pen, d);
                    break;
                default: // x, circle (open hi-hat = x in a circle)
                    // Masked locally so the staff line cannot split the X glyph.
                    dc.DrawEllipse(RenderDraw.Solid(paper), null, new Point(x, y), 5.4, 5.4);
                    dc.DrawLine(pen, new Point(x - 4, y - 4), new Point(x + 4, y + 4));
                    dc.DrawLine(pen, new Point(x - 4, y + 4), new Point(x + 4, y - 4));
                    if (entry?.Head == "circle") dc.DrawEllipse(null, RenderDraw.Pen(isSounding ? play : ink, 1), new Point(x, y), 6.2, 6.2);
                    break;
            }
        }
    }

    /// <summary>The pen for staff lines AND ledger lines: the staff-line colour (which already carries the staff-line opacity), one thickness.</summary>
    internal static Pen StaffLinePen(Color staffLineColor) => RenderDraw.Pen(staffLineColor, StaffLineThickness);

    internal static Color EngravingInkColor(Color ink, Color paper)
    {
        const double soften = 0.14;
        static byte Mix(byte foreground, byte background, double amount)
            => (byte)Math.Round(foreground * (1 - amount) + background * amount);
        return Color.FromArgb(ink.A,
            Mix(ink.R, paper.R, soften), Mix(ink.G, paper.G, soften), Mix(ink.B, paper.B, soften));
    }

    internal static void DrawLedgerLines(DrawingContext dc, StaffNotationMeasureLayout layout,
        Color staffLineColor, LedgerLineMode mode)
    {
        if (mode == LedgerLineMode.Hidden) return;
        // Exactly the staff lines' pen: ledger lines are an extension of the staff and always match it.
        var pen = StaffLinePen(staffLineColor);
        foreach (var line in LedgerLineSegments(layout, mode))
            dc.DrawLine(pen, new Point(line.X1, line.Y), new Point(line.X2, line.Y));
    }

    internal static void DrawAugmentationDots(DrawingContext dc, TabCell cell, StaffNotationNote note, Brush brush)
    {
        var isLine = Math.Abs(note.StaffStep % 2) == 0;
        var dotY = isLine ? note.Y - StaffGap / 2 : note.Y;
        for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++)
            dc.DrawEllipse(brush, null, new Point(note.X + HeadRadiusX + 3 + d * 4, dotY), 1.4, 1.4);
    }

    internal static void DrawTies(DrawingContext dc, IReadOnlyList<StaffNotationTie> ties, Brush brush, double contentLeft = double.NegativeInfinity)
    {
        foreach (var tie in ties)
        {
            // A stub reaching back over the key / time signature is left to the previous bar's outgoing stub.
            var edge = tie.TowardLeft ? Math.Max(tie.X2, contentLeft + 2) : tie.X2;
            if (tie.IsStub && tie.TowardLeft && edge > tie.X1 - 13) continue;
            if (tie.IsStub) DrawTieStub(dc, tie.X1, tie.Y1, edge, tie.TowardLeft, tie.Above, brush);
            else DrawTie(dc, tie.X1, tie.Y1, tie.X2, tie.Y2, tie.Above, brush, tie.StartInset, tie.EndInset);
        }
    }

    internal static void DrawTie(DrawingContext dc, double x1, double y1, double x2, double y2, bool above, Brush brush, double startInset = 5, double endInset = 5)
    {
        var dir = above ? -1.0 : 1.0;
        var y1b = y1 + dir * 6;
        var y2b = y2 + dir * 6;
        var (sx, c1, c2, ex, span) = ArcShape(x1, x2, startInset, endInset);
        var bow = Math.Clamp(span * 0.10 + 4, 5, 14);
        var figure = new PathFigure { StartPoint = new Point(sx, y1b), IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new Point(c1, y1b + dir * bow),
            new Point(c2, y2b + dir * bow),
            new Point(ex, y2b), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, RenderDraw.Pen(brush, 1.2), geometry);
    }

    /// <summary>Half a tie: from the note to <paramref name="edgeX"/> (the barline or the system edge), ending level at the arc's
    /// peak, so the halves on both sides of a barline join into one arc.</summary>
    internal static void DrawTieStub(DrawingContext dc, double x, double y, double edgeX, bool towardLeft, bool above, Brush brush)
    {
        var dir = above ? -1.0 : 1.0;
        var sign = towardLeft ? -1.0 : 1.0;
        var yb = y + dir * 6;
        var sx = x + sign * 5;
        var span = Math.Max(8, sign * (edgeX - sx)) * sign;
        var peak = yb + dir * 7;
        var figure = new PathFigure { StartPoint = new Point(sx, yb), IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new Point(sx + span * 0.3, yb + dir * 4.5),
            new Point(sx + span * 0.65, peak),
            new Point(sx + span, peak), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, RenderDraw.Pen(brush, 1.2), geometry);
    }

    internal static void DrawSlideStrokes(DrawingContext dc, IReadOnlyList<StaffNotationSlideStroke> slides, Brush brush)
    {
        if (slides.Count == 0) return;
        var pen = RenderDraw.Pen(brush, 1.1);
        foreach (var slide in slides) dc.DrawLine(pen, new Point(slide.X1, slide.Y1), new Point(slide.X2, slide.Y2));
    }

    internal static void DrawHopoSlurs(DrawingContext dc, IReadOnlyList<StaffNotationSlur> slurs, Brush brush)
    {
        var pen = RenderDraw.Pen(brush, 1.1);
        foreach (var slur in slurs)
        {
            var direction = slur.StemsUp ? 1.0 : -1.0;
            var (sx, c1, c2, ex, span) = ArcShape(slur.X1, slur.X2, slur.StartInset, slur.EndInset);
            var start = new Point(sx, slur.Y1 + direction * 6);
            var end = new Point(ex, slur.Y2 + direction * 6);
            var bow = Math.Clamp(span * 0.10 + 4, 5, 12);
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new BezierSegment(
                new Point(c1, start.Y + direction * bow),
                new Point(c2, end.Y + direction * bow), end, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    internal static void DrawCentered(DrawingContext dc, string text, double cx, double cy, double size, Brush brush,
        FontWeight? weight = null)
    {
        var formatted = MakeText(text, size, brush, weight);
        TabForge.Visualization.Draw.DrawText(dc, formatted, new Point(cx - formatted.Width / 2, cy - formatted.Height / 2));
    }

    internal static void Draw(DrawingContext dc, string text, double x, double y, double size, Brush brush,
        FontWeight? weight = null) => TabForge.Visualization.Draw.DrawText(dc, MakeText(text, size, brush, weight), new Point(x, y));

    internal static FormattedText MakeText(string text, double size, Brush brush, FontWeight? weight) =>
        ScoreText.CachedText(text, size, brush, weight, "Segoe UI Symbol");

}
