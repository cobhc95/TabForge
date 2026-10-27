using System;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Visualization;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard resize limits: the research distances and nut widths, and for 4, 6 and 7 strings at 12 and 24 frets over a range of
/// pane heights and widths, the string gap stays in its range, the drawn aspect stays in its range, the board stays inside the pane beside
/// the legend, and the pane's maximum height is the height where the gap reaches its cap.
/// Does not own: the dock's splitter (it is checked by running the app).
/// Tests: FretboardSizeRules, FretboardGeometryRules.Compute.
/// </summary>
public class FretboardSizeTests
{
    private static readonly int[] StringCounts = { 4, 6, 7 };
    private static readonly int[] FretCounts = { 12, 24 };
    private static readonly double[] Widths = { 900, 1300, 1900 };
    private const double Tolerance = 0.5;

    private static double Padding => FretboardGeometryRules.TopPad + FretboardGeometryRules.BottomPad;

    /// <summary>The board in a pane of <paramref name="width"/> (less the close X reserve), laid out as the panel lays it out.</summary>
    private static FretboardGeometryRules.Layout LayoutAt(int strings, int frets, double width, double height)
    {
        var input = new FretboardGeometryRules.Input(strings, frets, frets, 1, false, 0);
        var pane = new UiRect(0, 0, width - InstrumentPanelView.CloseButtonReserve, height);
        return FretboardGeometryRules.Compute(input, FretboardGeometryRules.ContentArea(input, pane));
    }

    /// <summary>The fretboard row is resizable by its splitter on a default start (the lock is off), and locked only by the Lock fretboard size setting.</summary>
    [Xunit.Fact]
    public void FretboardRowIsLockedOnlyBySizeLockSetting()
    {
        Assert.False(InstrumentPanelStrip.LocksRowHeight(null));
        Assert.False(InstrumentPanelStrip.LocksRowHeight(new AppearanceSettings()));
        Assert.True(InstrumentPanelStrip.LocksRowHeight(new AppearanceSettings { LockInstrumentSize = true }));
    }

