using System;

namespace TabForge.Avalonia.Platform;

// Owns: the vertical scroll of the track rows as pure arithmetic: the largest offset, the clamp, the offset that brings one row into view and
//     the distance one mouse-wheel notch moves.
// Does not own: the offset value (TimelineView holds it), the scroll bar (TrackRowScrollBar), the row geometry (ArrangementRowPresenter).
// Tests: TabForge.Avalonia.Tests.TrackListScrollTests.
// Threads: UI thread only; pure arithmetic, no state.
public static class RowScrollRules
{
    /// <summary>How far one mouse-wheel notch scrolls the track rows, in DIPs: one default track row.</summary>
    public const double WheelNotch = TimelineView.DefaultRowHeight;

    /// <summary>The largest offset: the rows' height that does not fit the viewport (0 when they all fit or the viewport is unknown).</summary>
    public static double MaxOffset(double contentHeight, double viewportHeight) => Math.Max(0, contentHeight - viewportHeight);

    /// <summary>The offset clamped to 0 and <see cref="MaxOffset"/>; a non-finite offset reads as 0.</summary>
    public static double Clamp(double offset, double contentHeight, double viewportHeight) =>
        Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, MaxOffset(contentHeight, viewportHeight));

    /// <summary>The offset that keeps the row from <paramref name="rowTop"/> to <paramref name="rowTop"/> + <paramref name="rowHeight"/> inside the viewport at <paramref name="offset"/>.</summary>
    public static double ShowRow(double offset, double rowTop, double rowHeight, double viewportHeight)
    {
        if (rowTop < offset) return rowTop;
        if (rowTop + rowHeight > offset + viewportHeight) return rowTop + rowHeight - viewportHeight;
        return offset;
    }
}
