using System;
using System.Globalization;
using TabForge.KeyboardMode;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Draws what stands still or changes on events in the keyboard view: the panel with the octave and white-key guide lines and the hint, the key strip with its hit line, the lit keys (due, held right or extra,
/// awaited), the hand legend, the score panel, the "Waiting for you" cue, the song progress line and the grade flash.
/// Owns: the drawing of those layers and the cache of key outlines as paths (key: key number; ceiling: 128 entries; rebuilt when the geometry object changes; thread: the view's UI thread).
/// Does not own: the moving notes (KeyboardNotesPainter), the state (KeyboardFeedback, KeyboardModeScore, KeyboardModeWaitMode), the numbers (KeyboardStripGeometry) or the colours (KeyboardInk).
/// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests.
/// </summary>
public sealed class KeyboardChromePainter
{
    public const string IdleHint = "Press Play: the notes fall onto the keys";
    public const string WaitText = "Waiting for you: play the outlined keys";
    public const string LeftText = "Left hand", RightText = "Right hand";
    public const double ProgressHeight = 4;
    private const double TextSize = 11, Margin = 8, LitSlices = 8, MaxGlowWidth = 7, MinGlowWidth = 2;
    private const string Family = KeyboardNotesPainter.TextFamily;
    private const int Weight = KeyboardNotesPainter.TextWeight;

    private readonly IUiPath?[] _shapes = new IUiPath?[128];
    private KeyboardStripGeometry? _shapesFor;

    /// <summary>The panel and the guide lines of the falling area: a faint line at every white key's left edge and a stronger one at each C; the hint text when there is one.</summary>
    public void Back(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, string hint)
    {
        d.Rect(ink.Panel, null, 0, 0, g.Width, g.Height);
        for (var m = g.Lowest; m <= g.Highest; m++)
        {
            if (!KeyboardGeometryRules.IsWhite(m)) continue;
            var x = g.KeyX(m);
            d.Line(new UiStroke(m % 12 == 0 ? ink.Octave : ink.GuideLine, m % 12 == 0 ? 1.2 : 1), x, 0, x, g.StripTop);
        }
        d.Line(new UiStroke(ink.GuideLine, 1), g.Right, 0, g.Right, g.StripTop);
        if (hint.Length == 0) return;
        var text = d.Text(hint, Family, 13, ink.Label, d.PixelsPerDip, Math.Max(40, g.Width - 40), UiTextAlign.Centre, Weight);
        d.DrawText(text, 20, g.StripTop / 2 - text.Height / 2);
    }

    /// <summary>The key strip: white keys, black keys, a note name on every C, and the hit line along the top edge (drawn over the notes).</summary>
    public void Strip(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink)
    {
        d.Rect(ink.Strip, null, 0, g.StripTop, g.Width, g.StripHeight);
        var seam = new UiStroke(ink.Seam, 1);
        for (var m = g.Lowest; m <= g.Highest; m++)
        {
            if (!KeyboardGeometryRules.IsWhite(m)) continue;
            d.Rect(ink.WhiteKey, seam, g.KeyX(m), g.StripTop, g.WhiteWidth, g.StripHeight);
            if (m % 12 != 0 || g.WhiteWidth < 14) continue;
            var label = d.Text("C" + (m / 12 - 1).ToString(CultureInfo.InvariantCulture), Family, Math.Clamp(g.WhiteWidth * 0.34, 8, 12), ink.KeyLabel, d.PixelsPerDip, weight: Weight);
            d.DrawText(label, g.KeyCentre(m) - label.Width / 2, g.StripTop + g.StripHeight - label.Height - 3);
        }
        for (var m = g.Lowest; m <= g.Highest; m++)
            if (!KeyboardGeometryRules.IsWhite(m)) d.Rect(ink.BlackKey, null, g.KeyX(m), g.StripTop, g.KeyWidth(m), g.BlackHeight, 2);
        d.Rect(ink.HitLine, null, g.Left, g.StripTop - 2, g.Right - g.Left, 3);
    }

    /// <summary>The lit keys, each filled within its own outline: held right (green, a check), extra (red, a cross), held while nothing is judged or due (the hand's colour), due, and the awaited outline. White keys first.</summary>
    public void Keys(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, KeyboardFeedback? feedback, KeyMask dueLeft, KeyMask dueRight, KeyMask awaited)
    {
        var glow = new UiStroke(ink.Glow, Math.Clamp(g.WhiteWidth * 0.22, MinGlowWidth, MaxGlowWidth) * 2);   // twice the visible width: the outer half is clipped away
        var wait = new UiStroke(ink.Wait, 6);
        var seam = new UiStroke(ink.Seam, 1);
        for (var pass = 0; pass < 2; pass++)
            for (var m = g.Lowest; m <= g.Highest; m++)
            {
                if (KeyboardGeometryRules.IsWhite(m) != (pass == 0)) continue;
                var held = feedback?.HeldOf(m) ?? KeyHold.None;
                var left = dueLeft.Has(m);
                var due = left || dueRight.Has(m);
                var isAwaited = awaited.Has(m);
                if (held == KeyHold.None && !due && !isAwaited) continue;
                UiColor? fill = held switch
                {
                    KeyHold.Correct => ink.Ok,
                    KeyHold.Extra => ink.Extra,
                    KeyHold.Held => ink.HandFill(due ? left : m < KeyboardHands.MiddleC),
                    _ => due ? ink.HandFill(left) : null,
                };
                var shape = ShapeOf(d, g, m);
                d.PushClip(shape);
                if (fill is { } c)
                {
                    LitFill(d, g, c);
                    d.Path(null, glow, shape);
                }
                if (isAwaited) d.Path(null, wait, shape);
                d.Path(null, seam, shape);
                d.Pop();
                Mark(d, g, ink, m, held);
            }
    }

    /// <summary>A lit key's colour, brighter where the note lands (the key's top) and a little deeper at its front, in thin slices.</summary>
    private static void LitFill(IDrawContext d, KeyboardStripGeometry g, UiColor c)
    {
        // White and Black are base tokens with one value in both themes, so the dark read is the value.
        var top = ThemeColours.Blend(c, UiTokens.Colour("White", light: false), 0.55);
        var bottom = ThemeColours.Blend(c, UiTokens.Colour("Black", light: false), 0.18);
        var h = g.StripHeight / LitSlices;
        for (var i = 0; i < LitSlices; i++)
        {
            var t = (i + 0.5) / LitSlices;
            var colour = t < 0.45 ? ThemeColours.Blend(top, c, t / 0.45) : ThemeColours.Blend(c, bottom, (t - 0.45) / 0.55);
            d.Rect(colour, null, g.Left, g.StripTop + i * h, g.Right - g.Left, h + 0.5);
        }
    }

    private IUiPath ShapeOf(IDrawContext d, KeyboardStripGeometry g, int midi)
    {
        if (!ReferenceEquals(_shapesFor, g)) { Array.Clear(_shapes); _shapesFor = g; }
        if (_shapes[midi] is { } done) return done;
        Span<UiPoint> points = stackalloc UiPoint[8];
        var n = g.KeyOutline(midi, points);
        var first = points[0];
        var list = points[..n].ToArray();
        return _shapes[midi] = d.Paths.BuildPath(sink =>
        {
            sink.BeginFigure(first, true, true);
            for (var i = 1; i < list.Length; i++) sink.LineTo(list[i]);
        });
    }

    /// <summary>A check on a right key, a cross on an extra one, near the key's front (inside its outline).</summary>
    private static void Mark(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, int m, KeyHold held)
    {
        if (held is not (KeyHold.Correct or KeyHold.Extra)) return;
        var white = KeyboardGeometryRules.IsWhite(m);
        double x = g.KeyX(m), w = g.KeyWidth(m), h = white ? g.StripHeight : g.BlackHeight;
        var r = Math.Clamp(w * 0.24, 3, 8);
        double cx = x + w / 2, cy = g.StripTop + h - r - Math.Min(h * 0.12, white ? 18 : 6);
        var pen = new UiStroke(ink.Mark, 2);
        if (held == KeyHold.Correct)
        {
            d.Line(pen, cx - r, cy, cx - r * 0.3, cy + r * 0.7);
            d.Line(pen, cx - r * 0.3, cy + r * 0.7, cx + r, cy - r * 0.7);
        }
        else
        {
            d.Line(pen, cx - r * 0.8, cy - r * 0.8, cx + r * 0.8, cy + r * 0.8);
            d.Line(pen, cx - r * 0.8, cy + r * 0.8, cx + r * 0.8, cy - r * 0.8);
        }
    }

    /// <summary>The hand legend at the top left; with play-along the score panel at the top right; while the song waits the cue below them.</summary>
    public void Hud(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, KeyboardModeScore? score, bool waiting)
    {
        var dpi = d.PixelsPerDip;
        var left = d.Text(LeftText, Family, TextSize, ink.Label, dpi, weight: Weight);
        var right = d.Text(RightText, Family, TextSize, ink.Label, dpi, weight: Weight);
        var edge = new UiStroke(ink.BackdropEdge, 1);
        double lx = Margin, ly = Margin + ProgressHeight, lw = 22 + Math.Max(left.Width, right.Width) + 10, lh = left.Height + right.Height + 14;
        d.Rect(ink.Backdrop, edge, lx, ly, lw, lh, 5);
        d.Rect(ink.Left, new UiStroke(ink.LeftEdge, 1), lx + 7, ly + 6 + (left.Height - 10) / 2, 10, 10, 2);
        d.DrawText(left, lx + 22, ly + 6);
        d.Rect(ink.Right, new UiStroke(ink.RightEdge, 1), lx + 7, ly + 7 + left.Height + (right.Height - 10) / 2, 10, 10, 2);
        d.DrawText(right, lx + 22, ly + 7 + left.Height);
        if (score is not null) ScorePanel(d, g, ink, score);
        if (waiting) WaitCue(d, g, ink, lx, ly, lw, lh);
    }

    private static void ScorePanel(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, KeyboardModeScore score)
    {
        var dpi = d.PixelsPerDip;
        var big = d.Text(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(score.AccuracyPercent)}%"), Family, 24, ink.Label, dpi, weight: Weight);
        var caption = d.Text("accuracy", Family, TextSize, ink.Label, dpi, weight: Weight);
        var rest = d.Text(string.Create(CultureInfo.InvariantCulture, $"Streak {score.Streak} (best {score.BestStreak})\nHits {score.Hits}   Misses {score.Misses}"), Family, TextSize + 1, ink.Label, dpi, weight: Weight);
        var w = Math.Max(big.Width + 6 + caption.Width, rest.Width) + 20;
        double x = g.Width - Margin - w, y = Margin + ProgressHeight;
        d.Rect(ink.Backdrop, new UiStroke(ink.BackdropEdge, 1), x, y, w, big.Height + rest.Height + 14, 5);
        d.DrawText(big, x + 10, y + 4);
        d.DrawText(caption, x + 16 + big.Width, y + 4 + big.Height - caption.Height - 4);
        d.DrawText(rest, x + 10, y + 8 + big.Height);
    }

    /// <summary>The song's progress: a thin line along the top of the view, filled to <paramref name="fraction"/>.</summary>
    public static void Progress(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, double fraction)
    {
        d.Rect(ink.BackdropEdge, null, 0, 0, g.Width, ProgressHeight);
        d.Rect(ink.HitLine, null, 0, 0, g.Width * Math.Clamp(fraction, 0, 1), ProgressHeight);
    }

    private static void WaitCue(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, double legendX, double legendY, double legendW, double legendH)
    {
        // Beside the hand legend, in the top margin row, so it never covers the notes falling in the middle.
        var text = d.Text(WaitText, Family, 13, ink.Label, d.PixelsPerDip, weight: Weight);
        var w = text.Width + 44;
        double x = legendX + legendW + Margin, y = legendY, h = Math.Max(legendH, text.Height + 14);
        // Too narrow for the side spot (the score panel sits at the right): drop below the legend.
        if (x + w > g.Width - 200) { x = Margin; y = legendY + legendH + Margin; h = text.Height + 14; }
        d.Rect(ink.Backdrop, new UiStroke(ink.Wait, 3), x, y, w, h, 6);
        // A pause sign, so the cue is not told by colour or words alone.
        var top = y + (h - (text.Height - 4)) / 2;
        d.Rect(ink.Wait, null, x + 12, top, 4, text.Height - 4);
        d.Rect(ink.Wait, null, x + 20, top, 4, text.Height - 4);
        d.DrawText(text, x + 32, y + (h - text.Height) / 2);
    }

    /// <summary>The grade flash above the strip, centred over the note's key (kept inside the view), at <paramref name="alpha"/> opacity.</summary>
    public static void Grade(IDrawContext d, KeyboardStripGeometry g, KeyboardInk ink, KeyGradeKind kind, int key, double alpha)
    {
        if (alpha <= 0) return;
        var colour = ink.GradeColour(kind);
        var text = d.Text(KeyboardFeedback.TextOf(kind), Family, 20, colour, d.PixelsPerDip, weight: Weight);
        var w = text.Width + 24;
        var centre = g.Has(key) ? g.KeyCentre(key) : g.Width / 2;
        var x = Math.Clamp(centre - w / 2, Margin, Math.Max(Margin, g.Width - Margin - w));
        var y = g.StripTop - text.Height - 26;
        if (alpha < 1) d.PushOpacity(alpha);
        d.Rect(ink.Backdrop, new UiStroke(colour, 1.5), x, y, w, text.Height + 10, 6);
        d.DrawText(text, x + 12, y + 5);
        if (alpha < 1) d.Pop();
    }
}