    /// <summary>In the Avalonia panel a wide, short pane is filled: the board runs to the legend's margin and to the pane's height less the padding.</summary>
    [Xunit.Fact]
    public void AvaloniaBoardFillsAWidePaneBesideTheLegend()
    {
        const double paneWidth = 1500, paneHeight = 210;
        const double legendMargin = 28;   // the gap the Core layout keeps between the board and the legend column
        var scale = FretboardScaleRules.Scale(paneWidth, paneHeight, FretboardScaleRules.NaturalHeight(6, 1.0));
        var virtualWidth = (paneWidth - InstrumentPanelView.CloseButtonReserve) / scale;
        var virtualHeight = paneHeight / scale;
        var input = new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0);
        var limits = InstrumentPanelView.BoardLimits;
        var area = FretboardGeometryRules.ContentArea(input, new UiRect(0, 0, virtualWidth, virtualHeight), FretboardHorizontalPosition.Centre, limits);
        var layout = FretboardGeometryRules.Compute(input, area, FretboardHorizontalPosition.Centre, limits: limits);
        Assert.Equal(virtualWidth - FretboardGeometryRules.LegendWidth - legendMargin, layout.Board.Right, Tolerance);
        // The board keeps the real proportion of its frets and strings, so a short wide pane fills the width and the height follows the proportion.
        var real = FretboardSizeRules.RealAspect(24, 6, bass: false);
        var ratio = layout.Board.Width / layout.Board.Height;
        Assert.True(Math.Abs(ratio / real - 1) <= 0.02, $"board {ratio:F2} : 1 is not the real {real:F2} : 1");
    }

    [Xunit.Fact]
    public void ResearchDistancesAndNutWidthsMatchTheGuitarScale()
    {
        Assert.Equal(324, FretboardSizeRules.DistanceFromNutMm(12), Tolerance);
        Assert.Equal(486, FretboardSizeRules.DistanceFromNutMm(24), Tolerance);
        Assert.Equal(38, FretboardSizeRules.NutWidthMm(4));
        Assert.Equal(45, FretboardSizeRules.NutWidthMm(5));
        Assert.Equal(43, FretboardSizeRules.NutWidthMm(6));
        Assert.Equal(48, FretboardSizeRules.NutWidthMm(7));
    }

    [Xunit.Fact]
    public void StringGapClampsToItsRangeAndTheAspectRange()
    {
        Assert.Equal(FretboardSizeRules.StringGapMaxPx, FretboardSizeRules.ClampStringGap(100, 6, 1150));
        // A 1150 DIP board at 6 strings needs gaps of at least 1150 / (14 * 5) for the 14:1 aspect, above the 12 DIP floor.
        Assert.Equal(1150.0 / (FretboardSizeRules.AspectMax * 5), FretboardSizeRules.ClampStringGap(1, 6, 1150), Tolerance);
        // A 300 DIP board at 6 strings may be at most 300 / (5 * 5) = 12 DIPs per gap for a 5:1 aspect.
        Assert.Equal(12, FretboardSizeRules.ClampStringGap(30, 6, 300), Tolerance);
    }

    [Xunit.Theory]
    [Xunit.InlineData(4)]
    [Xunit.InlineData(6)]
    [Xunit.InlineData(7)]
    public void MaxPaneHeightIsTheHeightWhereTheGapReachesItsCap(int strings)
    {
        var max = FretboardSizeRules.MaxPaneHeight(strings, Padding);
        foreach (var width in Widths)
        foreach (var frets in FretCounts)
        {
            var atMax = LayoutAt(strings, frets, width, max);
            Assert.True(atMax.StringGap <= FretboardSizeRules.StringGapMaxPx + 1e-9);
            var beyond = LayoutAt(strings, frets, width, max + 80);
            Assert.True(beyond.StringGap <= FretboardSizeRules.StringGapMaxPx + 1e-9);
            Assert.True(beyond.Board.Height <= (strings - 1) * FretboardSizeRules.StringGapMaxPx + 1e-9, "the board grows past the cap");
        }
    }

    [Xunit.Theory]
    [Xunit.InlineData(4)]
    [Xunit.InlineData(6)]
    [Xunit.InlineData(7)]
    public void BoardStaysInsideThePaneWithAGapAndAspectInRange(int strings)
    {
        var gaps = strings - 1;
        foreach (var width in Widths)
        foreach (var frets in FretCounts)
        for (var height = FretboardSizeRules.MinPaneHeight(strings, Padding); height <= FretboardSizeRules.MaxPaneHeight(strings, Padding) + 40; height += 4)
        {
            var layout = LayoutAt(strings, frets, width, height);
            var board = layout.Board;
            Assert.True(board.X >= 0, $"board starts outside the pane: {strings} strings, {frets} frets, {width} wide, {height} high");
            Assert.True(board.Right + FretboardGeometryRules.LegendWidth <= width - InstrumentPanelView.CloseButtonReserve + Tolerance,
                $"board and legend run past the pane: {strings} strings, {frets} frets, {width} wide, {height} high");
            Assert.True(layout.StringGap >= FretboardSizeRules.StringGapMinPx - 1e-9 && layout.StringGap <= FretboardSizeRules.StringGapMaxPx + 1e-9,
                $"string gap {layout.StringGap:F1} outside its range");
            // The aspect is only promised where the pane is tall enough for the gap the aspect needs and not taller than the cap allows.
            var aspect = board.Width / board.Height;
            var lowestAspectHeight = gaps * Math.Max(FretboardSizeRules.StringGapMinPx, board.Width / (FretboardSizeRules.AspectMax * gaps)) + Padding;
            var highestAspectHeight = gaps * Math.Min(FretboardSizeRules.StringGapMaxPx, board.Width / (FretboardSizeRules.AspectMin * gaps)) + Padding;
            if (height < lowestAspectHeight || height > highestAspectHeight) continue;
            Assert.True(aspect >= FretboardSizeRules.AspectMin - 0.05 && aspect <= FretboardSizeRules.AspectMax + 0.05,
                $"aspect {aspect:F2} outside {FretboardSizeRules.AspectMin}..{FretboardSizeRules.AspectMax}: {strings} strings, {frets} frets, {width} wide, {height} high");
        }
    }
}
