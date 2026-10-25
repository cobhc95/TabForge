using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views;

/// <summary>
/// A thin vertical bar over the track rows, beside the track list, visible only while the rows overflow the pane. Its Maximum is the timeline's
/// largest row offset and its thumb is the visible share of the rows; dragging the thumb sets the timeline's RowScrollOffset.
/// Owns: the bar's range, thumb, place and visibility, read from the timeline. Does not own: the offset clamp and the wheel (TimelineView), the row
/// geometry (RowScrollRules, ArrangementRowPresenter) and the list and strip (ArrangementView).
/// Tests: TabForge.Avalonia.Tests.TrackListScrollTests.
/// Threads: UI thread only.
/// </summary>
public sealed class TrackRowScrollBar : ScrollBar
{
    private readonly TimelineView _timeline;
    private bool _syncing;

    public TrackRowScrollBar(TimelineView timeline)
    {
        _timeline = timeline;
        Name = "TrackRowScrollBar";
        Orientation = Orientation.Vertical;
        VerticalAlignment = VerticalAlignment.Top;
        Width = AvaloniaScrollBarStyles.ScrollBarThickness;
        ValueChanged += (_, e) => { if (!_syncing) _timeline.RowScrollOffset = e.NewValue; };
        timeline.RowScrollChanged += Refresh;
        timeline.RowsChanged += Refresh;
        timeline.LayoutUpdated += (_, _) => Refresh();
    }

    /// <summary>Reads the range, thumb and place from the timeline; writes a property only when its value changed.</summary>
    public void Refresh()
    {
        var max = _timeline.MaxRowScrollOffset;
        var visible = max > 0;
        if (IsVisible != visible) IsVisible = visible;
        var top = _timeline.TrackRowsTop;
        var height = Math.Max(0, _timeline.TrackRowsBottom - top);
        if (Margin.Top != top) Margin = new Thickness(0, top, 0, 0);
        if (Height != height) Height = height;
        _syncing = true;
        if (Maximum != max) Maximum = max;
        if (visible && ViewportSize != _timeline.RowsViewportHeight) ViewportSize = _timeline.RowsViewportHeight;
        if (Value != _timeline.RowScrollOffset) Value = _timeline.RowScrollOffset;
        _syncing = false;
    }
}
