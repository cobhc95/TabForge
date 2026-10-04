using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// ArrangementPanel: timeline zoom, binding and refresh, playhead and follow, bar/track/range selection, area move.
public sealed partial class ArrangementPanel
{
    private void SetMeasureWidth(double width)
    {
        if (Math.Abs(MeasureWidth - width) < 0.01) return;
        SlowTrace.ZoomStep();
        using var slowTrace = SlowTrace.Measure("zoom step (set width, extent)", 2);
        MeasureWidth = width;
        _timeline.MeasureWidth = width;
        _timeline.ClearHover();
        _timeline.InvalidateMeasure();
        _timeline.InvalidateVisual();
        RefreshTimelineExtent();
        // Rebuild the section bracket geometry using the new bar widths immediately; otherwise its
        // cached endpoints remain at the previous zoom until playback enters a different section.
        LayoutSectionHighlight(_playheadBar);
    }

    private double PlayheadViewportX()
    {
        if (_playheadBar < 0) return _horizontal.ViewportWidth * 0.5;
        var contentX = _timeline.XOfBar(_playheadBar) +
                       _timeline.BarWidthOf(_playheadBar) * Math.Clamp(_playheadFraction, 0, 1);
        return Math.Clamp(contentX - _horizontal.HorizontalOffset, 0, Math.Max(0, _horizontal.ViewportWidth));
    }

    // Zoom-out limit: the whole song exactly fills the visible timeline (never empty space after the last bar).
    private double FitAllMeasureWidth()
    {
        var total = _timeline.TotalWidth;
        var viewport = _horizontal.ViewportWidth > 0 ? _horizontal.ViewportWidth : _horizontal.ActualWidth;
        if (total <= 0 || viewport <= 0) return 12;
        return Math.Clamp(MeasureWidth * viewport / total, 1.5, 12);
    }

    // +/-4 px per step at normal sizes; proportional steps below 12 px so the far zoom-out stays smooth.
    private double ZoomStep(bool zoomIn)
    {
        var w = MeasureWidth;
        var next = zoomIn ? (w < 12 ? w / 0.8 : w + 4) : (w <= 12 ? w * 0.8 : w - 4);
        return Math.Clamp(next, Math.Min(12, FitAllMeasureWidth()), 90);
    }

