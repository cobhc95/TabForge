using System;
using TabForge.Platform;
using TabForge.Visualization;

namespace TabForge.Presenters;

// Owns: the realistic size limits of the drawn fretboard as it resizes with its pane: the guitar scale length and the distance of a fret
//   from the nut, the nut width by string count, the board's drawn aspect (board width : board height) and the string-gap range, the
//   pane height limits they imply (the gap cap, and the height a pane's width alone allows), and the fret-number font size that follows the string gap.
// Does not own: the layout itself (FretboardGeometryRules.Compute applies these limits), the pane's height (the dock), the drawing.
// Research basis: a 25.5 in (648 mm) scale; fret n sits at 648 * (1 - 2^(-n/12)) mm from the nut (12th 324 mm, 24th 486 mm); the
//   board is about 42-43 mm wide at the nut for a 6-string electric, 52 mm for an acoustic, 48 mm for a 7-string, 38 mm for a 4-string
//   bass and 45 mm for a 5-string; its length to width runs about 7:1 at 12 frets and 10:1 at 24 frets. The drawn aspect allows 5:1 to
//   14:1 so the height can be exaggerated a little for readability.
// Tests: TabForge.Avalonia.Tests.FretboardSizeTests.
public static class FretboardSizeRules
{
    /// <summary>The guitar scale length, in millimetres (25.5 inches).</summary>
    public const double ScaleLengthMm = 648;

    /// <summary>The bass scale length, in millimetres (34 inches).</summary>
    public const double BassScaleLengthMm = 864;

    /// <summary>The share of the scale length that the frets span: the even fret cell is that span over <see cref="CellFrets"/>.</summary>
    public const double FretSpanOfScale = 0.75;

    /// <summary>The fret count the even fret cell is taken over (24 frets span 486 mm of a guitar, a cell of 20.25 mm).</summary>
    public const int CellFrets = 24;

    /// <summary>The average string pitch in millimetres: about 9 on a guitar (nut 43 over 6 strings, 48 over 7), about 15 on a bass.</summary>
    public const double GuitarStringPitchMm = 9;
    public const double BassStringPitchMm = 15;

    /// <summary>The real length of one even fret cell in millimetres: 20.25 on a guitar, 27 on a bass.</summary>
    public static double FretCellMm(bool bass) => (bass ? BassScaleLengthMm : ScaleLengthMm) * FretSpanOfScale / CellFrets;

    /// <summary>The average string pitch in millimetres of a guitar or a bass.</summary>
    public static double StringPitchMm(bool bass) => bass ? BassStringPitchMm : GuitarStringPitchMm;

    /// <summary>The real aspect (width : height) of a board showing <paramref name="shownFrets"/> frets of <paramref name="strings"/> strings:
    /// shown frets × cell over strings × pitch (a 24-fret 7-string guitar is about 7.7 : 1).</summary>
    public static double RealAspect(int shownFrets, int strings, bool bass) => shownFrets * FretCellMm(bass) / (Math.Max(1, strings) * StringPitchMm(bass));

    /// <summary>The smallest drawn string gap, in DIPs: the fret numbers and dots stay readable.</summary>
    public const double StringGapMinPx = 12;

    /// <summary>The largest drawn string gap, in DIPs: past it the pane's extra height is empty background.</summary>
    public const double StringGapMaxPx = 34;

    /// <summary>The narrowest drawn board: its width is at least this many times its height.</summary>
    public const double AspectMin = 5;

    /// <summary>The widest drawn board: its width is at most this many times its height.</summary>
    public const double AspectMax = 14;

    /// <summary>The string gap that the fret-number font size is sized against, and the font size at that gap.</summary>
    public const double ReferenceStringGapPx = 26;
    public const double ReferenceFretNumberFontSize = 13.5;
    public const double FretNumberFontMin = 8;
    public const double FretNumberFontMax = 14;

    /// <summary>The distance of fret <paramref name="fret"/> from the nut, in millimetres (the 12th fret is 324 mm, the 24th 486 mm).</summary>
    public static double DistanceFromNutMm(int fret) => ScaleLengthMm * (1 - Math.Pow(2, -fret / 12.0));

    /// <summary>The fretboard's width at the nut in millimetres, by string count: 4 bass 38, 5 bass 45, 6 guitar 43, 7 guitar 48, 8 guitar 54.</summary>
    public static double NutWidthMm(int strings) => strings switch
    {
        4 => 38,
        5 => 45,
        7 => 48,
        8 => 54,
        _ => 43,
    };

