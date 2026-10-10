using System.Windows;
using System.Windows.Media;
using TabForge.Views.Rendering;

namespace TabForge.KeyboardMode;

/// <summary>What the notes page shows besides the bars: the hands filter, note names and finger numbers.</summary>
internal readonly record struct KeyboardPageOptions(KeyboardHandsFilter Hands, bool Names, bool Fingers)
{
    public static KeyboardPageOptions Default => new(KeyboardHandsFilter.Both, true, true);
}

// Owns: drawing the notes of one page of the keyboard view: a rounded bar per note exactly over its key (its key's width, a black-key bar narrower), from its end (top) to its start (bottom), in its
//   hand's colour with a bright line at the start, the other hand faded or left out by the hands filter, and when the bar has room its finger number (a disc) and its note name (F#3) in a colour that
//   reads on the hand colour. Also the marks layer: a green edge and a check on a bar that was hit, a red edge and a cross on one that was missed.
//   A page is laid out from y = 0 (its last moment) downward: y = (page end - virtual time) * pixels per ms; the view moves it (and the marks) down with one transform as the song plays.
// Does not own: when a page is drawn (KeyboardModeView), the keys and overlays (KeyboardChromeDrawer, KeyboardKeyPainter), the notes or the judge.
// Tests: TestKeyboardModeKeyView (capture: docs/CAPTURE_SCRIPT.md, step learn-playing with "keys").
internal static class KeyboardPageDrawer
{
    public const double MinBarHeight = 10;
    private const double WhiteInset = 1.5, MaxRadius = 7, MinNameHeight = 20, MinNameWidth = 16;

    /// <summary>A note's bar on the page (whole device pixels).</summary>
    public static Rect BarOf(KeyboardModePlaced placed, double pageEndMs, KeyboardModeLayout lay, double pixelsPerDip)
    {
        var key = placed.Note.Fret;
        var inset = KeyboardModeLayout.IsWhite(key) ? WhiteInset : 0;
        var x = PixelSnap.Snap(lay.KeyX(key) + inset, pixelsPerDip);
        var w = Math.Max(3, lay.KeyWidth(key) - 2 * inset);
        var bottom = PixelSnap.Snap((pageEndMs - placed.VirtualMs) * lay.PxPerMs, pixelsPerDip);
        var h = PixelSnap.Snap(Math.Max(MinBarHeight, placed.Note.DurationMs * lay.PxPerMs), pixelsPerDip);
        return new Rect(x, bottom - h, w, h);
    }

    private static double RadiusOf(Rect bar) => Math.Min(MaxRadius, Math.Min(bar.Width, bar.Height) / 2.5);

    /// <summary>Draws <paramref name="notes"/> (sorted by virtual onset) with the page's last moment <paramref name="pageEndMs"/> at y = 0.</summary>
    public static void Draw(DrawingContext dc, IReadOnlyList<KeyboardModePlaced> notes, double pageEndMs, KeyboardModeLayout lay, KeyboardColours col, KeyboardPageOptions options, double pixelsPerDip)
    {
        // White-key notes first so the narrower black-key notes sit on top where a chord is close.
        for (var pass = 0; pass < 2; pass++)
            foreach (var placed in notes)
            {
                var note = placed.Note;
                var key = note.Fret;
                if (!lay.Has(key) || KeyboardModeLayout.IsWhite(key) != (pass == 0)) continue;
                var opacity = KeyboardHands.OpacityOf(options.Hands, note.LeftHand);
                if (opacity <= 0) continue;
                if (opacity < 1) dc.PushOpacity(opacity);
                var bar = BarOf(placed, pageEndMs, lay, pixelsPerDip);
                var radius = RadiusOf(bar);
                dc.DrawRoundedRectangle(col.HandBrush(note.LeftHand), col.HandEdge(note.LeftHand), bar, radius, radius);
                dc.DrawLine(col.Cap, new Point(bar.X + radius * 0.6, bar.Bottom - 1.5), new Point(bar.Right - radius * 0.6, bar.Bottom - 1.5));
                var textBottom = bar.Bottom - 4;
                if (options.Fingers && note.Finger > 0 && bar.Height >= 16) textBottom = Finger(dc, bar, note, col, pixelsPerDip) - 2;
                if (options.Names && textBottom - bar.Top >= MinNameHeight - 4 && bar.Width >= MinNameWidth)
                {
                    var name = KeyboardChromeDrawer.Text(KeyboardNoteSource.NameOf(key), Math.Min(12, Math.Max(10.5, bar.Width * 0.3)), col.HandText(note.LeftHand), pixelsPerDip);
                    if (name.Width <= bar.Width - 4 && textBottom - name.Height >= bar.Top + 2)
                        dc.DrawText(name, PixelSnap.Snap(new Point(bar.X + (bar.Width - name.Width) / 2, textBottom - name.Height), pixelsPerDip));
                }
                if (opacity < 1) dc.Pop();
            }
    }

