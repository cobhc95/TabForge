using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views.Score;

// ScoreRenderer: the marks drawn over the tab (bends, whammy, harmonics, grace notes, fingering, let-ring spans).
internal sealed partial class ScoreRenderer
{
    private static StreamGeometry ArrowHead(Point tip, double dx, double dy, double size = 3.4)
    {
        // (dx, dy) is the unit direction the arrow points in.
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var back = new Point(tip.X - dx * size * 1.6, tip.Y - dy * size * 1.6);
            var nx = -dy; var ny = dx;
            c.BeginFigure(tip, true, true);
            c.LineTo(new Point(back.X + nx * size * 0.8, back.Y + ny * size * 0.8), true, false);
            c.LineTo(new Point(back.X - nx * size * 0.8, back.Y - ny * size * 0.8), true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Curved bend arrow(s) with amounts: bend up, release, pre-bend (vertical) and custom multi-point curves.</summary>
    private void DrawTabBend(DrawingContext dc, TabNote note, double cx, double sy, double halfWidth, double rightLimit, Color ink, bool blockedAbove = false)
    {
        var points = ScoreMarkText.EffectiveBendPoints(note);
        if (points.Count == 0) return;
        var pen = RenderDraw.Pen(ink, 1.05);
        var brush = ScoreText.Brush(ink);
        var baseY = sy - 5.5;
        // The standard arrows all rise about the same height up to a full tone, higher for larger bends.
        static double HeightOf(double v) => v <= 0 ? 0 : v <= 4 ? 22 + v * 1.5 : Math.Min(28 + (v - 4) * 4, 48);
        double YOf(double v) => baseY - HeightOf(v);
        var startX = cx + halfWidth + 1.5;
        // A bend on a lower string rises past the higher strings' numbers: its pre-bend arrow then stands beside them, not through them.
        var arrowX = blockedAbove ? cx + halfWidth + 3.5 : cx;
        var span = Math.Clamp(Math.Min(rightLimit - startX - 3, 18.0 * Math.Max(1, points.Count - 1)), 12, 80);
        double XOf(double offset) => startX + Math.Clamp(offset, 0, 60) / 60.0 * span;

        var previousValue = 0.0;
        var first = points[0];
        if (first.Value > 0.01 && first.Offset < 1)
        {
            // Pre-bend: vertical arrow from the fret number.
            var top = YOf(first.Value);
            dc.DrawLine(pen, new Point(arrowX, baseY), new Point(arrowX, top + 3));
            dc.DrawGeometry(brush, null, ArrowHead(new Point(arrowX, top), 0, -1));
            _bendLabelBoxes.Add(new Rect(arrowX - 3.6, top - 1, 7.2, 7));
            var label = ScoreMarkText.BendAmountLabel(first.Value);
            if (label.Length > 0) DrawBendLabel(dc, arrowX, top - 13, label, brush);
            previousValue = first.Value;
            if (points.All(pt => Math.Abs(pt.Value - first.Value) < 0.01)) return;   // a pure pre-bend is just the arrow
            startX = arrowX + 1;                                                       // a release continues from the arrow's tip
            span = Math.Clamp(Math.Min(rightLimit - startX - 3, 18.0 * Math.Max(1, points.Count - 1)), 12, 80);
        }

        var curve = new StreamGeometry();
        var arrows = new List<(Point Tip, double Dx, double Dy)>();
        var labels = new List<(Point At, string Text)>();
        using (var c = curve.Open())
        {
            var current = new Point(XOf(points[0].Offset), YOf(previousValue > 0 ? previousValue : points[0].Value));
            c.BeginFigure(current, false, false);
            for (var i = 1; i < points.Count; i++)
            {
                var next = new Point(XOf(points[i].Offset), YOf(points[i].Value));
                if (Math.Abs(next.Y - current.Y) < 0.5) c.LineTo(next, true, false);
                else
                {
                    var rising = next.Y < current.Y;
                    c.QuadraticBezierTo(rising ? new Point(next.X, current.Y) : new Point(current.X, next.Y), next, true, false);
                    arrows.Add((next, 0, rising ? -1 : 1));
                    if (rising && (i == points.Count - 1 || points[i + 1].Value <= points[i].Value + 0.01))
                    {
                        var text = ScoreMarkText.BendAmountLabel(points[i].Value);
                        if (text.Length > 0) labels.Add((new Point(next.X, next.Y - 13), text));
                    }
                    else if (!rising && points[i].Value > 0.01)
                    {
                        // A release that stops above the note (e.g. back to 1/2) names where it lands, beside the arrow.
                        var text = ScoreMarkText.BendAmountLabel(points[i].Value);
                        if (text.Length > 0) labels.Add((new Point(next.X + 9, next.Y - 8), text));
                    }
                }
                current = next;
            }
        }
        curve.Freeze();
        dc.DrawGeometry(null, pen, curve);
        foreach (var (tip, dx, dy) in arrows)
        {
            dc.DrawGeometry(brush, null, ArrowHead(tip, dx, dy, 3.0));
            _bendLabelBoxes.Add(new Rect(tip.X - 3.4, dy < 0 ? tip.Y - 1 : tip.Y - 6, 6.8, 7));
        }
        _bendLabelBoxes.Add(curve.Bounds);
        foreach (var (at, text) in labels) DrawBendLabel(dc, at.X, at.Y, text, brush);
    }

    private readonly List<Rect> _bendLabelBoxes = new();

    private readonly List<(string Text, Rect Box)> _drawnBendLabels = new();

    private readonly List<(double X, double Y, string Text, Brush Brush)> _pendingBendLabels = new();

    /// <summary>Queues a bend amount; labels are placed after every curve of the bar is known so none lands on an arrow or another label.</summary>
    private void DrawBendLabel(DrawingContext dc, double cx, double y, string text, Brush brush) =>
        _pendingBendLabels.Add((cx, y, text, brush));

    /// <summary>Draws the queued bend labels, moving each one line up while it would touch an arrowhead, curve or earlier label.</summary>
    private void FlushBendLabels(DrawingContext dc)
    {
        foreach (var (x, y, text, brush) in _pendingBendLabels)
        {
            var ft = ScoreText.MakeTextIn(ScoreTextArea.Technique, text, 8, brush);
            var rect = new Rect(x - ft.Width / 2, y, ft.Width, ft.Height - 2);
            // The same amount repeated over a crowded run (notes a few px apart) is printed once: a pile of identical labels says nothing more.
            if (ScoreMarkText.DuplicateBendLabel(_drawnBendLabels, text, rect)) continue;
            for (var guard = 0; guard < 4 && _bendLabelBoxes.Any(r => r.IntersectsWith(rect)); guard++)
                rect.Offset(0, -9);
            if (_sky.Count > 0) rect.Y = _sky.PlaceAbove(rect.X, rect.Right, rect.Height, rect.Bottom);   // clear of the technique labels, accents and so on stacked over the beat
            _bendLabelBoxes.Add(rect);
            _drawnBendLabels.Add((text, rect));
            TabForge.Visualization.Draw.DrawText(dc, ft, new Point(rect.X, rect.Y));
        }
        _pendingBendLabels.Clear();
    }

    /// <summary>
    /// Whammy-bar diagram draws it: a line that starts at the note on the TAB and dives down (or rises) by the
    /// bar's amount, with the value (-1/2, -1, -2 ...) at each turning point.
    /// </summary>
    /// <summary>Lowest point a whammy dive may reach: just under the strings, and never so low that its amount label (about 12 px under the point) leaves the system.</summary>
    private double WhammyFloorY(double tabTop, int strings) =>
        Math.Min(tabTop + (strings - 1) * _host.StringGap + 8, tabTop + (strings - 1) * _host.StringGap + 28.0 * _host.Appearance.ScoreSpacing - 14);

    private void DrawTabWhammy(DrawingContext dc, TabCell cell, double cx, double rightLimit, double startY, Color ink, bool claim = false, double maxDrop = double.PositiveInfinity)
    {
        var points = ScoreMarkText.EffectiveWhammyPoints(cell);
        if (points.Count == 0) return;
        var pen = RenderDraw.Pen(ink, 1.0);
        var brush = ScoreText.Brush(ink);
        var startX = cx + 7;
        var span = Math.Clamp(rightLimit - startX - 2, 26, 62);
        var unit = 7.5; // pixels per quarter-tone (-2 tones = 60 px)
        var deepest = points.Min(p => p.Value);
        // A deep dive is drawn compressed so it stays inside the system (the labels keep the true amounts).
        if (deepest < 0 && deepest * -unit > maxDrop) unit = Math.Max(0.6, Math.Max(maxDrop, 8) / -deepest);
        Point At(BendPointModel p) => new(startX + Math.Clamp(p.Offset, 0, 60) / 60.0 * span, startY - p.Value * unit);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(At(points[0]), false, false);
            for (var i = 1; i < points.Count; i++) c.LineTo(At(points[i]), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
        // The curve and its amount labels take their space in the bar's stack so dynamics and other marks keep clear.
        var curveTop = points.Min(p => At(p).Y) - 12; var curveBottom = points.Max(p => At(p).Y) + 12;
        if (claim) _sky.Claim(startX - 2, startX + span + 2, curveTop, curveBottom);
        var placed = new List<Rect>();
        var placedText = "";
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var turning = i == points.Count - 1 && Math.Abs(p.Value) > 0.01 ||
                          i > 0 && i < points.Count - 1 && (points[i - 1].Value - p.Value) * (points[i + 1].Value - p.Value) > 0 ||
                          i == 0 && Math.Abs(p.Value) > 0.01;
            if (!turning) continue;
            var pt = At(p);
            var text = ScoreMarkText.WhammyAmountLabel(p.Value);
            var ft = ScoreText.MakeTextIn(ScoreTextArea.Technique, text, 7.5, brush);
            var left = Math.Clamp(pt.X - ft.Width / 2, startX - 2, Math.Max(startX - 2, rightLimit - ft.Width));
            var rect = new Rect(left, pt.Y + (p.Value < 0 ? 2 : -11), ft.Width, ft.Height - 2);
            if (placed.Any(r => r.IntersectsWith(rect))) { if (text == placedText) continue; rect.Offset(0, p.Value < 0 ? 9 : -9); }
            if (ScoreMarkText.DuplicateBendLabel(_drawnBendLabels, text, rect)) continue;   // the same amount of a neighbouring beat a few px away
            placed.Add(rect);
            _drawnBendLabels.Add((text, rect));
            placedText = text;
            TabForge.Visualization.Draw.DrawText(dc, ft, new Point(rect.X, rect.Y));
        }
    }

    /// <summary>1-3 short slanted slashes stacked over a beat (tremolo picking in TAB).</summary>
    private static void DrawTabTremoloSlashes(DrawingContext dc, int count, double cx, double firstY, Color ink)
    {
        var pen = RenderDraw.Pen(ink, 1.6);
        for (var i = 0; i < count; i++)
            dc.DrawLine(pen, new Point(cx - 4.5, firstY + i * 3.4 + 2.2), new Point(cx + 4.5, firstY + i * 3.4 - 2.2));
    }

    /// <summary>Everything per beat that is not a plain fret number: bends, whammy, trill, tremolo, wah, strokes, brush.</summary>
    private void DrawTabBeatMarks(DrawingContext dc, TrackModel track, StaffNotationMeasureLayout layout, StaffNotationBeat beat,
        double measureRight, double tabTop, int strings, Color ink, bool hasStaff, double underOffset = 0)
    {
        var cell = beat.Cell;
        var cx = beat.CenterX;
        var next = layout.Beats.Where(b => b.CenterX > cx + 1).OrderBy(b => b.CenterX).FirstOrDefault();
        var rightLimit = next is null ? measureRight - 4 : next.CenterX - 7;
        var brush = ScoreText.Brush(ink);
        var topString = cell.Notes.Count == 0 ? 0 : cell.Notes.Min(n => n.StringIndex);
        var topY = tabTop + topString * _host.StringGap;

        foreach (var note in cell.Notes)
        {
            if (note.StringIndex < 0 || note.StringIndex >= strings) continue;
            var sy = tabTop + note.StringIndex * _host.StringGap;
            var label = ScoreMarkText.FretLabelWidthText(note);
            var half = ScoreText.MakeTextIn(ScoreTextArea.Fret, label, _host.FretFontSize, brush, FontWeights.Normal, "Consolas").Width / 2 + 1;
            if (track.Kind != TrackKind.Drums && (note.Techniques.Contains("Bend") || note.BendPoints.Count > 0 && !note.IsGraceNote) &&
                !ScoreMarkText.IsRepeatedDoubleStopBend(cell, note))
                DrawTabBend(dc, note, cx, sy, half, rightLimit, ink, cell.Notes.Any(o => o.StringIndex < note.StringIndex && !o.IsGraceNote));
            if (note.Techniques.Contains("Trill"))
            {
                var fret = ScoreMarkText.TrillFret(track, note);
                if (fret >= 0) ScoreText.DrawIn(ScoreTextArea.Fret, dc, $"({fret})", cx + half + 2.6, sy - 6, _host.FretFontSize - 2, brush, FontWeights.Normal, "Consolas");
            }
        }

        if (cell.Notes.Any(n => n.Techniques.Any(t => t.StartsWith("TremBar", StringComparison.OrdinalIgnoreCase))) || cell.WhammyPoints.Count > 0)
            DrawTabWhammy(dc, cell, cx, rightLimit, topY, ink, !hasStaff, WhammyFloorY(tabTop, strings) - topY);

        var slashes = ScoreMarkText.TremoloSlashCount(cell);
        if (slashes > 0) DrawTabTremoloSlashes(dc, slashes, cx, tabTop + cell.Notes.Max(n => n.StringIndex) * _host.StringGap + 11, ink); // The reference: under the fret number

        if (!hasStaff && cell.Notes.Any(n => n.Techniques.Contains("Trill")))
        {
            var trY = _sky.PlaceAbove(cx - 6, Math.Max(cx + 22, rightLimit - 4), 14, tabTop - 10);
            ScoreText.DrawCentered(dc, "tr", cx - 4, trY, 9, brush, FontWeights.SemiBold);
            DrawVibratoLine(dc, cx + 4, Math.Max(cx + 22, rightLimit - 4), trY + 7, false, brush);
        }

        // Wah: "+" closed / "o" open, with the words "wah" only where the pedal turns on.
        var wahClose = cell.Notes.Any(n => n.Techniques.Contains("WahClose"));
        var wahOpen = cell.Notes.Any(n => n.Techniques.Contains("WahOpen"));
        if ((wahClose || wahOpen) && !hasStaff) // with a staff the +/o is engraved over the notation
            ScoreText.DrawCentered(dc, wahClose ? "+" : "o", cx, _sky.PlaceAbove(cx - 5, cx + 5, 12, tabTop - 10), 10, brush, FontWeights.Bold);

        // Pick strokes under the TAB: down = a square bracket, up = a V.
        var stroke = cell.Notes.Any(n => n.Techniques.Contains("PickDown")) ? 1 : cell.Notes.Any(n => n.Techniques.Contains("PickUp")) ? 2 : 0;
        if (stroke != 0)
        {
            var y = tabTop + (strings - 1) * _host.StringGap + 8 + ScoreMarkText.PickStrokeLift;
            var pen = RenderDraw.Pen(ink, 1.1);
            if (stroke == 1)
            {
                dc.DrawLine(pen, new Point(cx - 3.5, y + 6), new Point(cx - 3.5, y));
                dc.DrawLine(pen, new Point(cx - 3.5, y), new Point(cx + 3.5, y));
                dc.DrawLine(pen, new Point(cx + 3.5, y), new Point(cx + 3.5, y + 6));
            }
            else
            {
                dc.DrawLine(pen, new Point(cx - 3.5, y), new Point(cx, y + 6));
                dc.DrawLine(pen, new Point(cx, y + 6), new Point(cx + 3.5, y));
            }
        }

        // Brush / arpeggio: an arrow running along the chord just left of the numbers; rasgueado is a caption.
        var brushDown = cell.Notes.Any(n => n.Techniques.Contains("BrushDown") || n.Techniques.Contains("ArpeggioDown"));
        var brushUp = cell.Notes.Any(n => n.Techniques.Contains("BrushUp") || n.Techniques.Contains("ArpeggioUp"));
        if ((brushDown || brushUp) && cell.Notes.Count > 0)
        {
            var bottomString = cell.Notes.Max(n => n.StringIndex);
            var x = cx - 10;
            var y1 = topY - 3;
            var y2 = tabTop + bottomString * _host.StringGap + 3;
            if (y2 < y1 + 8) y2 = y1 + 8;
            var pen = RenderDraw.Pen(ink, 1.1);
            var arpeggio = cell.Notes.Any(n => n.Techniques.Contains("ArpeggioDown") || n.Techniques.Contains("ArpeggioUp"));
            if (arpeggio)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x, y1), false, false);
                    for (var y = y1; y < y2; y += 4) c.LineTo(new Point(x + (((int)((y - y1) / 4) % 2) == 0 ? 2 : -2), y + 2), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
            else dc.DrawLine(pen, new Point(x, y1), new Point(x, y2));
            // GP5 draws the arrow the way the stroke crosses the TAB: a downstroke (bass string first) runs from the bottom line up, so its head is at the top.
            dc.DrawGeometry(brush, null, brushDown ? ArrowHead(new Point(x, y1 - 2), 0, -1, 2.8) : ArrowHead(new Point(x, y2 + 2), 0, 1, 2.8));
        }
        if (cell.Notes.Any(n => n.Techniques.Contains("Rasgueado")))
            ScoreText.DrawCentered(dc, "rasg.", cx, hasStaff ? tabTop - 22 : _sky.PlaceAbove(cx - 13, cx + 13, 11, tabTop - 10), 8.5, brush);
        var lastStringY = tabTop + (strings - 1) * _host.StringGap + underOffset + ScoreMarkText.PickStrokeShift(cell);
        var harmonicLift = DrawHarmonicFrets(dc, cell, cx, lastStringY, ink, measureRight - 2);
        DrawTabFingering(dc, cell, cx, lastStringY + harmonicLift, ink);
    }

    /// <summary>Draws the harmonic values under the TAB; returns the height taken so fingering moves below it.</summary>
    private static double DrawHarmonicFrets(DrawingContext dc, TabCell cell, double cx, double lastStringY, Color ink, double rightLimit = double.PositiveInfinity)
    {
        var parts = cell.Notes.OrderByDescending(n => n.StringIndex).Select(ScoreMarkText.HarmonicFretText).Where(t => t.Length > 0).ToList();
        if (parts.Count == 0) return 0;
        // Stays inside its bar (a beat at the very end of a system would otherwise straddle the bar line).
        var ft = ScoreText.MakeText(string.Join(" ", parts), 8, ScoreText.Brush(ink), FontWeights.SemiBold);
        var left = Math.Min(cx - ft.Width / 2, rightLimit - ft.Width);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(left, lastStringY + 9));
        return ScoreMarkText.HarmonicRowHeight;
    }

