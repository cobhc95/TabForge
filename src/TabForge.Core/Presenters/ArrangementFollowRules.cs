using System;

namespace TabForge.Presenters;

// Owns: the horizontal follow of the arrangement timeline: a playhead or a clicked bar that lands in the first or last few visible bars pages the
//     view by half its width toward that edge (clamped to the song's ends), and a playhead outside the view centres it.
// Does not own: the scroll viewer that moves (the Avalonia arrangement's follow hook), the playhead position (TimelineView), the zoom.
// Tests: TabForge.Avalonia.Tests.TimelineFollowTests, TabForge.Avalonia.Tests.ArrangementSidewaysScrollBarTests.
// Threads: any; no state.
public static class ArrangementFollowRules
{
    /// <summary>A bar counts as an edge bar when it is among this many first or last visible bars.</summary>
    public const int EdgeBarCount = 3;

    /// <summary>One page of follow scrolling as a share of the view's width: half a page, so the bar stays in the middle portion of the view.</summary>
    public const double HalfPageShare = 0.5;

    /// <summary>True when the playhead at <paramref name="playheadX"/> lies outside the view, so the view must centre on it.</summary>
    public static bool NeedsSeekIntoView(double playheadX, double horizontalOffset, double viewportWidth)
        => playheadX < horizontalOffset || playheadX > horizontalOffset + viewportWidth;

    /// <summary>The offset that centres the view on <paramref name="playheadX"/>, never below 0.</summary>
    public static double CentredOffset(double playheadX, double viewportWidth)
        => Math.Max(0, playheadX - Math.Max(0, viewportWidth) * HalfPageShare);

    /// <summary>
    /// The offset after bar <paramref name="bar"/> is shown: a bar among the last <see cref="EdgeBarCount"/> visible bars pages right by half a page,
    /// a bar among the first <see cref="EdgeBarCount"/> pages left unless the view already starts at bar 0. The result is clamped to the scrollable width.
    /// Any other bar leaves the offset as it is.
    /// </summary>
    public static double PagedOffsetForEdgeBar(int bar, int firstVisibleBar, int lastVisibleBar, double horizontalOffset, double viewportWidth, double scrollableWidth)
    {
        var page = Math.Max(0, viewportWidth) * HalfPageShare;
        var scrollable = Math.Max(0, scrollableWidth);
        if (bar > lastVisibleBar - EdgeBarCount) return Math.Clamp(horizontalOffset + page, 0, scrollable);
        if (firstVisibleBar > 0 && bar < firstVisibleBar + EdgeBarCount) return Math.Clamp(horizontalOffset - page, 0, scrollable);
        return horizontalOffset;
    }
}
