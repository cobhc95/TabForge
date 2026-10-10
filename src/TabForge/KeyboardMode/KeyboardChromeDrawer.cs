using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Views.Rendering;

namespace TabForge.KeyboardMode;

// Owns: drawing what stands still or changes on events in the keyboard view: the panel with the octave and white-key guide lines, the key strip with its hit line, the hand legend,
//   the score panel, the "Waiting for you" cue, the song progress line and the grade flash text (the lit keys: KeyboardKeyPainter).
// Does not own: the moving notes (KeyboardPageDrawer), when each layer is drawn (KeyboardModeView), the colours (KeyboardColours) or the state (KeyboardFeedback, KeyboardModeScore).
// Tests: TestKeyboardModeKeyView (capture: docs/CAPTURE_SCRIPT.md, step learn-playing with "keys").
internal static class KeyboardChromeDrawer
{
    public const string IdleHint = "Press Play: the notes fall onto the keys";
    public const string WaitText = "Waiting for you: play the outlined keys";
    public const string LeftText = "Left hand", RightText = "Right hand";
    private const double TextSize = 11, Margin = 8;
    private static readonly Typeface Face = new("Segoe UI Semibold");

    internal static FormattedText Text(string text, double size, Brush brush, double pixelsPerDip) =>
        PixelSnap.Text(text, Face, size, brush, pixelsPerDip);

    /// <summary>The panel and the guide lines of the falling area: a faint line at every white key's left edge and a stronger one at each C; the hint text when there is one.</summary>
    public static void Back(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, string hint, double pixelsPerDip)
    {
        var col = pal.Keyboard;
        dc.DrawRectangle(pal.Panel, null, new Rect(0, 0, lay.Width, lay.Height));
        for (var m = lay.Lowest; m <= lay.Highest; m++)
        {
            if (!KeyboardModeLayout.IsWhite(m)) continue;
            var x = lay.KeyX(m);
            dc.DrawLine(m % 12 == 0 ? col.Octave : col.Guide, new Point(x, 0), new Point(x, lay.StripTop));
        }
        dc.DrawLine(col.Guide, new Point(lay.Right, 0), new Point(lay.Right, lay.StripTop));
        if (hint.Length == 0) return;
        var text = Text(hint, 13, pal.Label, pixelsPerDip);
        text.MaxTextWidth = Math.Max(40, lay.Width - 40);
        text.TextAlignment = TextAlignment.Center;
        dc.DrawText(text, new Point(20, lay.StripTop / 2 - text.Height / 2));
    }

    /// <summary>The key strip: white keys, black keys, a note name on every C, and the hit line along the top edge (drawn over the notes).</summary>
    public static void Strip(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, double pixelsPerDip)
    {
        var col = pal.Keyboard;
        dc.DrawRectangle(pal.Strip, null, new Rect(0, lay.StripTop, lay.Width, lay.StripHeight));
        for (var m = lay.Lowest; m <= lay.Highest; m++)
        {
            if (!KeyboardModeLayout.IsWhite(m)) continue;
            dc.DrawRectangle(col.White, col.Seam, lay.KeyRect(m));
            if (m % 12 != 0 || lay.WhiteWidth < 14) continue;
            var label = Text("C" + (m / 12 - 1).ToString(CultureInfo.InvariantCulture), Math.Clamp(lay.WhiteWidth * 0.34, 8, 12), col.KeyLabel, pixelsPerDip);
            dc.DrawText(label, new Point(lay.KeyCentre(m) - label.Width / 2, lay.StripTop + lay.StripHeight - label.Height - 3));
        }
        for (var m = lay.Lowest; m <= lay.Highest; m++)
            if (!KeyboardModeLayout.IsWhite(m)) dc.DrawRoundedRectangle(col.Black, null, lay.KeyRect(m), 2, 2);
        dc.DrawRectangle(pal.HitLine, null, new Rect(lay.Left, lay.StripTop - 2, lay.Right - lay.Left, 3));
    }

