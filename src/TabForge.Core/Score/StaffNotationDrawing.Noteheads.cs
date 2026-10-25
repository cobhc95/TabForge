using System.Globalization;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationLayoutBuilder;

namespace TabForge.Views;

/// <summary>
/// The heads of a measure: augmentation dots, dead-note crosses, grace notes, rests and drum heads, in colour values through the drawing port.
/// Owns: drawing those glyphs. Does not own: where they sit (StaffNotationLayoutBuilder), the rest of the measure (StaffNotationDrawing).
/// Tests: no dedicated test class.
/// </summary>
internal static partial class StaffNotationDrawing
{
    /// <summary>Small slashed grace notes just before the main note (stem up, slash across the stem).</summary>
    internal static void DrawGraceNotes(IDrawContext dc, StaffNotationBeat beat, UiColor colour, double ghostRoom)
    {
        if (beat.GraceNotes.Count == 0) return;
        var pen = new UiStroke(colour, 0.8);
        var x = beat.CenterX - GraceOffset(beat, ghostRoom);
        foreach (var grace in GraceDrawOrder(beat)) // latest grace nearest the main note, so they read left to right in time
        {
            var rx = HeadRadiusX * 0.72; var ry = HeadRadiusY * 0.72;
            dc.PushRotate(-20, x, grace.Y);
            dc.Ellipse(colour, (UiStroke?)null, x, grace.Y, rx, ry);
            dc.Pop();
            var stemX = x + rx * 0.92;
            dc.Line(pen, stemX, grace.Y, stemX, grace.Y - 17);
            dc.Line(pen, stemX - 4, grace.Y - 9, stemX + 4.5, grace.Y - 14);
            x -= 9;
        }
    }

    internal static void DrawDeadNoteHead(IDrawContext dc, double x, double y, UiColor colour)
    {
        var pen = new UiStroke(colour, 1.8, RoundCaps: true);
        dc.Line(pen, x - 3.8, y - 3.8, x + 3.8, y + 3.8);
        dc.Line(pen, x - 3.8, y + 3.8, x + 3.8, y - 3.8);
    }

    /// <summary>Rests are drawn as vector shapes (not font glyphs): full-size, black, the same on every machine.</summary>
    internal static void DrawRest(IDrawContext dc, TabCell cell, double cx, double staffTop, UiColor colour)
    {
        var duration = NormalizeDuration(cell.DurationDenominator);
        var g = StaffGap;
        var restPen = new UiStroke(colour, 1.7, RoundCaps: true);
        double dotY;
        if (duration <= 1)
        {
            dc.Rect(colour, (UiStroke?)null, cx - 5.5, staffTop + g, 11, g * 0.5); // hangs from the 4th line
            dotY = staffTop + g * 1.6;
        }
        else if (duration == 2)
        {
            dc.Rect(colour, (UiStroke?)null, cx - 5.5, staffTop + 2 * g - g * 0.5, 11, g * 0.5); // sits on the middle line
            dotY = staffTop + 2 * g - g * 0.9;
        }
        else if (duration == 4)
        {
            var (x, y0) = (cx, staffTop + g * 1.0); // x is captured instead of the parameter, so other rests allocate no closure
            var geometry = dc.Paths.BuildPath(c =>
            {
                c.BeginFigure(new UiPoint(x - 2.2, y0), false, false);
                c.LineTo(new UiPoint(x + 2.6, y0 + 4.6), smoothJoin: true);
                c.LineTo(new UiPoint(x - 2.6, y0 + 9.2), smoothJoin: true);
                c.LineTo(new UiPoint(x + 2.4, y0 + 13.4), smoothJoin: true);
                c.BezierTo(new UiPoint(x - 3.6, y0 + 13.0), new UiPoint(x - 4.2, y0 + 17.4), new UiPoint(x + 0.4, y0 + 18.0), smoothJoin: true);
            });
            dc.Path((UiColor?)null, restPen, geometry);
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
            dc.Line(restPen, stemTopX, top, stemBottomX, bottom);
            for (var f = 0; f < flags; f++)
            {
                var fy = top + g * 0.8 * (f + 1) - 1;
                var fx = stemTopX - (stemTopX - stemBottomX) * (fy - top) / (bottom - top);
                dc.Ellipse(colour, (UiStroke?)null, fx - 2.3, fy, 1.9, 1.9);
                dc.Line(new UiStroke(colour, 1.2, RoundCaps: true), fx - 2.3, fy, fx + 1.6, fy - 2.2);
            }
            dotY = top + g * 0.6;
        }
        for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++)
            dc.Ellipse(colour, (UiStroke?)null, cx + 8 + d * 4, dotY, 1.4, 1.4);
    }

    internal static void DrawDrumHeads(IDrawContext dc, StaffNotationBeat beat, int measureIndex,
        UiColor ink, UiColor playColor, UiColor paper, IReadOnlySet<(int bar, int cell, int s)> sounding,
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
        var play = playColor == default ? Palette.Get("FollowHighlightText") : playColor;
        var pen = new UiStroke(isSounding ? play : ink, 1.4);
        var fill = isSounding ? play : ink;
        foreach (var note in beat.Cell.Notes)
        {
            // Each sound at its own staff position from the track's drum map.
            var entry = beat.DrumMap?.Invoke(note.MidiValue > 0 ? note.MidiValue : note.Fret);
            var y = entry is null ? beat.MinY : beat.StaffTop + entry.StaffStep * StaffGap / 2;
            var x = beat.CenterX;
            if (isSounding)
                dc.Ellipse(new UiColor(ScorePlayedChip.HaloAlpha(ScorePlayedChip.IsLight(paper), isStruck), play.R, play.G, play.B), (UiStroke?)null, x, y, isStruck ? 10 : 8.5, isStruck ? 8 : 6.8);
            switch (entry?.Head ?? "x")
            {
                case "normal":
                    dc.Ellipse(fill, (UiStroke?)null, x, y, HeadRadiusX, HeadRadiusY);
                    break;
                case "diamond": {
                    var (headX, headY) = (x, y); // captured instead of the loop's x and y, so other drum notes allocate no closure
                    var d = dc.Paths.BuildPath(g =>
                    {
                        g.BeginFigure(new UiPoint(headX - 4.5, headY), true, true); g.LineTo(new UiPoint(headX, headY - 4.5)); g.LineTo(new UiPoint(headX + 4.5, headY)); g.LineTo(new UiPoint(headX, headY + 4.5));
                    });
                    dc.Path(paper, pen, d);
                    break; }
                default: // x, circle (open hi-hat = x in a circle)
                    // Masked locally so the staff line cannot split the X glyph.
                    dc.Ellipse(paper, (UiStroke?)null, x, y, 5.4, 5.4);
                    dc.Line(pen, x - 4, y - 4, x + 4, y + 4);
                    dc.Line(pen, x - 4, y + 4, x + 4, y - 4);
                    if (entry?.Head == "circle") dc.Ellipse((UiColor?)null, new UiStroke(isSounding ? play : ink, 1), x, y, 6.2, 6.2);
                    break;
            }
        }
    }

    internal static void DrawAugmentationDots(IDrawContext dc, TabCell cell, StaffNotationNote note, UiColor colour)
    {
        var isLine = Math.Abs(note.StaffStep % 2) == 0;
        var dotY = isLine ? note.Y - StaffGap / 2 : note.Y;
        for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++)
            dc.Ellipse(colour, (UiStroke?)null, note.X + HeadRadiusX + 3 + d * 4, dotY, 1.4, 1.4);
    }
}
