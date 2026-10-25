// Owns: the arrangement timeline's pure number maths: bar width from slots, bar lookup by x, zoom steps and fit-all, and the
//       row-height and pane-height sums. Every method takes plain numbers and allocates nothing.
// Does not own: the bar boundary arrays (TimelineGeometry), the project, scroll offsets or any drawing.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

public static class TimelineGeometryRules
{
    /// <summary>Width of a bar of <paramref name="slots"/> sixteenth slots: the unit width scaled by slots/16, kept between a quarter and 4 units.</summary>
    public static double BarWidth(double unitWidth, int slots) => unitWidth * Math.Clamp(slots / 16.0, 0.25, 4.0);

    /// <summary>The x of bar <paramref name="bar"/> (clamped; the bar count gives the right edge) in the boundary list of barCount + 1 entries.</summary>
    public static double XOfBar(ReadOnlySpan<double> boundaries, int bar)
    {
        var barCount = boundaries.Length - 1;
        return boundaries[Math.Clamp(bar, 0, barCount)];
    }

    /// <summary>The right edge of bar <paramref name="bar"/>.</summary>
    public static double RightOfBar(ReadOnlySpan<double> boundaries, int bar) => boundaries[Math.Clamp(bar + 1, 0, boundaries.Length - 1)];

    /// <summary>The bar containing x: 0 before the start or with no bars, the last bar at or past the end or for NaN.</summary>
    public static int BarAt(ReadOnlySpan<double> boundaries, double x)
    {
        var barCount = boundaries.Length - 1;
        if (barCount <= 0) return 0;
        if (double.IsNaN(x) || x >= boundaries[barCount]) return barCount - 1;
        if (x <= 0) return 0;
        var low = 0;
        var high = boundaries.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (boundaries[middle] <= x) low = middle + 1;
            else high = middle;
        }
        return Math.Clamp(low - 1, 0, barCount - 1);
    }

    /// <summary>The y inside the rows (from the first row) for a y in the lane control, or -1 above the grid.</summary>
    public static double RowsYAt(double y, double gridTop, double verticalScroll) => y < gridTop ? -1 : y + verticalScroll - gridTop;

    /// <summary>The measure width at which the whole song exactly fills the viewport (12 when unknown, kept between 1.5 and 12).</summary>
    public static double FitAllMeasureWidth(double measureWidth, double totalWidth, double viewportWidth) =>
        totalWidth <= 0 || viewportWidth <= 0 ? 12 : Math.Clamp(measureWidth * viewportWidth / totalWidth, 1.5, 12);

    /// <summary>The next measure width: +/-4 px per step at normal sizes, proportional steps near 12 px or less, never beyond fit-all or 90.</summary>
    public static double ZoomStep(double measureWidth, bool zoomIn, double fitAllWidth)
    {
        var next = zoomIn ? (measureWidth < 12 ? measureWidth / 0.8 : measureWidth + 4) : (measureWidth <= 12 ? measureWidth * 0.8 : measureWidth - 4);
        return Math.Clamp(next, Math.Min(12, fitAllWidth), 90);
    }

    /// <summary>Height that shows the header plus <paramref name="rowsHeight"/> of rows.</summary>
    public static double PreferredHeight(double rulerHeight, double sectionHeight, double rowsHeight, double addLaneExtra, double topMargin) =>
        rulerHeight + sectionHeight + rowsHeight + addLaneExtra + 2 + topMargin;

    /// <summary>The preferred height when each of <paramref name="visibleRows"/> rows is <paramref name="rowHeight"/> instead of the current height.</summary>
    public static double PreferredHeightAt(double preferredHeight, int visibleRows, double rowHeight, double currentRowHeight) =>
        preferredHeight + visibleRows * (rowHeight - currentRowHeight);

    /// <summary>The track row height that makes the rows fill <paramref name="paneHeight"/>, between the default and the maximum.</summary>
    public static double RowHeightForPaneHeight(double paneHeight, double preferredHeight, int visibleRows, double currentRowHeight, double minHeight, double maxHeight)
    {
        if (visibleRows == 0) return minHeight;
        var fixedPart = preferredHeight - visibleRows * currentRowHeight;
        return Math.Clamp((paneHeight - fixedPart) / visibleRows, minHeight, maxHeight);
    }
}