    /// <summary>The hand legend at the top left; with play-along the score panel at the top right; while the song waits the cue below them.</summary>
    public static void Hud(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, KeyboardModeScore? score, bool waiting, double pixelsPerDip)
    {
        var col = pal.Keyboard;
        var left = Text(LeftText, TextSize, pal.Label, pixelsPerDip);
        var right = Text(RightText, TextSize, pal.Label, pixelsPerDip);
        var legend = new Rect(Margin, Margin + ProgressHeight, 22 + Math.Max(left.Width, right.Width) + 10, left.Height + right.Height + 14);
        dc.DrawRoundedRectangle(col.Backdrop, col.BackdropEdge, legend, 5, 5);
        dc.DrawRoundedRectangle(col.Left, col.LeftEdge, new Rect(legend.X + 7, legend.Y + 6 + (left.Height - 10) / 2, 10, 10), 2, 2);
        dc.DrawText(left, new Point(legend.X + 22, legend.Y + 6));
        dc.DrawRoundedRectangle(col.Right, col.RightEdge, new Rect(legend.X + 7, legend.Y + 7 + left.Height + (right.Height - 10) / 2, 10, 10), 2, 2);
        dc.DrawText(right, new Point(legend.X + 22, legend.Y + 7 + left.Height));
        if (score is not null) ScorePanel(dc, lay, pal, score, pixelsPerDip);
        if (waiting) WaitCue(dc, lay, pal, legend, pixelsPerDip);
    }

    private static void ScorePanel(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, KeyboardModeScore score, double pixelsPerDip)
    {
        var col = pal.Keyboard;
        var big = Text(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(score.AccuracyPercent)}%"), 24, pal.Label, pixelsPerDip);
        var caption = Text("accuracy", TextSize, pal.Label, pixelsPerDip);
        var rest = Text(string.Create(CultureInfo.InvariantCulture, $"Streak {score.Streak} (best {score.BestStreak})\nHits {score.Hits}   Misses {score.Misses}"), TextSize + 1, pal.Label, pixelsPerDip);
        var w = Math.Max(big.Width + 6 + caption.Width, rest.Width) + 20;
        var box = new Rect(lay.Width - Margin - w, Margin + ProgressHeight, w, big.Height + rest.Height + 14);
        dc.DrawRoundedRectangle(col.Backdrop, col.BackdropEdge, box, 5, 5);
        dc.DrawText(big, new Point(box.X + 10, box.Y + 4));
        dc.DrawText(caption, new Point(box.X + 16 + big.Width, box.Y + 4 + big.Height - caption.Height - 4));
        dc.DrawText(rest, new Point(box.X + 10, box.Y + 8 + big.Height));
    }

    public const double ProgressHeight = 4;

    /// <summary>The song's progress: a thin line along the top of the view, filled to <paramref name="fraction"/>.</summary>
    public static void Progress(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, double fraction)
    {
        dc.DrawRectangle(pal.Keyboard.BackdropEdge.Brush, null, new Rect(0, 0, lay.Width, ProgressHeight));
        dc.DrawRectangle(pal.HitLine, null, new Rect(0, 0, lay.Width * Math.Clamp(fraction, 0, 1), ProgressHeight));
    }

    private static void WaitCue(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, Rect legend, double pixelsPerDip)
    {
        // Beside the hand legend, in the top margin row, so it never covers the notes falling in the middle.
        var col = pal.Keyboard;
        var text = Text(WaitText, 13, pal.Label, pixelsPerDip);
        var w = text.Width + 44;
        var box = new Rect(legend.Right + Margin, legend.Y, w, Math.Max(legend.Height, text.Height + 14));
        // Too narrow for the side spot (the score panel sits at the right): drop below the legend.
        if (box.Right > lay.Width - 200) box = new Rect(Margin, legend.Bottom + Margin, w, text.Height + 14);
        dc.DrawRoundedRectangle(col.Backdrop, col.WaitPen, box, 6, 6);
        // A pause sign, so the cue is not told by colour or words alone.
        var top = box.Y + (box.Height - (text.Height - 4)) / 2;
        dc.DrawRectangle(col.WaitPen.Brush, null, new Rect(box.X + 12, top, 4, text.Height - 4));
        dc.DrawRectangle(col.WaitPen.Brush, null, new Rect(box.X + 20, top, 4, text.Height - 4));
        dc.DrawText(text, new Point(box.X + 32, box.Y + (box.Height - text.Height) / 2));
    }

    /// <summary>The grade flash above the strip, centred over the note's key (kept inside the view).</summary>
    public static void Grade(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, KeyGradeKind kind, int key, double pixelsPerDip)
    {
        var col = pal.Keyboard;
        var text = Text(KeyboardFeedback.TextOf(kind), 20, col.TextOf(kind), pixelsPerDip);
        var w = text.Width + 24;
        var centre = lay.Has(key) ? lay.KeyCentre(key) : lay.Width / 2;
        var box = new Rect(Math.Clamp(centre - w / 2, Margin, Math.Max(Margin, lay.Width - Margin - w)), lay.StripTop - text.Height - 26, w, text.Height + 10);
        var edge = new Pen(col.TextOf(kind), 1.5);
        edge.Freeze();
        dc.DrawRoundedRectangle(col.Backdrop, edge, box, 6, 6);
        dc.DrawText(text, new Point(box.X + 12, box.Y + 5));
    }
}
