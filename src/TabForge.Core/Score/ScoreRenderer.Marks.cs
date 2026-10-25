using System.Globalization;
using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the marks the score renderer draws over the tab: bend arrows and labels, whammy, harmonics, grace notes, fingering and let-ring spans.
// Does not own: the bar furniture (ScoreRenderer.Bars.cs) or the note heads and stems (ScoreRenderer.cs).
// Tests: no named test.
// Length: over 300 lines because the mark kinds share the note geometry and the placement helpers they are drawn with.

// ScoreRenderer: the marks drawn over the tab (bends, whammy, harmonics, grace notes, fingering, let-ring spans).
internal sealed partial class ScoreRenderer
{

    private static IUiPath ArrowHead(IPathFactory paths, UiPoint tip, double dx, double dy, double size = 3.4) => paths.BuildPath(c =>
    {
        // (dx, dy) is the unit direction the arrow points in.
        var back = new UiPoint(tip.X - dx * size * 1.6, tip.Y - dy * size * 1.6);
        var nx = -dy; var ny = dx;
        c.BeginFigure(tip, true, true);
        c.LineTo(new UiPoint(back.X + nx * size * 0.8, back.Y + ny * size * 0.8));
        c.LineTo(new UiPoint(back.X - nx * size * 0.8, back.Y - ny * size * 0.8));
    });

    /// <summary>Curved bend arrow(s) with amounts: bend up, release, pre-bend (vertical) and custom multi-point curves.</summary>
    private static IUiPath Polyline(IPathFactory paths, UiPoint[] corners) => paths.BuildPath(c =>
    {
        c.BeginFigure(corners[0], false, false);
        for (var i = 1; i < corners.Length; i++) c.LineTo(corners[i]);
    });

    private void DrawTabBend(IDrawContext d, TabNote note, double cx, double sy, double halfWidth, double rightLimit, UiColor ink, bool blockedAbove = false)
    {
        var points = ScoreMarkText.EffectiveBendPoints(note);
        if (points.Count > 0) DrawTabBendMarks(d, points, cx, sy, halfWidth, rightLimit, ink, blockedAbove);
    }

    // Length: about 70 lines in one method: the arrows, the curve and the labels share the per-point positions, so splitting them would pass the same locals through parameters.
    /// <summary>Apart from <see cref="DrawTabBend"/> because its curve lambda's closure is allocated on entry: notes without a bend skip it.</summary>
    private void DrawTabBendMarks(IDrawContext d, IReadOnlyList<BendPointModel> points, double cx, double sy, double halfWidth, double rightLimit, UiColor ink, bool blockedAbove)
    {
        var pen = new UiStroke(ink, 1.05);
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
            d.Line(pen, arrowX, baseY, arrowX, top + 3);
            d.Path(ink, null, ArrowHead(d.Paths, new UiPoint(arrowX, top), 0, -1));
            _bendLabelBoxes.Add(new UiRect(arrowX - 3.6, top - 1, 7.2, 7));
            var label = ScoreMarkText.BendAmountLabel(first.Value);
            if (label.Length > 0) DrawBendLabel(d, arrowX, top - 13, label, ink);
            previousValue = first.Value;
            if (points.All(pt => Math.Abs(pt.Value - first.Value) < 0.01)) return;   // a pure pre-bend is just the arrow
            startX = arrowX + 1;                                                       // a release continues from the arrow's tip
            span = Math.Clamp(Math.Min(rightLimit - startX - 3, 18.0 * Math.Max(1, points.Count - 1)), 12, 80);
        }