    /// <summary>The line or arc between a grace fret and its main note, as the reference draws it: a slide or hammer-on / pull-off is an arc above the numbers (GP5), a bend grace a rising arrow.</summary>
    private void DrawTabGraceTransition(DrawingContext dc, TabCell cell, TabNote grace, double graceX, double graceWidth, double mainX, double graceY, double tabTop, Color ink)
    {
        var slide = grace.Techniques.Contains("Slide") || grace.Techniques.Contains("ShiftSlide") || grace.Techniques.Contains("LegatoSlide");
        var hammer = grace.Techniques.Contains("HOPO") || grace.Techniques.Contains("HOPOOrigin");
        var bend = grace.Techniques.Contains("GraceBend") || cell.Notes.Any(o => o.Techniques.Contains("GraceBend"));
        if (!slide && !hammer && !bend) return;
        var main = cell.Notes.Where(o => !o.IsGraceNote).OrderBy(o => o.StringIndex == grace.StringIndex ? 0 : 1).ThenBy(o => Math.Abs(o.StringIndex - grace.StringIndex)).First();
        var mainLabel = ScoreMarkText.FretLabelWidthText(main);
        var mainWidth = ScoreText.MakeTextIn(ScoreTextArea.Fret, mainLabel, _host.FretFontSize, ScoreText.Brush(ink), FontWeights.Normal, "Consolas").Width;
        var startX = graceX + graceWidth / 2 + 1.5;
        var endX = mainX - mainWidth / 2 - 1.5;
        if (endX - startX < 2) return;
        var mainY = tabTop + main.StringIndex * _host.StringGap;
        var pen = RenderDraw.Pen(ink, 1.05);
        if (bend && !slide)
        {
            // A bent grace note: a line rising from the grace fret to the main note, with an arrowhead at its end.
            var from = new Point(startX, graceY - 1.5);
            var to = new Point(endX, mainY - 4.5);
            dc.DrawLine(pen, from, to);
            var len = Math.Max(1, Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y)));
            dc.DrawGeometry(ScoreText.Brush(ink), null, ArrowHead(to, (to.X - from.X) / len, (to.Y - from.Y) / len, 2.2));
        }
        else
        {
            // GP5 draws a grace slide transition the same as a hammer-on: an arc over the two numbers (no slide line).
            var y = Math.Min(graceY, mainY) - 8.5;
            var arc = Math.Clamp((endX - startX) * 0.35, 2, 4);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(startX, y), false, false);
                c.QuadraticBezierTo(new Point((startX + endX) / 2, y - arc * 2), new Point(endX, y), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    /// <summary>Left-hand finger numbers (circled, T = thumb) and right-hand p i m a c letters under the TAB.</summary>
    private static void DrawTabFingering(DrawingContext dc, TabCell cell, double cx, double lastStringY, Color ink)
    {
        var left = cell.Notes.Where(n => n.LeftHandFinger is >= 0 and <= 4).OrderBy(n => n.StringIndex).Select(n => n.LeftHandFinger!.Value).ToList();
        var right = cell.Notes.Where(n => n.RightHandFinger is >= 0 and <= 4).OrderByDescending(n => n.StringIndex).Select(n => n.RightHandFinger!.Value).ToList();
        if (left.Count == 0 && right.Count == 0) return;
        var brush = ScoreText.Brush(ink);
        var ringPen = RenderDraw.Pen(ink, 0.8);
        if (left.Count > 0)
        {
            var startX = cx - (left.Count - 1) * 5.5;
            for (var i = 0; i < left.Count; i++)
            {
                var x = startX + i * 11;
                dc.DrawEllipse(null, ringPen, new Point(x, lastStringY + 20), 5.4, 5.4);
                ScoreText.DrawCentered(dc, left[i] == 0 ? "T" : left[i].ToString(CultureInfo.InvariantCulture), x, lastStringY + 14.6, 8, brush, FontWeights.SemiBold);
            }
        }
        if (right.Count > 0)
        {
            const string letters = "TIMAC"; // The reference labels the right hand T I M A (thumb, index, middle, annular)
            var text = string.Join(" ", right.Select(f => letters[f].ToString()));
            ScoreText.DrawCentered(dc, text, cx, lastStringY + (left.Count > 0 ? 28 : 14), 8, brush, FontWeights.SemiBold);
        }
    }

    /// <summary>"let ring - - - |" spans, one per run of consecutive LetRing beats inside a bar.</summary>
    private void DrawLetRingSpans(DrawingContext dc, StaffNotationMeasureLayout layout, double measureRight, double tabTop, Color ink, bool hasStaff)
    {
        var brush = ScoreText.Brush(ink);
        var pen = RenderDraw.DashedPen(ink, 1.0, 4.0, 2.5);
        var endPen = RenderDraw.Pen(ink, 1.0);
        var beats = layout.Beats;
        var i = 0;
        while (i < beats.Count)
        {
            if (!beats[i].Cell.Notes.Any(n => n.Techniques.Contains("LetRing"))) { i++; continue; }
            var j = i;
            while (j + 1 < beats.Count && beats[j + 1].Cell.Notes.Any(n => n.Techniques.Contains("LetRing"))) j++;
            var startX = beats[i].CenterX - 4;
            var endX = j + 1 < beats.Count ? beats[j + 1].CenterX - 8 : measureRight - 3;
            var labelY = tabTop - 30;
            var text = ScoreText.MakeTextIn(ScoreTextArea.Technique, "let ring", 8.5, brush);
            // Stacked between the staff and the TAB: above the TAB's own labels, below the staff's ink and dynamics (both voices share the skyline).
            if (hasStaff) labelY = _sky.PlaceAbove(startX, startX + text.Width, 10.5, tabTop - 19) - 1;
            else labelY = _sky.PlaceAbove(startX, Math.Max(endX, startX + text.Width), 10.5, tabTop - 13);   // tab only: the whole line clears accents, technique labels and the P.M. lane
            TabForge.Visualization.Draw.DrawText(dc, text, new Point(startX, labelY));
            endX = Math.Max(endX, startX + text.Width + 4);   // a short span: the end tick sits after the label, not through it
            var lineY = labelY + text.Height / 2;
            if (endX > startX + text.Width + 3) dc.DrawLine(pen, new Point(startX + text.Width + 3, lineY), new Point(endX, lineY));
            dc.DrawLine(endPen, new Point(endX, lineY - 4), new Point(endX, lineY + 4));
            i = j + 1;
        }
    }
}
