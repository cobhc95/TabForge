using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the composition of the arrangement timeline's hit queries: one press's header cell first, else its section slot, row and bar plus the
//       grid and lane flags, and the hover query (the row, the bar inside it, and the section strip). It asks the existing geometry only.
// Does not own: the geometry maths (TimelineView.BarAtX/RowAtY/SectionSlots/SectionMarkerAt, TrackHeaderColumn.CellAt, TimelineAudioClips.ClipAt),
//       the gestures and drags (TimelineInputController) or any drawing.
// Tests: TabForge.Avalonia.Tests.TimelineHitTesterTests, TabForge.Avalonia.Tests.TimelineInputTests, TabForge.Avalonia.Tests.TimelineHoverTests.

/// <summary>One press on the arrangement: a header cell, or the section slot, row and bar under it plus the grid and lane flags.</summary>
internal readonly record struct TimelineHit(
    int Bar,
    int Row,
    int SectionSlot,
    MarkerModel? Section,
    bool OnGrid,
    bool OnLanes,
    TimelineView.SectionSlot[] Slots,
    (int Row, TrackRowCells.Cell Cell)? HeaderCell);

/// <summary>One hover on the arrangement: the row under the pointer, the bar inside it (only over the row's tab, not its audio lanes) and the section block under it, if any.</summary>
internal readonly record struct TimelineHover(int Bar, int Row, MarkerModel? Section);

/// <summary>The hit-query composition of the arrangement timeline; the geometry itself stays in the view, the header and the clip painter.</summary>
internal sealed class TimelineHitTester
{
    private readonly TimelineView _view;
    private readonly TrackHeaderColumn? _header;

    public TimelineHitTester(TimelineView view, TrackHeaderColumn? header = null)
    {
        _view = view;
        _header = header;
    }

    /// <summary>
    /// The press hit: a track-header cell wins; otherwise the view's section strip, then its row and bar. <see cref="TimelineHit.OnGrid"/> is the
    /// source being the timeline and <see cref="TimelineHit.OnLanes"/> adds a track row (a drag there selects a bar range).
    /// </summary>
    public TimelineHit Hit(Point point, Control source)
    {
        if (ReferenceEquals(source, _header) && _header!.CellAt(point) is { } cell)
            return new TimelineHit(-1, -1, -1, null, false, false, Array.Empty<TimelineView.SectionSlot>(), cell);
        var onGrid = ReferenceEquals(source, _view);
        var row = _view.RowAtY(point.Y);
        var slots = onGrid ? _view.SectionSlots() : Array.Empty<TimelineView.SectionSlot>();
        var slot = SectionDragPresenter.HitIndex(slots, point.X, point.Y);
        return new TimelineHit(
            _view.BarAtX(point.X),
            row,
            slot,
            slot >= 0 ? _view.SectionMarkerAt(slots[slot].MarkerIndex) : null,
            onGrid,
            onGrid && row >= 0,
            slots,
            null);
    }

    /// <summary>
    /// The hover query while no gesture runs: the row, the bar inside it (none over the ruler, the sections or an audio lane: the tab shade shows only
    /// over the tab), and the section strip.
    /// </summary>
    public TimelineHover HoverAt(Point point)
    {
        var row = _view.RowAtY(point.Y);
        var bar = row >= 0 && _view.IsOverNotationBand(point.Y, row) ? _view.BarAtX(point.X) : -1;
        return new TimelineHover(bar, row, SectionAt(point));
    }

    /// <summary>The audio clip under a press; the controller asks this on release, as it always did.</summary>
    public TimelineClipSelection? ClipAt(Point point) => TimelineAudioClips.ClipAt(_view, point);

    // Only the strip's band is tested, so a pointer over the lanes costs no section lookup (the controller's old hover path did the same).
    private MarkerModel? SectionAt(Point point)
    {
        if (point.Y < TimelineView.RulerHeight || point.Y >= TimelineView.RulerHeight + TimelineView.SectionHeight) return null;
        var slots = _view.SectionSlots();
        var slot = SectionDragPresenter.HitIndex(slots, point.X, point.Y);
        return slot >= 0 ? _view.SectionMarkerAt(slots[slot].MarkerIndex) : null;
    }
}