    /// <summary>The finger number in a disc at the bar's start; returns the disc's top.</summary>
    private static double Finger(DrawingContext dc, Rect bar, KeyboardModeNote note, KeyboardColours col, double pixelsPerDip)
    {
        var r = Math.Clamp(bar.Width * 0.36, 6, 10);
        var c = new Point(bar.X + bar.Width / 2, bar.Bottom - 4 - r);
        dc.DrawEllipse(col.HandText(note.LeftHand), null, c, r, r);
        var text = KeyboardChromeDrawer.Text(note.Finger.ToString(System.Globalization.CultureInfo.InvariantCulture), r * 1.25, col.HandBrush(note.LeftHand), pixelsPerDip);
        dc.DrawText(text, PixelSnap.Snap(new Point(c.X - text.Width / 2, c.Y - text.Height / 2), pixelsPerDip));
        return c.Y - r;
    }

    /// <summary>The marks of the judged notes on the page: <paramref name="hit"/> answers true (hit), false (missed) or null (not judged) for a placed note.</summary>
    public static void Marks(DrawingContext dc, IReadOnlyList<KeyboardModePlaced> notes, double pageEndMs, double fromV, double toV, Func<KeyboardModePlaced, bool?> hit,
        KeyboardModeLayout lay, KeyboardColours col, double pixelsPerDip)
    {
        foreach (var placed in notes)
        {
            if (placed.VirtualMs < fromV || placed.VirtualMs > toV || !lay.Has(placed.Note.Fret) || hit(placed) is not { } ok) continue;
            var bar = BarOf(placed, pageEndMs, lay, pixelsPerDip);
            var radius = RadiusOf(bar);
            dc.DrawRoundedRectangle(null, ok ? col.HitEdge : col.MissEdge, bar, radius, radius);
            var r = Math.Clamp(bar.Width * 0.3, 4, 8);
            var c = new Point(bar.X + bar.Width / 2, bar.Top - r - 3);   // above the bar's end: it stays in view while the bar slides into the keys
            dc.DrawEllipse(ok ? col.Perfect : col.ExtraKey, null, c, r, r);
            if (ok)
            {
                dc.DrawLine(col.MarkPen, new Point(c.X - r * 0.55, c.Y), new Point(c.X - r * 0.15, c.Y + r * 0.45));
                dc.DrawLine(col.MarkPen, new Point(c.X - r * 0.15, c.Y + r * 0.45), new Point(c.X + r * 0.55, c.Y - r * 0.45));
            }
            else
            {
                dc.DrawLine(col.MarkPen, new Point(c.X - r * 0.45, c.Y - r * 0.45), new Point(c.X + r * 0.45, c.Y + r * 0.45));
                dc.DrawLine(col.MarkPen, new Point(c.X - r * 0.45, c.Y + r * 0.45), new Point(c.X + r * 0.45, c.Y - r * 0.45));
            }
        }
    }
}