    /// <summary>The string gap that keeps a board of <paramref name="boardWidth"/> within the aspect range, then within the gap range.
    /// Where the aspect cannot be met (a board too narrow or too wide for the gap range) the gap range wins. An infinite aspect cap
    /// (the WPF panel) means the board has no aspect range, so the gap is returned unchanged.</summary>
    public static double ClampStringGap(double gap, int strings, double boardWidth, FretboardBoardLimits? limits = null)
    {
        var caps = limits ?? FretboardBoardLimits.Default;
        if (double.IsPositiveInfinity(caps.AspectMax)) return gap;
        var gaps = Math.Max(1, strings - 1);
        var low = Math.Max(StringGapMinPx, boardWidth / (caps.AspectMax * gaps));
        var high = Math.Min(caps.MaxStringGap, boardWidth / (AspectMin * gaps));
        return low > high ? Math.Clamp(gap, StringGapMinPx, caps.MaxStringGap) : Math.Clamp(gap, low, high);
    }

    /// <summary>The pane height at which the string gap reaches its largest value: the board's gaps at the largest gap, plus the padding around the board.</summary>
    public static double MaxPaneHeight(int strings, double padding, FretboardBoardLimits? limits = null) =>
        Math.Max(1, strings - 1) * (limits ?? FretboardBoardLimits.Default).MaxStringGap + padding;

    /// <summary>The pane height (real DIPs) at which the string gap reaches its cap in a pane of <paramref name="paneWidth"/>: <see cref="MaxPaneHeight"/> is in
    /// drawing units, so it is multiplied by the scale a pane of that width draws at (a width of 0 means no width limit, the largest scale).</summary>
    public static double GapCapPaneHeight(int strings, double padding, double paneWidth, FretboardBoardLimits? limits = null)
    {
        var scale = FretboardScaleRules.Scale(paneWidth, double.PositiveInfinity, FretboardScaleRules.NaturalHeight(strings, 1.0));
        return scale * MaxPaneHeight(strings, padding, limits);
    }

    /// <summary>A pane height no fretboard reaches: the string gap laid out at it is the gap the pane's width alone allows.</summary>
    private const double WidthProbeHeight = 1e6;

    /// <summary>The pane height at which a fretboard of <paramref name="paneWidth"/> stops growing for its width alone. The board is laid out
    /// at the scale that width draws at, with the height unbounded, so its string gap is the largest the width allows (the aspect cap, the
    /// fret-width proportion and the gap cap). The board stops at the taller of that gap's board plus the padding and the natural height,
    /// both drawn at the same scale: below the natural height the scale follows the height, so the board keeps growing until the width takes over.</summary>
    /// <param name="state">The fretboard's string count, frets and spacing.</param>
    /// <param name="paneWidth">The pane's width in DIPs.</param>
    /// <param name="widthReserve">The width the pane keeps clear beside the fretboard (the close button), in DIPs.</param>
    public static double WidthLimitedPaneHeight(FretboardGeometryRules.Input state, double paneWidth, double widthReserve, FretboardBoardLimits? limits = null)
    {
        var natural = FretboardScaleRules.NaturalHeight(state.Strings, 1.0);
        var scale = FretboardScaleRules.Scale(paneWidth, double.PositiveInfinity, natural);
        var content = new UiRect(0, 0, (paneWidth - widthReserve) / scale, WidthProbeHeight);
        var area = FretboardGeometryRules.ContentArea(state, content, FretboardHorizontalPosition.Centre, limits);
        var gap = FretboardGeometryRules.Compute(state, area, FretboardHorizontalPosition.Centre, limits: limits).StringGap;
        var padding = FretboardGeometryRules.TopPad + FretboardGeometryRules.BottomPad;
        return scale * Math.Max(natural, Math.Max(1, state.Strings - 1) * gap + padding);
    }

    /// <summary>The pane height at which the string gap reaches its smallest value: the board's gaps at the smallest gap, plus the padding.</summary>
    public static double MinPaneHeight(int strings, double padding) => Math.Max(1, strings - 1) * StringGapMinPx + padding;

    /// <summary>The fret-number font size for a string gap: it grows with the gap, within its own minimum and maximum.</summary>
    public static double FretNumberFontSize(double stringGap) =>
        Math.Clamp(stringGap * ReferenceFretNumberFontSize / ReferenceStringGapPx, FretNumberFontMin, FretNumberFontMax);
}
