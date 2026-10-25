using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: moving the arrangement's sideways scroll viewer so the playhead and a clicked bar are seen: a playhead outside the view is centred on,
//     and a bar among the first or last visible bars pages the view by half a page toward that edge.
// Does not own: the playhead position (TimelineView), the thresholds and paging arithmetic (ArrangementFollowRules), the scroll bar and wheel (ArrangementView).
// Tests: TabForge.Avalonia.Tests.TimelineFollowTests.
// Threads: UI thread only; reads the timeline's layout and sets the viewer's offset.
internal static class TimelineFollowScroll
{
    /// <summary>Follows the playhead of <paramref name="timeline"/>: centres on it when it is outside the view, else pages if it is at an edge.</summary>
    public static void FollowPlayhead(ScrollViewer sideways, TimelineView timeline)
    {
        var x = timeline.PlayheadX();
        var viewport = sideways.Viewport.Width;
        if (double.IsNaN(x) || viewport <= UiTiming.TimelineMinViewportDips) return;
        if (ArrangementFollowRules.NeedsSeekIntoView(x, sideways.Offset.X, viewport))
        {
            sideways.Offset = new Vector(ArrangementFollowRules.CentredOffset(x, viewport), sideways.Offset.Y);
            return;
        }
        PageForEdgeBar(sideways, timeline, timeline.PlayheadBar);
    }

    /// <summary>Pages the view by half a page when <paramref name="bar"/> is among the first or last visible bars; a bar elsewhere leaves it still.</summary>
    public static void PageForEdgeBar(ScrollViewer sideways, TimelineView timeline, int bar)
    {
        var viewport = sideways.Viewport.Width;
        if (bar < 0 || bar >= timeline.BarCount || viewport <= UiTiming.TimelineMinViewportDips) return;
        var offset = sideways.Offset.X;
        var firstVisible = Math.Clamp(timeline.BarAtX(offset), 0, timeline.BarCount - 1);
        var lastVisible = Math.Clamp(timeline.BarAtX(offset + viewport - 1), 0, timeline.BarCount - 1);
        var scrollable = Math.Max(0, sideways.Extent.Width - viewport);
        var next = ArrangementFollowRules.PagedOffsetForEdgeBar(bar, firstVisible, lastVisible, offset, viewport, scrollable);
        if (Math.Abs(next - offset) > UiTiming.TimelineScrollEpsilonDips) sideways.Offset = new Vector(next, sideways.Offset.Y);
    }
}