        var arrows = new List<(UiPoint Tip, double Dx, double Dy)>();
        var labels = new List<(UiPoint At, string Text)>();
        var curve = d.Paths.BuildPath(c =>
        {
            var current = new UiPoint(XOf(points[0].Offset), YOf(previousValue > 0 ? previousValue : points[0].Value));
            c.BeginFigure(current, false, false);
            for (var i = 1; i < points.Count; i++)
            {
                var next = new UiPoint(XOf(points[i].Offset), YOf(points[i].Value));
                if (Math.Abs(next.Y - current.Y) < 0.5) c.LineTo(next);
                else
                {
                    var rising = next.Y < current.Y;
                    c.QuadraticTo(rising ? new UiPoint(next.X, current.Y) : new UiPoint(current.X, next.Y), next);
                    arrows.Add((next, 0, rising ? -1 : 1));
                    if (rising && (i == points.Count - 1 || points[i + 1].Value <= points[i].Value + 0.01))
                    {
                        var text = ScoreMarkText.BendAmountLabel(points[i].Value);
                        if (text.Length > 0) labels.Add((new UiPoint(next.X, next.Y - 13), text));
                    }
                    else if (!rising && points[i].Value > 0.01)
                    {
                        // A release that stops above the note (e.g. back to 1/2) names where it lands, beside the arrow.
                        var text = ScoreMarkText.BendAmountLabel(points[i].Value);
                        if (text.Length > 0) labels.Add((new UiPoint(next.X + 9, next.Y - 8), text));
                    }
                }
                current = next;
            }
        });
        d.Path(null, pen, curve);
        foreach (var (tip, dx, dy) in arrows)
        {
            d.Path(ink, null, ArrowHead(d.Paths, tip, dx, dy, 3.0));
            _bendLabelBoxes.Add(new UiRect(tip.X - 3.4, dy < 0 ? tip.Y - 1 : tip.Y - 6, 6.8, 7));
        }
        var curveBounds = d.Paths.Bounds(curve);
        _bendLabelBoxes.Add(new UiRect(curveBounds.X, curveBounds.Y, curveBounds.Width, curveBounds.Height));
        foreach (var (at, text) in labels) DrawBendLabel(d, at.X, at.Y, text, ink);
    }

    private readonly List<UiRect> _bendLabelBoxes = new();

    private readonly List<(string Text, UiRect Box)> _drawnBendLabels = new();

    private readonly List<(double X, double Y, string Text, UiColor Colour)> _pendingBendLabels = new();

    /// <summary>Queues a bend amount; labels are placed after every curve of the bar is known so none lands on an arrow or another label.</summary>
    private void DrawBendLabel(IDrawContext d, double cx, double y, string text, UiColor colour) =>
        _pendingBendLabels.Add((cx, y, text, colour));

    /// <summary>Draws the queued bend labels, moving each one line up while it would touch an arrowhead, curve or earlier label.</summary>
    private void FlushBendLabels(IDrawContext d)
    {
        foreach (var (x, y, text, colour) in _pendingBendLabels)
        {
            var ft = ScoreText.MakeTextIn(d, ScoreTextArea.Technique, text, 8, colour);
            var rect = new UiRect(x - ft.Width / 2, y, ft.Width, ft.Height - 2);
            // The same amount repeated over a crowded run (notes a few px apart) is printed once: a pile of identical labels says nothing more.
            if (ScoreMarkText.DuplicateBendLabel(_drawnBendLabels, text, rect)) continue;
            for (var guard = 0; guard < 4 && _bendLabelBoxes.Any(r => r.IntersectsWith(rect)); guard++)
                rect = rect with { Y = rect.Y - 9 };
            if (_sky.Count > 0) rect = rect with { Y = _sky.PlaceAbove(rect.X, rect.Right, rect.Height, rect.Bottom) };   // clear of the technique labels, accents and so on stacked over the beat
            _bendLabelBoxes.Add(rect);
            _drawnBendLabels.Add((text, rect));
            ScoreText.DrawFormatted(d, ft, rect.X, rect.Y);
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

    private void DrawTabWhammy(IDrawContext d, TabCell cell, double cx, double rightLimit, double startY, UiColor ink, bool claim = false, double maxDrop = double.PositiveInfinity)
    {
        var points = ScoreMarkText.EffectiveWhammyPoints(cell);
        if (points.Count == 0) return;
        var pen = new UiStroke(ink, 1.0);
        var brush = ink;
        var startX = cx + 7;
        var span = Math.Clamp(rightLimit - startX - 2, 26, 62);
        var unit = 7.5; // pixels per quarter-tone (-2 tones = 60 px)
        var deepest = points.Min(p => p.Value);
        // A deep dive is drawn compressed so it stays inside the system (the labels keep the true amounts).
        if (deepest < 0 && deepest * -unit > maxDrop) unit = Math.Max(0.6, Math.Max(maxDrop, 8) / -deepest);
        UiPoint At(BendPointModel p) => new(startX + Math.Clamp(p.Offset, 0, 60) / 60.0 * span, startY - p.Value * unit);
        // The corners go to the path as an array: a lambda over At would allocate its closure on every call, whammy or not.
        var corners = new UiPoint[points.Count];
        for (var i = 0; i < corners.Length; i++) corners[i] = At(points[i]);
        d.Path(null, pen, Polyline(d.Paths, corners));
        // The curve and its amount labels take their space in the bar's stack so dynamics and other marks keep clear.
        var curveTop = points.Min(p => At(p).Y) - 12; var curveBottom = points.Max(p => At(p).Y) + 12;
        if (claim) _sky.Claim(startX - 2, startX + span + 2, curveTop, curveBottom);
        var placed = new List<UiRect>();
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
            var ft = ScoreText.MakeTextIn(d, ScoreTextArea.Technique, text, 7.5, brush);
            var left = Math.Clamp(pt.X - ft.Width / 2, startX - 2, Math.Max(startX - 2, rightLimit - ft.Width));
            var rect = new UiRect(left, pt.Y + (p.Value < 0 ? 2 : -11), ft.Width, ft.Height - 2);
            if (placed.Any(r => r.IntersectsWith(rect))) { if (text == placedText) continue; rect = rect with { Y = rect.Y + (p.Value < 0 ? 9 : -9) }; }
            if (ScoreMarkText.DuplicateBendLabel(_drawnBendLabels, text, rect)) continue;   // the same amount of a neighbouring beat a few px away
            placed.Add(rect);
            _drawnBendLabels.Add((text, rect));
            placedText = text;
            ScoreText.DrawFormatted(d, ft, rect.X, rect.Y);
        }
    }

    /// <summary>1-3 short slanted slashes stacked over a beat (tremolo picking in TAB).</summary>
    private static void DrawTabTremoloSlashes(IDrawContext d, int count, double cx, double firstY, UiColor ink)
    {
        var pen = new UiStroke(ink, 1.6);
        for (var i = 0; i < count; i++)
            d.Line(pen, cx - 4.5, firstY + i * 3.4 + 2.2, cx + 4.5, firstY + i * 3.4 - 2.2);
    }

    /// <summary>Everything per beat that is not a plain fret number: bends, whammy, trill, tremolo, wah, strokes, brush.</summary>
    private void DrawTabBeatMarks(IDrawContext d, TrackModel track, StaffNotationMeasureLayout layout, StaffNotationBeat beat,
        double measureRight, double tabTop, int strings, UiColor ink, bool hasStaff, double underOffset = 0)
    {
        var cell = beat.Cell;
        var cx = beat.CenterX;
        var next = layout.Beats.Where(b => b.CenterX > cx + 1).OrderBy(b => b.CenterX).FirstOrDefault();
        var rightLimit = next is null ? measureRight - 4 : next.CenterX - 7;
        var brush = ink;
        var topString = cell.Notes.Count == 0 ? 0 : cell.Notes.Min(n => n.StringIndex);
        var topY = tabTop + topString * _host.StringGap;

        foreach (var note in cell.Notes)
        {
            if (note.StringIndex < 0 || note.StringIndex >= strings) continue;
            var sy = tabTop + note.StringIndex * _host.StringGap;
            var label = ScoreMarkText.FretLabelWidthText(note);
            var half = ScoreText.MakeTextIn(d, ScoreTextArea.Fret, label, _host.FretFontSize, brush, 400, "Consolas").Width / 2 + 1;
            if (track.Kind != TrackKind.Drums && (note.Techniques.Contains("Bend") || note.BendPoints.Count > 0 && !note.IsGraceNote) &&
                !ScoreMarkText.IsRepeatedDoubleStopBend(cell, note))
                DrawTabBend(d, note, cx, sy, half, rightLimit, ink, cell.Notes.Any(o => o.StringIndex < note.StringIndex && !o.IsGraceNote));
            if (note.Techniques.Contains("Trill"))
            {
                var fret = ScoreMarkText.TrillFret(track, note);
                if (fret >= 0) ScoreText.DrawIn(ScoreTextArea.Fret, d, $"({fret})", cx + half + 2.6, sy - 6, _host.FretFontSize - 2, brush, 400, "Consolas");
            }
        }

        if (cell.Notes.Any(n => n.Techniques.Any(t => t.StartsWith("TremBar", StringComparison.OrdinalIgnoreCase))) || cell.WhammyPoints.Count > 0)
            DrawTabWhammy(d, cell, cx, rightLimit, topY, ink, !hasStaff, WhammyFloorY(tabTop, strings) - topY);

        var slashes = ScoreMarkText.TremoloSlashCount(cell);
        if (slashes > 0) DrawTabTremoloSlashes(d, slashes, cx, tabTop + cell.Notes.Max(n => n.StringIndex) * _host.StringGap + 11, ink); // The reference: under the fret number

        if (!hasStaff && cell.Notes.Any(n => n.Techniques.Contains("Trill")))
        {
            var trY = _sky.PlaceAbove(cx - 6, Math.Max(cx + 22, rightLimit - 4), 14, tabTop - 10);
            ScoreText.DrawCentered(d, "tr", cx - 4, trY, 9, brush, 600);
            DrawVibratoLine(d, cx + 4, Math.Max(cx + 22, rightLimit - 4), trY + 7, false, brush);
        }

        // Wah: "+" closed / "o" open, with the words "wah" only where the pedal turns on.
        var wahClose = cell.Notes.Any(n => n.Techniques.Contains("WahClose"));
        var wahOpen = cell.Notes.Any(n => n.Techniques.Contains("WahOpen"));
        if ((wahClose || wahOpen) && !hasStaff) // with a staff the +/o is engraved over the notation
            ScoreText.DrawCentered(d, wahClose ? "+" : "o", cx, _sky.PlaceAbove(cx - 5, cx + 5, 12, tabTop - 10), 10, brush, 700);

        DrawPickStroke(d, cell, cx, tabTop, strings, ink);

        DrawBrushArrow(d, cell, cx, tabTop, topY, ink);
        if (cell.Notes.Any(n => n.Techniques.Contains("Rasgueado")))
            ScoreText.DrawCentered(d, "rasg.", cx, hasStaff ? tabTop - 22 : _sky.PlaceAbove(cx - 13, cx + 13, 11, tabTop - 10), 8.5, brush);
        var lastStringY = tabTop + (strings - 1) * _host.StringGap + underOffset + ScoreMarkText.PickStrokeShift(cell);
        var harmonicLift = DrawHarmonicFrets(d, cell, cx, lastStringY, ink, measureRight - 2);
        DrawTabFingering(d, cell, cx, lastStringY + harmonicLift, ink);
    }

    /// <summary>Pick strokes under the TAB: down = a square bracket, up = a V.</summary>
    private void DrawPickStroke(IDrawContext d, TabCell cell, double cx, double tabTop, int strings, UiColor ink)
    {
        var stroke = cell.Notes.Any(n => n.Techniques.Contains("PickDown")) ? 1 : cell.Notes.Any(n => n.Techniques.Contains("PickUp")) ? 2 : 0;
        if (stroke != 0)
        {
            var y = tabTop + (strings - 1) * _host.StringGap + 8 + ScoreMarkText.PickStrokeLift;
            var pen = new UiStroke(ink, 1.1);
            if (stroke == 1)
            {
                d.Line(pen, cx - 3.5, y + 6, cx - 3.5, y);
                d.Line(pen, cx - 3.5, y, cx + 3.5, y);
                d.Line(pen, cx + 3.5, y, cx + 3.5, y + 6);
            }
            else
            {
                d.Line(pen, cx - 3.5, y, cx, y + 6);
                d.Line(pen, cx, y + 6, cx + 3.5, y);
            }
        }
    }

    /// <summary>Brush / arpeggio: an arrow running along the chord just left of the numbers.</summary>
    private void DrawBrushArrow(IDrawContext d, TabCell cell, double cx, double tabTop, double topY, UiColor ink)
    {
        var brushDown = cell.Notes.Any(n => n.Techniques.Contains("BrushDown") || n.Techniques.Contains("ArpeggioDown"));
        var brushUp = cell.Notes.Any(n => n.Techniques.Contains("BrushUp") || n.Techniques.Contains("ArpeggioUp"));
        if ((brushDown || brushUp) && cell.Notes.Count > 0)
        {
            var bottomString = cell.Notes.Max(n => n.StringIndex);
            var x = cx - 10;
            var y1 = topY - 3;
            var y2 = tabTop + bottomString * _host.StringGap + 3;
            if (y2 < y1 + 8) y2 = y1 + 8;
            var pen = new UiStroke(ink, 1.1);
            var arpeggio = cell.Notes.Any(n => n.Techniques.Contains("ArpeggioDown") || n.Techniques.Contains("ArpeggioUp"));
            if (arpeggio)
            {
                var g = d.Paths.BuildPath(c =>
                {
                    c.BeginFigure(new UiPoint(x, y1), false, false);
                    for (var y = y1; y < y2; y += 4) c.LineTo(new UiPoint(x + (((int)((y - y1) / 4) % 2) == 0 ? 2 : -2), y + 2));
                });
                d.Path(null, pen, g);
            }
            else d.Line(pen, x, y1, x, y2);
            // GP5 draws the arrow the way the stroke crosses the TAB: a downstroke (bass string first) runs from the bottom line up, so its head is at the top.
            d.Path(ink, null, brushDown ? ArrowHead(d.Paths, new UiPoint(x, y1 - 2), 0, -1, 2.8) : ArrowHead(d.Paths, new UiPoint(x, y2 + 2), 0, 1, 2.8));
        }
    }

    /// <summary>Draws the harmonic values under the TAB; returns the height taken so fingering moves below it.</summary>
    private static double DrawHarmonicFrets(IDrawContext d, TabCell cell, double cx, double lastStringY, UiColor ink, double rightLimit = double.PositiveInfinity)
    {
        var parts = cell.Notes.OrderByDescending(n => n.StringIndex).Select(ScoreMarkText.HarmonicFretText).Where(t => t.Length > 0).ToList();
        if (parts.Count == 0) return 0;
        // Stays inside its bar (a beat at the very end of a system would otherwise straddle the bar line).
        var ft = ScoreText.MakeText(d, string.Join(" ", parts), 8, ink, 600);
        var left = Math.Min(cx - ft.Width / 2, rightLimit - ft.Width);
        ScoreText.DrawFormatted(d, ft, left, lastStringY + 9);
        return ScoreMarkText.HarmonicRowHeight;
    }

    /// <summary>The line or arc between a grace fret and its main note, as the reference draws it: a slide or hammer-on / pull-off is an arc above the numbers (GP5), a bend grace a rising arrow.</summary>
    private void DrawTabGraceTransition(IDrawContext d, TabCell cell, TabNote grace, double graceX, double graceWidth, double mainX, double graceY, double tabTop, UiColor ink)
    {
        var slide = grace.Techniques.Contains("Slide") || grace.Techniques.Contains("ShiftSlide") || grace.Techniques.Contains("LegatoSlide");
        var hammer = grace.Techniques.Contains("HOPO") || grace.Techniques.Contains("HOPOOrigin");
        var bend = grace.Techniques.Contains("GraceBend") || cell.Notes.Any(o => o.Techniques.Contains("GraceBend"));
        if (!slide && !hammer && !bend) return;
        var main = cell.Notes.Where(o => !o.IsGraceNote).OrderBy(o => o.StringIndex == grace.StringIndex ? 0 : 1).ThenBy(o => Math.Abs(o.StringIndex - grace.StringIndex)).First();
        var mainLabel = ScoreMarkText.FretLabelWidthText(main);
        var mainWidth = ScoreText.MakeTextIn(d, ScoreTextArea.Fret, mainLabel, _host.FretFontSize, ink, 400, "Consolas").Width;
        var startX = graceX + graceWidth / 2 + 1.5;
        var endX = mainX - mainWidth / 2 - 1.5;
        if (endX - startX < 2) return;
        var mainY = tabTop + main.StringIndex * _host.StringGap;
        var pen = new UiStroke(ink, 1.05);
        if (bend && !slide)
        {
            // A bent grace note: a line rising from the grace fret to the main note, with an arrowhead at its end.
            var from = new UiPoint(startX, graceY - 1.5);
            var to = new UiPoint(endX, mainY - 4.5);
            d.Line(pen, from.X, from.Y, to.X, to.Y);
            var len = Math.Max(1, Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y)));
            d.Path(ink, null, ArrowHead(d.Paths, to, (to.X - from.X) / len, (to.Y - from.Y) / len, 2.2));
        }
        else
        {
            // GP5 draws a grace slide transition the same as a hammer-on: an arc over the two numbers (no slide line).
            var y = Math.Min(graceY, mainY) - 8.5;
            var arc = Math.Clamp((endX - startX) * 0.35, 2, 4);
            var (from, peak, to) = (new UiPoint(startX, y), new UiPoint((startX + endX) / 2, y - arc * 2), new UiPoint(endX, y)); // block locals: the other branches allocate no closure
            var g = d.Paths.BuildPath(c =>
            {
                c.BeginFigure(from, false, false);
                c.QuadraticTo(peak, to);
            });
            d.Path(null, pen, g);
        }
    }

    /// <summary>Left-hand finger numbers (circled, T = thumb) and right-hand p i m a c letters under the TAB.</summary>
    private static void DrawTabFingering(IDrawContext d, TabCell cell, double cx, double lastStringY, UiColor ink)
    {
        var left = cell.Notes.Where(n => n.LeftHandFinger is >= 0 and <= 4).OrderBy(n => n.StringIndex).Select(n => n.LeftHandFinger!.Value).ToList();
        var right = cell.Notes.Where(n => n.RightHandFinger is >= 0 and <= 4).OrderByDescending(n => n.StringIndex).Select(n => n.RightHandFinger!.Value).ToList();
        if (left.Count == 0 && right.Count == 0) return;
        var brush = ink;
        var ringPen = new UiStroke(ink, 0.8);
        if (left.Count > 0)
        {
            var startX = cx - (left.Count - 1) * 5.5;
            for (var i = 0; i < left.Count; i++)
            {
                var x = startX + i * 11;
                d.Ellipse(null, ringPen, x, lastStringY + 20, 5.4, 5.4);
                ScoreText.DrawCentered(d, left[i] == 0 ? "T" : left[i].ToString(CultureInfo.InvariantCulture), x, lastStringY + 14.6, 8, brush, 600);
            }
        }
        if (right.Count > 0)
        {
            const string letters = "TIMAC"; // The reference labels the right hand T I M A (thumb, index, middle, annular)
            var text = string.Join(" ", right.Select(f => letters[f].ToString()));
            ScoreText.DrawCentered(d, text, cx, lastStringY + (left.Count > 0 ? 28 : 14), 8, brush, 600);
        }
    }

    /// <summary>"let ring - - - |" spans, one per run of consecutive LetRing beats inside a bar.</summary>
    private void DrawLetRingSpans(IDrawContext d, StaffNotationMeasureLayout layout, double measureRight, double tabTop, UiColor ink, bool hasStaff)
    {
        var brush = ink;
        var pen = new UiStroke(ink, 1.0, Dash: 4.0, Gap: 2.5);
        var endPen = new UiStroke(ink, 1.0);
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
            var text = ScoreText.MakeTextIn(d, ScoreTextArea.Technique, "let ring", 8.5, brush);
            // Stacked between the staff and the TAB: above the TAB's own labels, below the staff's ink and dynamics (both voices share the skyline).
            if (hasStaff) labelY = _sky.PlaceAbove(startX, startX + text.Width, 10.5, tabTop - 19) - 1;
            else labelY = _sky.PlaceAbove(startX, Math.Max(endX, startX + text.Width), 10.5, tabTop - 13);   // tab only: the whole line clears accents, technique labels and the P.M. lane
            ScoreText.DrawFormatted(d, text, startX, labelY);
            var lineY = labelY + text.Height / 2;
            if (j > i && endX > startX + text.Width + 3)   // one beat, or a span no longer than its label, is the label alone (the reference draws no dashes or end tick)
            {
                d.Line(pen, startX + text.Width + 3, lineY, endX, lineY);
                d.Line(endPen, endX, lineY - 4, endX, lineY + 4);
            }
            i = j + 1;
        }
    }
}
