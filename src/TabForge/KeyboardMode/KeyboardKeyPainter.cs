using System.Windows;
using System.Windows.Media;

namespace TabForge.KeyboardMode;

/// <summary>The keys the key layer lights: due notes per hand and the chord the song waits for.</summary>
internal readonly record struct KeyboardLitKeys(KeyMask DueLeft, KeyMask DueRight, KeyMask Awaited);

// Owns: drawing the key layer of the keyboard view: every lit key filled within its own outline (KeyboardModeLayout.KeyShape: a white key minus the black keys over it, a black key's rectangle),
//   with an inner glow and edge clipped to that outline, so no light reaches a neighbouring key or the falling area. A held right key is green with a check, an extra red with a cross (shape as
//   well as colour), a key held while nothing is judged takes its hand's colour; a due key its hand's colour; an awaited key gets an inner outline. White keys first, black keys over them.
// Does not own: which keys are lit (KeyboardModeView, KeyboardFeedback), the outlines (KeyboardModeLayout) or the colours (KeyboardColours).
// Tests: TestKeyboardModeKeyView.
internal static class KeyboardKeyPainter
{
    public static void Paint(DrawingContext dc, KeyboardModeLayout lay, KeyboardModePalette pal, KeyboardFeedback? feedback, KeyboardLitKeys lit)
    {
        var col = pal.Keyboard;
        var glow = new Pen(col.Glow.Brush, Math.Clamp(lay.WhiteWidth * 0.22, 2, 7) * 2);   // twice the visible width: the outer half is clipped away
        glow.Freeze();
        var wait = new Pen(col.WaitPen.Brush, col.WaitPen.Thickness * 2);
        wait.Freeze();
        for (var pass = 0; pass < 2; pass++)
            for (var m = lay.Lowest; m <= lay.Highest; m++)
            {
                if (KeyboardModeLayout.IsWhite(m) != (pass == 0)) continue;
                var held = feedback?.HeldOf(m) ?? KeyHold.None;
                var dueLeft = lit.DueLeft.Has(m);
                var due = dueLeft || lit.DueRight.Has(m);
                var awaited = lit.Awaited.Has(m);
                if (held == KeyHold.None && !due && !awaited) continue;
                var shape = lay.KeyShape(m);
                var fill = held switch
                {
                    KeyHold.Correct => col.LitOk,
                    KeyHold.Extra => col.LitExtra,
                    KeyHold.Held => col.LitHand(due ? dueLeft : m < KeyboardHands.MiddleC),
                    _ => due ? col.LitHand(dueLeft) : null,
                };
                dc.PushClip(shape);
                if (fill is not null)
                {
                    dc.DrawGeometry(fill, null, shape);
                    dc.DrawGeometry(null, glow, shape);
                }
                if (awaited) dc.DrawGeometry(null, wait, shape);
                dc.DrawGeometry(null, col.Seam, shape);
                dc.Pop();
                Mark(dc, lay, col, m, held);
            }
    }

    /// <summary>A check on a right key, a cross on an extra one, near the key's front (inside its outline).</summary>
    private static void Mark(DrawingContext dc, KeyboardModeLayout lay, KeyboardColours col, int m, KeyHold held)
    {
        if (held is not (KeyHold.Correct or KeyHold.Extra)) return;
        var rect = lay.KeyRect(m);
        var r = Math.Clamp(rect.Width * 0.24, 3, 8);
        var c = new Point(rect.X + rect.Width / 2, rect.Bottom - r - Math.Min(rect.Height * 0.12, KeyboardModeLayout.IsWhite(m) ? 18 : 6));
        if (held == KeyHold.Correct)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(new Point(c.X - r, c.Y), false, false);
                s.LineTo(new Point(c.X - r * 0.3, c.Y + r * 0.7), true, true);
                s.LineTo(new Point(c.X + r, c.Y - r * 0.7), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, col.MarkPen, g);
        }
        else
        {
            dc.DrawLine(col.MarkPen, new Point(c.X - r * 0.8, c.Y - r * 0.8), new Point(c.X + r * 0.8, c.Y + r * 0.8));
            dc.DrawLine(col.MarkPen, new Point(c.X - r * 0.8, c.Y + r * 0.8), new Point(c.X + r * 0.8, c.Y - r * 0.8));
        }
    }
}