    private void ZoomTimeline(double width, double viewportAnchorX)
    {
        var oldWidth = MeasureWidth;
        if (Math.Abs(oldWidth - width) < 0.01) return;
        var oldContentAnchor = _horizontal.HorizontalOffset + viewportAnchorX;
        _timeline.SetWaveZooming(true);
        (_waveSettle ??= new SettleAction(() => _timeline.SetWaveZooming(false), 150)).Request();
        SetMeasureWidth(width);
        var scale = MeasureWidth / oldWidth;
        var generation = ++_zoomGeneration;

        // Layout changes the scroll extent. Apply the zoom anchor only after that extent settles,
        // then re-run follow so this delayed zoom adjustment cannot undo playback tracking.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (generation != _zoomGeneration) return;
            _horizontal.UpdateLayout();
            _horizontal.ScrollToHorizontalOffset(Math.Max(0, oldContentAnchor * scale - viewportAnchorX));
            _horizontal.UpdateLayout();
            LayoutPlayhead();
            LayoutSectionHighlight(_playheadBar);
            if (_statusPlaybackActive && _playheadBar >= 0) FollowPlayhead();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SyncTimelineScrollBar()
    {
        _syncingHorizontalScroll = true;
        _timelineScrollBar.Minimum = 0;
        _timelineScrollBar.Maximum = Math.Max(0, _horizontal.ScrollableWidth);
        _timelineScrollBar.ViewportSize = Math.Max(0, _horizontal.ViewportWidth);
        _timelineScrollBar.LargeChange = Math.Max(1, _horizontal.ViewportWidth * 0.8);
        _timelineScrollBar.SmallChange = 24;
        _timelineScrollBar.Value = Math.Clamp(_horizontal.HorizontalOffset, 0, _timelineScrollBar.Maximum);
        // Keep the navigation rail visible even when the whole song currently fits in view.
        _timelineScrollBar.IsEnabled = true;
        _syncingHorizontalScroll = false;
    }

    public event EventHandler? AddTrackRequested;

    public void Bind(SongProject project, IReadOnlyList<MidiOutputDeviceInfo> devices, TabForge.Services.MediaContext? media = null)
    {
        _project = project;
        _devices = devices;
        _timeline.Media = media ?? TabForge.Services.MediaContext.Anonymous;   // the bound song's media context: base directory and approvals for its clips
        _timeline.Project = project;
        _timeline.MeasureWidth = MeasureWidth;
        _timeline.ValidateTimelineGeometry();
        // A structural edit (bars cleared in place, inserted or removed) can keep the same bar objects: the bar cells' note
        // summaries are recomputed so emptied bars show empty.
        _timeline.InvalidateActivities();
        RebuildControls();
        RefreshEmptyAreaMenus();
        RefreshTimelineExtent();
        _timeline.InvalidateMeasure();
        _timeline.InvalidateVisual();
    }

    public void RefreshAll()
    {
        _timeline.InvalidateMeasure();
        _timeline.InvalidateVisual();
        RebuildControls();
    }

    /// <summary>Refreshes the score-order visuals without rebuilding unchanged track-control rows.</summary>
    public void RefreshSectionOrder(int selectedBar, int selectedTrack)
    {
        _timeline.RebuildTimelineGeometry();
        _timeline.InvalidateActivities();
        _timeline.InvalidateSectionHits();
        _timeline.SelectedBar = selectedBar;
        _timeline.SelectedTrack = selectedTrack;
        _timeline.InvalidateVisual();
        LayoutPlayhead();
        LayoutSectionHighlight(_playheadBar);
        UpdateDragVisual();
    }

    public void RefreshScore() { _timeline.InvalidateVisual(); }

    public void InvalidateActivities(int trackIndex, int firstBar, int lastBar)
        => _timeline.InvalidateActivities(trackIndex, firstBar, lastBar);

    /// <summary>Call after a structural bar/time-signature edit; unchanged visual state never rebuilds geometry.</summary>
    /// <summary>Re-renders the timeline (e.g. after a theme change) and drops its cached lane drawings.</summary>
    public void InvalidateTimeline()
    {
        _timeline.InvalidateSectionHits();
        _timeline.InvalidateVisual();
        InvalidateVisual();
    }

    public void RefreshTimelineGeometry()
    {
        _timeline.RebuildTimelineGeometry();
        _timeline.InvalidateSectionHits();
        _timeline.InvalidateMeasure();
        _timeline.InvalidateVisual();
        RefreshTimelineExtent();
        LayoutSectionHighlight(_playheadBar);
    }

    /// <summary>Invalidates marker-derived drawing/hit metadata without touching timeline geometry.</summary>
    public void RefreshSections()
    {
        _timeline.InvalidateSectionHits();
        _timeline.InvalidateVisual();
        LayoutSectionHighlight(_playheadBar);
    }

    public void SetPlayhead(int bar, double fraction, bool playbackActive = false, bool playbackPaused = false)
    {
        // Nothing here re-renders the measure cells: the playhead and active-section brackets
        // are both overlays, so following playback stays cheap at every bar line.
        if (bar != _playheadBar) LayoutSectionHighlight(bar);
        _playheadBar = bar;
        _playheadFraction = fraction;
        _statusPlaybackActive = playbackActive;
        LayoutPlayhead();
        FollowPlayhead();
        QueuePlayheadFollow();
    }

    private void QueuePlayheadFollow()
    {
        if (_playheadFollowQueued) return;
        _playheadFollowQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _playheadFollowQueued = false;
            if (!IsLoaded || _playheadBar < 0) return;
            _horizontal.UpdateLayout();
            FollowPlayhead();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void LayoutSectionHighlight(int bar)
    {
        _lastSectionHighlightBar = bar;
        if (_project is null || bar < 0 || !_showSectionBrackets)
        {
            _sectionHighlight.Visibility = Visibility.Collapsed;
            return;
        }
        var section = _timeline.SectionGeometryAtBar(bar);
        if (section is not { } active)
        {
            _sectionHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        var start = active.StartBar;
        var end = Math.Max(start + 1, active.EndBar);
        var x = _timeline.XOfBar(start);
        var width = Math.Max(4, _timeline.XOfBar(end) - x);
        var top = RulerHeight + SectionHeight;
        var height = Math.Max(10, _timeline.ActualHeight - top);
        var arm = Math.Min(18, Math.Max(8, width * 0.18));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Left bracket: [
            context.BeginFigure(new Point(arm, 0), false, false);
            context.LineTo(new Point(0, 0), true, false);
            context.LineTo(new Point(0, height), true, false);
            context.LineTo(new Point(arm, height), true, false);

            // Right bracket: ]
            context.BeginFigure(new Point(width - arm, 0), false, false);
            context.LineTo(new Point(width, 0), true, false);
            context.LineTo(new Point(width, height), true, false);
            context.LineTo(new Point(width - arm, height), true, false);
        }
        geometry.Freeze();
        _sectionHighlight.Data = geometry;
        _sectionHighlight.Stroke = Draw.Solid(active.Color);
        _sectionHighlight.Visibility = Visibility.Visible;
        _sectionHighlight.Width = width;
        _sectionHighlight.Height = height;
        Canvas.SetLeft(_sectionHighlight, x);
        Canvas.SetTop(_sectionHighlight, top);
    }

    private (int Bar, int Track, Rect Cell) _barMarkerKey;

    /// <summary>The cell the bar marker sits in: the playhead's bar on the selected track (the first track when none is selected).</summary>
    internal Rect? BarMarkerCell() =>
        _playheadBar < 0 || (_project?.Tracks.Count ?? 0) == 0 ? null
        : _timeline.CellBounds(_playheadBar, _timeline.SelectedTrack >= 0 && _timeline.SelectedTrack < _project!.Tracks.Count ? _timeline.SelectedTrack : 0);

    /// <summary>The marker's rectangle inside <paramref name="cell"/>: a square of 45% of the cell height, centred.</summary>
    internal static Rect BarMarkerRect(Rect cell)
    {
        var side = Math.Min(Math.Round(cell.Height * 0.45), Math.Max(4, cell.Width - 2));
        return new Rect(cell.X + (cell.Width - side) / 2, cell.Y + (cell.Height - side) / 2, side, side);
    }

    internal Rect BarMarkerBounds => new(Canvas.GetLeft(_barMarker), Canvas.GetTop(_barMarker), _barMarker.Width, _barMarker.Height);
    internal bool BarMarkerVisible => _barMarker.Visibility == Visibility.Visible;
    internal bool PlayheadLineVisible => _playheadLine.Visibility == Visibility.Visible;

    // Moves the one cached marker; does nothing unless the bar, the track, the cell geometry or the style changed.
    private void LayoutBarMarker()
    {
        var cell = PlayheadStyles.ShowsBarMarker(_playheadStyle) ? BarMarkerCell() : null;
        if (cell is null || cell.Value.Y < RulerHeight + SectionHeight)   // scrolled under the ruler: nothing to show
        {
            _barMarker.Visibility = Visibility.Collapsed;
            _barMarkerKey = default;
            return;
        }
        var key = (_playheadBar, _timeline.SelectedTrack, cell.Value);
        if (key.Equals(_barMarkerKey) && _barMarker.Visibility == Visibility.Visible) return;
        _barMarkerKey = key;
        var r = BarMarkerRect(cell.Value);
        _barMarker.Width = r.Width; _barMarker.Height = r.Height;
        _barMarker.RadiusX = _barMarker.RadiusY = Math.Max(2, r.Width * 0.22);
        Canvas.SetLeft(_barMarker, r.X);
        Canvas.SetTop(_barMarker, r.Y);
        _barMarker.Visibility = Visibility.Visible;
    }

    private void LayoutHoverCell(int bar, int track)
    {
        if (_timeline.CellBounds(bar, track) is not { } cell || cell.Y < RulerHeight + SectionHeight)
        {
            _hoverCell.Visibility = Visibility.Collapsed;
            return;
        }
        _hoverCell.Fill = VisualTheme.IsLight ? HoverShadeLight : HoverShadeDark;
        _hoverCell.Width = cell.Width; _hoverCell.Height = cell.Height;
        Canvas.SetLeft(_hoverCell, cell.X);
        Canvas.SetTop(_hoverCell, cell.Y);
        _hoverCell.Visibility = Visibility.Visible;
    }

    internal Rect? HoverCellBounds => _hoverCell.Visibility == Visibility.Visible
        ? new Rect(Canvas.GetLeft(_hoverCell), Canvas.GetTop(_hoverCell), _hoverCell.Width, _hoverCell.Height) : null;
    /// <summary>Test / diagnostic hook: what the mouse-move handler does for a pointer at <paramref name="p"/> (timeline coordinates).</summary>
    internal void SimulateHover(Point p) => _timeline.SimulateHover(p);
    /// <summary>Centre of a bar cell in timeline coordinates (for hover diagnostics and tests).</summary>
    internal Point? CellCentre(int bar, int track) =>
        _timeline.CellBounds(bar, track) is { } c ? new Point(c.X + c.Width / 2, c.Y + c.Height / 2) : null;

    private int _playheadBar = -1;
    private double _playheadFraction;

    private void LayoutPlayhead()
    {
        if (_playheadBar < 0 || _timeline.Project is null)
        {
            _playheadLine.Visibility = Visibility.Collapsed;
            _selectedTrackPlayMarker.Visibility = Visibility.Collapsed;
            _sectionHighlight.Visibility = Visibility.Collapsed;
            _barMarker.Visibility = Visibility.Collapsed;
            _barMarkerKey = default;
            return;
        }
        LayoutBarMarker();
        if (!PlayheadStyles.ShowsLine(_playheadStyle))
        {
            _playheadLine.Visibility = Visibility.Collapsed;
            _selectedTrackPlayMarker.Visibility = Visibility.Collapsed;
            return;
        }
        var x = _timeline.XOfBar(_playheadBar) + _timeline.BarWidthOf(_playheadBar) * Math.Clamp(_playheadFraction, 0, 1);
        _playheadLine.Visibility = Visibility.Visible;
        Canvas.SetLeft(_playheadLine, x);
        Canvas.SetTop(_playheadLine, 0);
        _playheadLine.Height = Math.Max(10, _timeline.ActualHeight);
        if (_timeline.SelectedTrack >= 0 && _timeline.SelectedTrack < (_project?.Tracks.Count ?? 0))
        {
            var trackTop = RulerHeight + SectionHeight + RowTopOf(_project, _timeline.SelectedTrack) - _timeline.VerticalScrollOffset;
            _selectedTrackPlayMarker.Visibility = Visibility.Visible;
            Canvas.SetLeft(_selectedTrackPlayMarker, x);
            Canvas.SetTop(_selectedTrackPlayMarker, trackTop + (TrackRowHeight - 16) / 2);
        }
        else _selectedTrackPlayMarker.Visibility = Visibility.Collapsed;
    }

    private void LayoutDragLaneOutline()
    {
        var from = _timeline.DragTrackFrom;
        if (from < 0 || _project is null || from >= _project.Tracks.Count)
        {
            _dragLaneOutline.Visibility = Visibility.Collapsed;
            return;
        }

        _dragLaneOutline.Visibility = Visibility.Visible;
        _dragLaneOutline.Width = Math.Max(_timelineHost.Width, _timeline.TotalWidth);
        _dragLaneOutline.Height = RowHeightOf(_project, _project.Tracks[from]) - 2;
        // Moved by a render transform, not Canvas.Top, so following the pointer never triggers layout.
        Canvas.SetLeft(_dragLaneOutline, 0);
        Canvas.SetTop(_dragLaneOutline, 0);
        if (_dragLaneOutline.RenderTransform is not TranslateTransform outlineShift || outlineShift.IsFrozen)
            _dragLaneOutline.RenderTransform = outlineShift = new TranslateTransform();
        outlineShift.Y = RulerHeight + SectionHeight + RowTopOf(_project, from) - _timeline.VerticalScrollOffset + _timeline.DragTrackDeltaY + 1;
    }

    private static Geometry CreatePlayMarkerGeometry()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), true, true);
            context.LineTo(new Point(14, 8), true, false);
            context.LineTo(new Point(0, 16), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    public void SetSelectedBar(int bar)
    {
        _timeline.SelectedBar = bar;
        _timeline.InvalidateVisual();
    }

    /// <summary>Highlights the bars currently selected in the score without changing the loop range.</summary>
    public void SetScoreSelection((int Start, int End)? range)
    {
        var barCount = _project?.Tracks.Select(track => track.Measures.Count).DefaultIfEmpty(0).Max() ?? 0;
        if (range is { } selected && barCount > 0)
        {
            var start = Math.Clamp(Math.Min(selected.Start, selected.End), 0, barCount - 1);
            var end = Math.Clamp(Math.Max(selected.Start, selected.End), start, barCount - 1);
            _timeline.ScoreSelectionStart = start;
            _timeline.ScoreSelectionEnd = end;
        }
        else
        {
            _timeline.ScoreSelectionStart = -1;
            _timeline.ScoreSelectionEnd = -1;
        }
        _timeline.RefreshOverlay();
    }

    public void SetSelectedTrack(int index)
    {
        _timeline.SelectedTrack = index;
        _timeline.InvalidateVisual();
        _selectedTrackIndex = index;
        LayoutPlayhead();
        UpdateDragVisual();
    }

    public void SetLoopRange(int start, int end)
    {
        _timeline.LoopStart = start;
        _timeline.LoopEnd = end;
        _timeline.RefreshOverlay();
    }

    /// <summary>The user dropped a moved area: insert-before bar, or -1 when cancelled.</summary>
    public event EventHandler<int>? AreaMoveDropped;

    public void BeginAreaMove(int start, int end)
    {
        var x = _timeline.BarX(start);
        _areaMoveOutline.Width = Math.Max(4, _timeline.BarX(end + 1) - x);
        _areaMoveOutline.Height = Math.Max(10, _timeline.ActualHeight - RulerHeight);
        Canvas.SetLeft(_areaMoveOutline, x);
        Canvas.SetTop(_areaMoveOutline, RulerHeight);
        _areaMoveOutline.Visibility = Visibility.Visible;
        _areaMoveOutline.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.25,
            TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        _timeline.AreaMove.Begin(start, end);
    }

    private void EndAreaMoveVisual(int target)
    {
        _areaMoveOutline.BeginAnimation(OpacityProperty, null);
        _areaMoveOutline.Visibility = Visibility.Collapsed;
        AreaMoveDropped?.Invoke(this, target);
    }

    public void SetAreaVisible(bool visible)
    {
        if (_timeline.AreaVisible == visible) return;
        _timeline.AreaVisible = visible;
        _timeline.RefreshOverlay();
    }

    public void SetSkipRanges(IReadOnlyList<(int Start, int End)> ranges)
    {
        if (_timeline.SkipRanges.SequenceEqual(ranges)) return;
        _timeline.SkipRanges = ranges.ToArray();
        _timeline.RefreshOverlay();
    }

    public void SetLoopEnabled(bool enabled)
    {
        if (_timeline.LoopEnabled == enabled) return;
        _timeline.LoopEnabled = enabled;
        _timeline.RefreshOverlay();
    }

    private void FollowPlayhead()
    {
        if (_playheadBar < 0) return;
        var x = _timeline.XOfBar(_playheadBar) +
                _timeline.BarWidthOf(_playheadBar) * Math.Clamp(_playheadFraction, 0, 1);
        var viewport = _horizontal.ViewportWidth;
        if (viewport <= 1) return;
        var offset = _horizontal.HorizontalOffset;
        var barCount = _project is null || _project.Tracks.Count == 0 ? 0 : _project.Tracks.Max(t => t.Measures.Count);
        var endBar = Math.Min(barCount, _playheadBar + 3);
        var threeBarMargin = endBar > _playheadBar
            ? _timeline.XOfBar(endBar) - _timeline.XOfBar(_playheadBar)
            : MeasureWidth * 3;
        if (ArrangementFollowGeometry.ShouldAdvance(x, offset, viewport, threeBarMargin))
        {
            // Advance by half a page so the playhead returns to the middle portion of the view,
            // leaving several bars of look-ahead visible.
            var nextOffset = ArrangementFollowGeometry.AdvanceOffset(offset, viewport, _horizontal.ScrollableWidth);
            if (nextOffset > offset + 1) _horizontal.ScrollToHorizontalOffset(nextOffset);
        }
        else if (x < offset + 40)
        {
            // Seeking backwards should bring the playhead back into view too.
            _horizontal.ScrollToHorizontalOffset(ArrangementFollowGeometry.OffsetForBackwardSeek(x, viewport));
        }
    }

    private void RefreshTimelineExtent()
    {
        _timeline.UpdateCache();   // the song's width decides whether the timeline can be one GPU texture
        var width = Math.Max(200, _timeline.TotalWidth);
        if (_horizontal.ActualWidth > 0) width = Math.Max(width, _horizontal.ActualWidth);
        var fill = AddLaneFillHeight;
        _timeline.AddLaneFill = fill;
        if (_addLane.Row is { } laneRow && Math.Abs(laneRow.Height - fill) > 0.01) laneRow.Height = fill;
        var height = RulerHeight + SectionHeight + RowsHeight(_project) + fill + 2;
        _timelineHost.Width = width;
        _timelineHost.Height = height;
        // Keep the canvas, rendered timeline, and overlay coordinate spaces identical. In particular,
        // don't let the timeline re-measure to the viewport width while the overlays use content width.
        _timeline.Width = width;
        _timeline.Height = height;
        _sectionDragOverlay.Width = width;
        _sectionDragOverlay.Height = height;
        _sectionInsertionIndicator.Width = width;
        _sectionInsertionIndicator.Height = height;
        _dropGhost.Width = width;
        _dropGhost.Height = height;
        _timelineHost.InvalidateMeasure();
        SyncTimelineScrollBar();
        LayoutDragLaneOutline();
    }

    /// <summary>Clears the playhead overlay (stop / end of playback).</summary>
    public void ClearPlayhead()
    {
        _playheadBar = -1;
        LayoutPlayhead();
    }
}
