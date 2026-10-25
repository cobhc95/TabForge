
using TabForge.Platform;

namespace TabForge.Views;

// Owns: the claimed boxes around one staff: placing a mark above or below what is already claimed, and the lowest or highest claimed edge over a span.
// Does not own: the mark text (ScoreMarkText) or the drawing of marks (ScoreRenderer.Marks.cs).
// Tests: no dedicated test class.

/// <summary>
/// Layout-time row stacking for the markings around one staff: every mark claims its box and the next one is
/// placed outside it (above the notation, or below it), so no two texts or marks overlap. Seeded with the
/// notation's own ink (heads, accidentals, stems, flags, beams, grace notes). Nothing here runs per frame
/// beyond the O(marks) placement done while a system is engraved into its drawing cache.
/// </summary>
internal sealed class MarkSkyline
{
    private const double Gap = 2.6;
    private readonly List<UiRect> _boxes = new();

    public int Count => _boxes.Count;

    public void Clear() => _boxes.Clear();

    public void Claim(double x0, double x1, double y0, double y1)
    {
        if (x1 > x0 && y1 > y0) _boxes.Add(new UiRect(x0, y0, x1 - x0, y1 - y0));
    }

    public void Claim(UiRect box) { if (box.Width > 0 && box.Height > 0) _boxes.Add(box); }

    private bool Hit(double x0, double x1, double y0, double y1, out UiRect hit)
    {
        foreach (var b in _boxes)
            if (b.Right > x0 - Gap && b.X < x1 + Gap && b.Bottom > y0 - Gap && b.Y < y1 + Gap) { hit = b; return true; }
        hit = default;
        return false;
    }

    /// <summary>Top of a box [x0,x1] x [y, y+height] whose bottom edge is at most <paramref name="bottom"/>, moved up until clear; the box is claimed.</summary>
    public double PlaceAbove(double x0, double x1, double height, double bottom, bool claim = true)
    {
        var y = bottom - height;
        for (var guard = 0; guard < 80 && Hit(x0, x1, y, y + height, out var hit); guard++)
            y = Math.Min(y, hit.Y - Gap - height) - 0.01;
        if (claim) Claim(x0, x1, y, y + height);
        return y;
    }

    /// <summary>Top of a box [x0,x1] x [y, y+height] whose top edge is at least <paramref name="top"/>, moved down until clear; the box is claimed.</summary>
    public double PlaceBelow(double x0, double x1, double height, double top, bool claim = true)
    {
        var y = top;
        for (var guard = 0; guard < 80 && Hit(x0, x1, y, y + height, out var hit); guard++)
            y = Math.Max(y, hit.Bottom + Gap) + 0.01;
        if (claim) Claim(x0, x1, y, y + height);
        return y;
    }
}
