// Owns: the master track lane's row and line numbers without UI types: how many track rows the lane takes above track 1, its height,
//       the height and gap of one track's line, and whether a point lies on the strip.
// Does not own: which songs show it (MasterLane), its colours, which bars a line fills or drawing (TrackTimeline.MasterLane.cs).
// Tests: MasterLaneRowTests.
namespace TabForge.Presenters;

/// <summary>Pure master lane geometry rules. Static and allocation-free. Thread: UI thread.</summary>
public static class MasterLaneRules
{
    /// <summary>Padding above the first line and below the last, together.</summary>
    private const double LinePadding = 4;

    /// <summary>Lines thinner than this sit flush, because a gap would leave nothing to see.</summary>
    private const double GapMinimumLine = 3;

    /// <summary>Rows the lane adds above track 1: 0 hidden, 1 shown, 2 in compact mode.</summary>
    public static int Rows(bool shown, bool compact) => !shown ? 0 : compact ? 2 : 1;

    /// <summary>Height the lane takes above track 1.</summary>
    public static double Height(int rows, double rowHeight) => rows * rowHeight;

    /// <summary>Height of one track's line (gap included) in a lane of <paramref name="laneHeight"/>.</summary>
    public static double LineHeight(double laneHeight, int trackCount) => (laneHeight - LinePadding) / trackCount;

    /// <summary>Space left between two lines of height <paramref name="lineHeight"/>.</summary>
    public static double LineGap(double lineHeight) => lineHeight >= GapMinimumLine ? 1 : 0;

    /// <summary>The point <paramref name="yInGrid"/> (from the grid's top) lies on a strip of <paramref name="laneHeight"/>.</summary>
    public static bool OnStrip(double yInGrid, double laneHeight) => yInGrid >= 0 && yInGrid < laneHeight;
}
