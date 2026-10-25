using System;
using System.Collections.Generic;
using System.Globalization;
using TabForge.KeyboardMode;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Draws the falling notes of the keyboard view: a rounded bar per note exactly over its key (a black-key bar narrower), from its end (top) to its start (bottom), in its hand's colour with a bright line
/// at the start, the other hand faded or left out by the hands filter, and when the bar has room its finger number (a disc) and its note name; plus the marks of judged notes (a green edge and a check
/// on a bar that was hit, a red edge and a cross on one that was missed).
/// Owns: the bar rectangle of a note and the drawing of bars and marks. Does not own: which notes are on the page (KeyboardModeWindow), the keys and overlays (KeyboardChromePainter), the state or the colours (KeyboardInk).
/// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests.
/// </summary>
public static class KeyboardNotesPainter
{
    public const double MinBarHeight = 10;
    public const string TextFamily = "Segoe UI";
    public const int TextWeight = 600;
    private const double WhiteInset = 1.5, MaxRadius = 7, MinNameHeight = 20, MinNameWidth = 16, MinBarWidth = 3;

    /// <summary>A note's bar when the song time at the strip's top edge is <paramref name="nowV"/>: its bottom edge sits on the strip when the note starts.</summary>
    public static UiRect BarOf(KeyboardModePlaced placed, double nowV, KeyboardStripGeometry g)
    {
        var key = placed.Note.Fret;
        var inset = KeyboardGeometryRules.IsWhite(key) ? WhiteInset : 0;
        var bottom = g.StripTop - (placed.VirtualMs - nowV) * g.PxPerMs;
        var h = Math.Max(MinBarHeight, placed.Note.DurationMs * g.PxPerMs);
        return new UiRect(g.KeyX(key) + inset, bottom - h, Math.Max(MinBarWidth, g.KeyWidth(key) - 2 * inset), h);
    }

    private static double RadiusOf(UiRect bar) => Math.Min(MaxRadius, Math.Min(bar.Width, bar.Height) / 2.5);

    /// <summary>Draws <paramref name="notes"/> (in virtual onset order): white-key notes first so the narrower black-key notes sit on top where a chord is close.</summary>
    public static void Draw(IDrawContext d, IReadOnlyList<KeyboardModePlaced> notes, double nowV, KeyboardStripGeometry g, KeyboardInk ink, KeyboardHandsFilter hands, bool names, bool fingers)
    {
        for (var pass = 0; pass < 2; pass++)
            foreach (var placed in notes)
            {
                var note = placed.Note;
                var key = note.Fret;
                if (!g.Has(key) || KeyboardGeometryRules.IsWhite(key) != (pass == 0)) continue;
                var opacity = KeyboardHands.OpacityOf(hands, note.LeftHand);
                if (opacity <= 0) continue;
                var bar = BarOf(placed, nowV, g);
                if (bar.Y > g.StripTop || bar.Bottom < 0) continue;
                if (opacity < 1) d.PushOpacity(opacity);
                var radius = RadiusOf(bar);
                d.Rect(ink.HandFill(note.LeftHand), new UiStroke(ink.HandEdge(note.LeftHand), 1.2), bar.X, bar.Y, bar.Width, bar.Height, radius);
                d.Line(new UiStroke(ink.Cap, 2), bar.X + radius * 0.6, bar.Bottom - 1.5, bar.Right - radius * 0.6, bar.Bottom - 1.5);
                var textBottom = bar.Bottom - 4;
                if (fingers && note.Finger > 0 && bar.Height >= 16) textBottom = Finger(d, bar, note, ink) - 2;
                if (names && textBottom - bar.Y >= MinNameHeight - 4 && bar.Width >= MinNameWidth) Name(d, bar, textBottom, key, note.LeftHand, ink);
                if (opacity < 1) d.Pop();
            }
    }

    private static void Name(IDrawContext d, UiRect bar, double textBottom, int key, bool leftHand, KeyboardInk ink)
    {
        var name = d.Text(KeyboardNoteSource.NameOf(key), TextFamily, Math.Min(12, Math.Max(10.5, bar.Width * 0.3)), ink.HandText(leftHand), d.PixelsPerDip, weight: TextWeight);
        if (name.Width > bar.Width - 4 || textBottom - name.Height < bar.Y + 2) return;
        d.DrawText(name, bar.X + (bar.Width - name.Width) / 2, textBottom - name.Height);
    }

    /// <summary>The finger number in a disc at the bar's start; returns the disc's top.</summary>
    private static double Finger(IDrawContext d, UiRect bar, KeyboardModeNote note, KeyboardInk ink)
    {
        var r = Math.Clamp(bar.Width * 0.36, 6, 10);
        var cx = bar.X + bar.Width / 2;
        var cy = bar.Bottom - 4 - r;
        d.Ellipse(ink.HandText(note.LeftHand), null, cx, cy, r, r);
        var text = d.Text(note.Finger.ToString(CultureInfo.InvariantCulture), TextFamily, r * 1.25, ink.HandFill(note.LeftHand), d.PixelsPerDip, weight: TextWeight);
        d.DrawText(text, cx - text.Width / 2, cy - text.Height / 2);
        return cy - r;
    }

    /// <summary>The marks of the judged notes in [<paramref name="fromV"/>, <paramref name="toV"/>]: <paramref name="hit"/> answers true (hit), false (missed) or null (not judged) for a placed note.</summary>
    public static void Marks(IDrawContext d, IReadOnlyList<KeyboardModePlaced> notes, double nowV, double fromV, double toV, Func<KeyboardModePlaced, bool?> hit, KeyboardStripGeometry g, KeyboardInk ink)
    {
        var mark = new UiStroke(ink.Mark, 2);
        foreach (var placed in notes)
        {
            if (placed.VirtualMs < fromV || placed.VirtualMs > toV || !g.Has(placed.Note.Fret) || hit(placed) is not { } ok) continue;
            var bar = BarOf(placed, nowV, g);
            var radius = RadiusOf(bar);
            d.Rect(null, new UiStroke(ok ? ink.Ok : ink.Extra, 2.5), bar.X, bar.Y, bar.Width, bar.Height, radius);
            var r = Math.Clamp(bar.Width * 0.3, 4, 8);
            double cx = bar.X + bar.Width / 2, cy = bar.Y - r - 3;   // above the bar's end: it stays in view while the bar slides into the keys
            d.Ellipse(ok ? ink.Perfect : ink.Extra, null, cx, cy, r, r);
            if (ok)
            {
                d.Line(mark, cx - r * 0.55, cy, cx - r * 0.15, cy + r * 0.45);
                d.Line(mark, cx - r * 0.15, cy + r * 0.45, cx + r * 0.55, cy - r * 0.45);
            }
            else
            {
                d.Line(mark, cx - r * 0.45, cy - r * 0.45, cx + r * 0.45, cy + r * 0.45);
                d.Line(mark, cx - r * 0.45, cy + r * 0.45, cx + r * 0.45, cy - r * 0.45);
            }
        }
    }
}
