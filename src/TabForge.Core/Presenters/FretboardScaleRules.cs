using System;

namespace TabForge.Presenters;

// Owns: the drawing scale of the fretboard pane: its natural height for a string count and spacing, and the scale a pane of a given
//   size draws at (height over natural height, capped by the natural width, clamped to [MinScale, MaxScale]).
// Does not own: the layout inside the scaled space (FretboardGeometryRules), the pane's required height in the panel (InstrumentPanel),
//   or the drawing.
// Tests: no dedicated test class.
public static class FretboardScaleRules
{
    /// <summary>Width (DIPs) the fretboard is laid out for at scale 1; a narrower pane scales down instead of squeezing the frets.</summary>
    public const double NaturalWidth = 900;
    /// <summary>Smallest drawing scale (markers still readable).</summary>
    public const double MinScale = 0.7;
    /// <summary>Largest drawing scale; a taller pane beyond this spreads the strings instead.</summary>
    public const double MaxScale = 2.0;

    /// <summary>Sounding marker below the lowest string: radius 16 + halo 4 + 2; the fret numbers end at bottom + 5 + ~14.</summary>
    public const double BelowLowestString = 16 + 4 + 2;
    /// <summary>Colour legend (4 rows of 18 from y 10) and the Scales button under it (24 high, 6 apart).</summary>
    public const double LegendAndScalesButton = 10 + 4 * 18 + 6 + 24 + 6;
    /// <summary>Extra room under the drawn extent so rounding and DPI never cut the last row.</summary>
    public const double SafetyMargin = 6;

    /// <summary>The height (DIPs) the fretboard is drawn at scale 1: the drawn extent of the strings plus the bottom pad, at least the legend column.</summary>
    /// <param name="strings">The string count (at least 1).</param>
    /// <param name="stringSpacing">The user's string spacing factor; only values above 1 add height.</param>
    public static double NaturalHeight(int strings, double stringSpacing)
    {
        var wide = double.IsFinite(stringSpacing) ? Math.Clamp(stringSpacing, 1.0, FretboardGeometryRules.MaxSpacingFactor) : 1.0;
        var board = FretboardGeometryRules.TopPad + (Math.Max(1, strings) - 1) * FretboardGeometryRules.MinStringGap * wide
                    + Math.Max(FretboardGeometryRules.BottomPad, BelowLowestString);
        return Math.Ceiling(Math.Max(board, LegendAndScalesButton) + SafetyMargin);
    }

    /// <summary>The scale a pane of <paramref name="width"/> x <paramref name="height"/> draws the fretboard at, never below <paramref name="minScale"/> (a pane that may be smaller than <see cref="MinScale"/> allows passes its own).</summary>
    public static double Scale(double width, double height, double naturalHeight, double minScale = MinScale)
    {
        var scale = naturalHeight > 0 ? height / naturalHeight : 1;
        if (width > 0) scale = Math.Min(scale, width / NaturalWidth);
        return Math.Clamp(scale, minScale, MaxScale);
    }
}
