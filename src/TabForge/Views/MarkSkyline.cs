using System.Windows;

namespace TabForge.Views;

/// <summary>
/// Layout-time row stacking for the markings around one staff: every mark claims its box and the next one is
/// placed outside it (above the notation, or below it), so no two texts or marks overlap. Seeded with the
/// notation's own ink (heads, accidentals, stems, flags, beams, grace notes). Nothing here runs per frame
/// beyond the O(marks) placement done while a system is engraved into its drawing cache.
/// </summary>
internal sealed class MarkSkyline
{
    private const double Gap = 2.6;
    private readonly List<Rect> _boxes = new();

    public int Count => _boxes.Count;

    public void Clear() => _boxes.Clear();

    public void Claim(double x0, double x1, double y0, double y1)
    {
        if (x1 > x0 && y1 > y0) _boxes.Add(new Rect(x0, y0, x1 - x0, y1 - y0));
    }

    public void Claim(Rect box) { if (box.Width > 0 && box.Height > 0) _boxes.Add(box); }

    private bool Hit(double x0, double x1, double y0, double y1, out Rect hit)
    {
        foreach (var b in _boxes)
            if (b.Right > x0 - Gap && b.Left < x1 + Gap && b.Bottom > y0 - Gap && b.Top < y1 + Gap) { hit = b; return true; }
        hit = default;
        return false;
    }

    /// <summary>Top of a box [x0,x1] x [y, y+height] whose bottom edge is at most <paramref name="bottom"/>, moved up until clear; the box is claimed.</summary>
    public double PlaceAbove(double x0, double x1, double height, double bottom, bool claim = true)
    {
        var y = bottom - height;
        for (var guard = 0; guard < 80 && Hit(x0, x1, y, y + height, out var hit); guard++)
            y = Math.Min(y, hit.Top - Gap - height) - 0.01;
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

    /// <summary>The lowest claimed bottom edge inside the column, or <paramref name="fallback"/> when nothing is there.</summary>
    public double LowestIn(double x0, double x1, double fallback)
    {
        var lowest = fallback;
        foreach (var b in _boxes)
            if (b.Right > x0 && b.Left < x1) lowest = Math.Max(lowest, b.Bottom);
        return lowest;
    }

    /// <summary>The highest claimed top edge inside the column, or <paramref name="fallback"/> when nothing is there.</summary>
    public double HighestIn(double x0, double x1, double fallback)
    {
        var highest = fallback;
        foreach (var b in _boxes)
            if (b.Right > x0 && b.Left < x1) highest = Math.Min(highest, b.Top);
        return highest;
    }
}
