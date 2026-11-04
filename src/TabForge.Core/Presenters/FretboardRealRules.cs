using System;

namespace TabForge.Presenters;

// Owns: the real-instrument proportions the fretboard art is drawn to, in plain doubles: the wood left beyond the outer strings, string
//   thickness by gauge, fret wire and nut widths, inlay sizes and where inlays sit across the board.
// Does not own: the layout (FretboardGeometryRules), the pixels (Avalonia FretboardArtist) or the look names (FretboardStyles).
// Research basis: a 6-string electric is 43 mm at the nut with the outer strings about 3.2 mm in from each edge and 7.3 mm apart (so the
//   wood beyond the outer string is 0.44 of the string spacing); a 4-string bass is 38 mm with about 3.5 mm in and 10.4 mm apart (0.34);
//   fret wire crowns are about 2 mm wide; a bone nut is about 7 mm thick as seen from above, with a groove for each string; front inlay dots are 4 to 6 mm (6 mm, about 0.8 of the guitar's
//   string spacing, is the common stock size); double dots at the 12th and 24th sit about a string spacing either side of the centre line;
//   guitar strings run from about 0.26 mm (.010) to 1.17 mm (.046), bass strings from about 1.1 mm (.045) to 2.7 mm (.105).
// Tests: TabForge.Avalonia.Tests.FretboardStyleTests.
public static class FretboardRealRules
{
    public const double GuitarSpacingMm = 7.3;
    public const double BassSpacingMm = 10.4;

    /// <summary>The wood beyond the outer strings, as a share of the string gap.</summary>
    public static double EdgeMarginRatio(bool bass) => bass ? 0.34 : 0.44;

    /// <summary>The string spacing in millimetres the drawing scale is taken from.</summary>
    public static double SpacingMm(bool bass) => bass ? BassSpacingMm : GuitarSpacingMm;

    public const double WireWidthMm = 1.9;
    public const double NutThicknessMm = 7.0;

    /// <summary>The string's diameter in millimetres: string 0 is the highest, the last the lowest; thickness grows slightly faster than evenly.</summary>
    public static double GaugeMm(int stringIndex, int strings, bool bass)
    {
        var (thin, thick) = bass ? (1.1, 2.7) : (0.26, 1.17);
        var t = strings <= 1 ? 0 : Math.Clamp(stringIndex / (double)(strings - 1), 0, 1);
        return thin + (thick - thin) * Math.Pow(t, 1.3);
    }

    /// <summary>The drawn string width in DIPs: the gauge at the board's own scale (gap over real spacing), kept between a hairline and a bold line.</summary>
    public static double StringWidthPx(int stringIndex, int strings, double gap, bool bass) =>
        Math.Clamp(GaugeMm(stringIndex, strings, bass) * gap / SpacingMm(bass) * StringDrawScale, 0.8, bass ? 4.6 : 3.4);

    /// <summary>Strings are drawn a little finer than their true gauge at the board's scale: at a screen size a full-gauge string reads as a bar, a thinner one as wire.</summary>
    public const double StringDrawScale = 0.72;

    /// <summary>True for the strings that are wound on a real set: the thick ones (a guitar's lowest three, a bass's all but the highest).</summary>
    public static bool IsWound(int stringIndex, int strings, bool bass) => bass ? stringIndex >= 1 : stringIndex >= Math.Max(0, strings - 3);

    /// <summary>The fret wire's drawn width in DIPs from the width of one fret cell (the cell's real length is the guitar's or bass's even cell).</summary>
    public static double WireWidthPx(double cellPx, double cellMm) => Math.Clamp(WireWidthMm * cellPx / cellMm, 1.8, 3.8);

    public static double NutWidthPx(double cellPx, double cellMm) => Math.Clamp(NutThicknessMm * cellPx / cellMm, 7.0, 13.0);

    /// <summary>A dot inlay's diameter in DIPs: 0.7 of the string gap on a guitar (about 5 mm), 0.52 on a bass, and the small dots half of that.</summary>
    public static double DotDiameter(double gap, bool bass, bool small) => gap * (bass ? 0.52 : 0.7) * (small ? 0.6 : 1.0);

    /// <summary>True for the fret positions that carry a marker: 3, 5, 7, 9, 12, 15, 17, 19, 21, 24 (double at 12 and 24).</summary>
    public static bool HasInlay(int fret) => fret is 3 or 5 or 7 or 9 or 12 or 15 or 17 or 19 or 21 or 24;

    public static bool IsDouble(int fret) => fret is 12 or 24;

    /// <summary>How far above and below the board's centre line a double marker sits: one string gap (three quarters on a 3-string board).</summary>
    public static double DoubleOffset(double gap, int strings) => strings >= 4 ? gap : gap * 0.75;

    /// <summary>The fin or block height as a share of the wood's height, growing toward the body for fins: 0.5 at the 3rd fret to 0.78 at the 24th.</summary>
    public static double FinHeightShare(int fret) => 0.5 + 0.28 * Math.Clamp((fret - 3) / 21.0, 0, 1);
}
