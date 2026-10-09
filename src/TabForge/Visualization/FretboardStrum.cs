using System.Windows;
using System.Windows.Media;

namespace TabForge.Visualization;

/// <summary>
/// Owns: the strummed-chord mark on the fretboard (one stroke arrow beside the nut over the chord's strings, plus one merged tag pill).
/// Does not own: the note markers (FretboardRenderer draws them, without per-note tags on a strummed chord) or the stroke data (TechniqueTag.StrumOf).
/// Tests: TestFretStrumArrow.
/// </summary>
internal static class FretboardStrum
{
    private static readonly VisualRole[] Roles = { VisualRole.Current, VisualRole.Selected, VisualRole.Next };

    /// <summary>The strummed chord to mark: the sounding (else selected, else next) strummed notes, their string span
    /// (high string = lowest index), direction and de-duplicated tags; null when none is strummed.</summary>
    internal static (VisualRole Role, int HighString, int LowString, bool Down, string Tags)? Group(IReadOnlyList<VisualNote> notes)
    {
        foreach (var role in Roles)
        {
            int high = int.MaxValue, low = -1; sbyte strum = 0;
            List<string>? tags = null;
            foreach (var n in notes)
            {
                if (n.Role != role || n.Strum == 0 || n.Released) continue;
                strum = n.Strum;
                high = Math.Min(high, n.StringIndex);
                low = Math.Max(low, n.StringIndex);
                if (n.Technique is { Length: > 0 } t && !(tags ??= new()).Contains(t)) tags.Add(t);
            }
            if (strum != 0) return (role, high, low, strum > 0, tags is null ? "" : string.Join(" ", tags));
        }
        return null;
    }

    /// <summary>Draws the stroke arrow (down = toward the high strings) at <paramref name="x"/> and the merged tag pill above it.</summary>
    internal static void Draw(DrawingContext dc, IReadOnlyList<VisualNote> notes, Func<int, double> stringY, double x, Rect bounds, VisualTheme theme, double scale)
    {
        if (Group(notes) is not { } strum) return;
        var color = strum.Role == VisualRole.Next ? theme.Next : theme.Current;
        double low = stringY(strum.LowString), high = stringY(strum.HighString);
        var (from, to) = strum.Down ? (low, high) : (high, low);
        var dir = from == to ? (strum.Down ? -1 : 1) : Math.Sign(to - from);
        from -= dir * 6; to += dir * 6;   // reach just past the outer strings (and give a one-string chord a visible length)
        dc.DrawLine(TabForge.Visualization.Draw.Pen(color, 3), new Point(x, from), new Point(x, to - dir * 4));
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(x, to + dir * 2), true, true);
            g.LineTo(new Point(x - 5, to - dir * 8), false, false);
            g.LineTo(new Point(x + 5, to - dir * 8), false, false);
        }
        head.Freeze();
        dc.DrawGeometry(TabForge.Visualization.Draw.Solid(color), null, head);
        if (strum.Tags.Length == 0) return;
        var w = Math.Max(22, strum.Tags.Length * 6.2 + 10) * scale;
        var pill = new Rect(Math.Max(bounds.X, x - 6), Math.Max(bounds.Y, Math.Min(from, to) - 17 * scale), w, 13 * scale);
        dc.DrawRoundedRectangle(TabForge.Visualization.Draw.Solid(theme.Background, 0.92), TabForge.Visualization.Draw.Pen(color, 1.4), pill, 6.5 * scale, 6.5 * scale);
        TabForge.Visualization.Draw.Centered(dc, strum.Tags, pill.X + pill.Width / 2, pill.Y + 1.5 * scale, 8.5 * scale, TabForge.Visualization.Draw.Solid(theme.Text), bold: true);
    }
}
