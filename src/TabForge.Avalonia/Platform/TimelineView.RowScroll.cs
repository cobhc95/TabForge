using System;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the timeline's vertical row scroll and its height properties (the offset, the content, visible and viewport heights), split from TimelineView.cs.
// Does not own: the rows themselves or the pane that sets the height (TimelineView).
public sealed partial class TimelineView : Control
{
    private double _rowScrollOffset;

    /// <summary>
    /// How far the track rows are scrolled up, in DIPs, from 0 to <see cref="MaxRowScrollOffset"/>. Every row moves with it (<see cref="RowTop"/>);
    /// the ruler, sections, master lane and Add track strip stay put. Raises <see cref="RowScrollChanged"/> when it changes.
    /// </summary>
    public double RowScrollOffset
    {
        get => _rowScrollOffset;
        set
        {
            var clamped = RowScrollRules.Clamp(value, TrackRowsContentHeight, RowsViewportHeight);
            if (clamped == _rowScrollOffset) return;
            _rowScrollOffset = clamped;
            OnRowsScrolled();
        }
    }

    /// <summary>The largest <see cref="RowScrollOffset"/>: the rows that do not fit the pane (0 when they all fit).</summary>
    public double MaxRowScrollOffset => RowScrollRules.MaxOffset(TrackRowsContentHeight, RowsViewportHeight);

    /// <summary>Raised when <see cref="RowScrollOffset"/> changes, so the track list and its scroll bar follow.</summary>
    public event Action? RowScrollChanged;

    /// <summary>Height of the track rows (all of them, scrolled or not), without the master lane.</summary>
    public double TrackRowsContentHeight => _rows.Count == 0 ? 0 : ArrangementRowPresenter.RowsHeight(_rows, RowHeight) - MasterLaneHeight;

    /// <summary>Height the track rows may show: the pane less the fixed parts; unbounded while the pane height is unknown.</summary>
    public double RowsViewportHeight => PaneHeight > 0 ? Math.Max(0, PaneHeight - FixedRowsHeight) : double.PositiveInfinity;

    /// <summary>The screen top of the scrolling track rows: under the ruler, the sections and the master lane.</summary>
    public double TrackRowsTop => RulerHeight + SectionHeight + MasterLaneHeight;

    /// <summary>The screen bottom of the scrolling track rows: the timeline's visible bottom. The Add track strip sits directly under it, in the arrangement's own row.</summary>
    public double TrackRowsBottom => VisibleHeight;

    /// <summary>The height the whole timeline would take with every row and no pane limit.</summary>
    public double ContentHeight => RulerHeight + SectionHeight + ArrangementRowPresenter.RowsHeight(_rows, RowHeight);

    /// <summary>
    /// The height the timeline takes with every row at its base height, whatever the pane. The Auto-resize fit targets this height, so a stretched
    /// row never feeds back into the pane that stretched it; it is the same as <see cref="ContentHeight"/> whenever the rows are not stretched.
    /// </summary>
    public double BaseContentHeight
    {
        get
        {
            var baseRows = new Rows(_compact ? 0 : _song.Tracks.Count, MasterLaneHeight, _baseRowHeight, _song.Tracks, this);
            return RulerHeight + SectionHeight + ArrangementRowPresenter.RowsHeight(baseRows, _baseRowHeight);
        }
    }

    /// <summary>The height the timeline shows: its content rounded up to whole pixels (the dock snaps the pane to whole pixels, so a fractional row height would leave a sliver), or the pane when the content is taller.</summary>
    public double VisibleHeight => PaneHeight > 0 ? Math.Min(Math.Ceiling(ContentHeight - 1e-6), PaneHeight) : ContentHeight;

    /// <summary>Scrolls the rows so the track at <paramref name="row"/> is inside the pane (a selected or dragged track stays in view).</summary>
    public void EnsureRowVisible(int row)
    {
        if (row < 0 || row >= RowCount) return;
        var rowTop = ArrangementRowPresenter.RowTop(_rows, row) - MasterLaneHeight;
        RowScrollOffset = RowScrollRules.ShowRow(_rowScrollOffset, rowTop, RowHeight, RowsViewportHeight);
    }

    /// <summary>Scrolls the rows by a mouse wheel's vertical movement; Ctrl + wheel is the zoom and is left alone. Returns whether the wheel was used.</summary>
    public bool ScrollRowsByWheel(PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || e.KeyModifiers.HasPrimary()) return false;
        RowScrollOffset -= e.Delta.Y * RowScrollRules.WheelNotch;
        e.Handled = true;
        return true;
    }

    /// <summary>Re-reads the timeline's height and scroll range after the pane or the rows changed; the offset is clamped to the new range.</summary>
    private void RefreshRowScroll()
    {
        Height = VisibleHeight;
        var clamped = RowScrollRules.Clamp(_rowScrollOffset, TrackRowsContentHeight, RowsViewportHeight);
        if (clamped == _rowScrollOffset) return;
        _rowScrollOffset = clamped;
        OnRowsScrolled();
    }

    private void OnRowsScrolled()
    {
        InvalidateVisual();
        PlayheadLayer.Redraw();
        LiveTakeLayer.InvalidateVisual();
        RowScrollChanged?.Invoke();
    }
}
