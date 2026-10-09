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

// TrackTimeline: interaction (section hits, edge resizing, section drag preview, tooltips, mouse).
internal sealed partial class TrackTimeline : ISectionTipHost, ISectionEdgeHost
{
    // ---------- interaction ----------

    public int BarAt(double x) => Project is null ? 0 : EnsureTimelineGeometry().BarAt(x);

    internal readonly record struct SectionHit(MarkerModel Marker, int MarkerIndex, int FirstBar,
        Rect Bounds, Color Color, string Title, bool Locked);
    private static readonly List<SectionHit> EmptySectionHits = new();

    public List<SectionHit> SectionHits()
    {
        var project = Project;
        var bars = BarCount;
        if (project is null || bars <= 0 || project.Markers.Count == 0) return EmptySectionHits;

        if (_sectionHitsCache is not null && _sectionHitsCache.Count == project.Markers.Count)
            return _sectionHitsCache;

        var markers = project.Markers.OrderBy(marker => marker.MeasureIndex).ToList();
        var result = new List<SectionHit>(markers.Count);
        var geometry = EnsureTimelineGeometry();
        // Similar section names share their family's colour. Automatic colours stay distinct;
        // an explicit editor choice is preserved. The section list uses the same resolver.
        var colours = SectionColours.Resolve(markers, MatchSimilarSectionColours, _theme.Accent);
        Color ColourOf(MarkerModel marker) => colours[marker];
        for (var index = 0; index < markers.Count; index++)
        {
            var start = Math.Clamp(markers[index].MeasureIndex, 0, bars);
            // A resized section can stop early, leaving empty (unsectioned) bars before the next one.
            var end = SectionLayout.End(markers, index, bars);
            if (end <= start) continue;

            var x = geometry.XOfBar(start);
            var width = geometry.XOfBar(end) - x - 2;
            var bounds = new Rect(x + 1, ArrangementPanel.RulerHeight + 1,
                Math.Max(2, width), ArrangementPanel.SectionHeight - 4);
            result.Add(new SectionHit(markers[index], index, start, bounds,
                ColourOf(markers[index]), markers[index].Title, markers[index].LockPosition));
        }
        _sectionHitsCache = result;
        return result;
    }

    public void InvalidateSectionHits() { _sectionHitsCache = null; _dragLanes.Clear(); _hoverSectionIndex = -1; SectionTip.Close(); }

    // ---- section edge resizing ----
    public event EventHandler? SectionResizeStarting;
    public event EventHandler? SectionResized;
    /// <summary>Plain section drop: move only the marker (the bars stay); (marker ordinal, bar).</summary>
    public event EventHandler<(int markerIndex, int bar)>? SectionMarkerMoved;
    /// <summary>Right-click on the section lane: (section ordinal or -1 for empty lane, bar).</summary>
    public event EventHandler<(int markerIndex, int bar)>? SectionLaneContextRequested;
    /// <summary>Section edge resize and Ctrl+drag marker-only move: SectionEdgeController.</summary>
    internal SectionEdgeController SectionEdges => _sectionEdges ??= new SectionEdgeController(this);
    private SectionEdgeController? _sectionEdges;

    private static int ActiveSectionIndex(IReadOnlyList<SectionHit> sections, int playheadBar)
    {
        if (playheadBar < 0) return -1;
        var low = 0;
        var high = sections.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (sections[middle].FirstBar <= playheadBar) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    internal (int StartBar, int EndBar, Color Color)? SectionGeometryAtBar(int bar)
    {
        var sections = SectionHits();
        var index = ActiveSectionIndex(sections, bar);
        if (index < 0) return null;
        var section = sections[index];
        var end = index + 1 < sections.Count ? sections[index + 1].FirstBar : BarCount;
        return (section.FirstBar, end, section.Color);
    }

    private Rect SectionPreviewBounds(SectionHit section)
    {
        if ((_sectionDragging || _sectionSettling) &&
            _sectionPreviewPositions.TryGetValue(section.Marker, out var x))
            return new Rect(x, section.Bounds.Y, section.Bounds.Width, section.Bounds.Height);
        return section.Bounds;
    }

    private void DrawSectionBlock(DrawingContext dc, SectionHit section, Rect rect, bool isDragged, int activeSectionIndex)
    {
        var color = section.Color;
        var hovered = false;   // hover feedback is drawn in the overlay layer (DrawSectionHover)
        var pressed = section.MarkerIndex == _pressedSectionIndex || isDragged;
        var active = section.MarkerIndex == activeSectionIndex;
        if (isDragged)
        {
            dc.DrawRoundedRectangle(Draw.Solid(Colors.Black, 0.34), null,
                new Rect(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), 3, 3);
        }
        dc.DrawRoundedRectangle(Draw.Solid(color, 0.68), Draw.Pen(color, isDragged ? 1.4 : 1), rect, 3, 3);
        if (hovered || pressed || active)
        {
            var intensity = SectionGlowIntensity * (pressed ? 1 : active && hovered ? 0.78 : active ? 0.60 : 0.42);
            dc.DrawRoundedRectangle(null, Draw.Pen(color, 7, intensity * 0.18),
                new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), 4, 4);
            dc.DrawRoundedRectangle(null, Draw.Pen(color, isDragged ? 2.4 : 2, intensity), rect, 3, 3);
        }
        if (hovered || pressed)
        {
            dc.DrawRoundedRectangle(Draw.Solid(Colors.White, pressed ? 0.14 : 0.08), null, rect, 3, 3);
            dc.DrawRoundedRectangle(null, Draw.Pen(Colors.White, pressed ? 1.2 : 0.8,
                pressed ? 0.42 : 0.24), rect, 3, 3);
        }
        if (ShowSectionNames && rect.Width > 34)
        {
            var title = section.Locked ? "🔒 " + section.Title : section.Title;
            var textBrush = Draw.Solid(Colors.White);
            var ft = Draw.Text(title, 11, textBrush, true);
            var maxTextWidth = Math.Max(0, rect.Width - 10);
            while (ft.Width > maxTextWidth && title.Length > 2)
            {
                title = title[..^2].TrimEnd() + "…";
                ft = Draw.Text(title, 11, textBrush, true);
            }
            dc.PushClip(new RectangleGeometry(rect));
            if (ft.Width <= maxTextWidth)
                Draw.DrawText(dc, ft, new Point(rect.X + 5, rect.Y + 2));
            dc.Pop();
        }
    }

    private void DrawSectionHover(DrawingContext dc)
    {
        if (_hoverSectionIndex < 0 || _sectionDragging || _sectionSettling) return;
        foreach (var section in SectionHits())
        {
            if (section.MarkerIndex != _hoverSectionIndex) continue;
            var rect = section.Bounds;
            var color = section.Color;
            var intensity = SectionGlowIntensity * 0.42;
            dc.DrawRoundedRectangle(null, Draw.Pen(color, 7, intensity * 0.18), new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), 4, 4);
            dc.DrawRoundedRectangle(null, Draw.Pen(color, 2, intensity), rect, 3, 3);
            dc.DrawRoundedRectangle(Draw.Solid(Colors.White, 0.08), null, rect, 3, 3);
            dc.DrawRoundedRectangle(null, Draw.Pen(Colors.White, 0.8, 0.24), rect, 3, 3);
            return;
        }
    }

    private void UpdateSectionPreviewTargets(IReadOnlyList<SectionHit> hits, int insertBefore)
    {
        if (_sectionDragMarker is null || _sectionPressOriginIndex < 0 || _sectionPressOriginIndex >= hits.Count)
            return;

        var sourceIndex = _sectionPressOriginIndex;
        var adjustedTarget = Math.Clamp(insertBefore > sourceIndex ? insertBefore - 1 : insertBefore, 0, hits.Count - 1);
        var x = hits[0].Bounds.X;
        for (var destination = 0; destination < hits.Count; destination++)
        {
            var source = destination == adjustedTarget
                ? sourceIndex
                : sourceIndex < adjustedTarget && destination >= sourceIndex && destination < adjustedTarget
                    ? destination + 1
                    : sourceIndex > adjustedTarget && destination > adjustedTarget && destination <= sourceIndex
                        ? destination - 1
                        : destination;
            var section = hits[source];
            _sectionPreviewTargets[section.Marker] = x;
            // The expensive timeline surface is redrawn only when a valid boundary is crossed.
            // Pointer motion itself is handled by the lightweight drag overlay below.
            _sectionPreviewPositions[section.Marker] = x;
            x += section.Bounds.Width + 2;
        }
        InvalidateVisual();
    }

    private void StartSectionPreviewAnimation()
    {
        if (_renderingSectionPreview) return;
        if (!AnimateSectionDragging || UiMotion.DurationMilliseconds(1) <= 0)
        {
            ClearSectionPreview();
            InvalidateVisual();
            return;
        }
        CompositionTarget.Rendering += AdvanceSectionPreview;
        _renderingSectionPreview = true;
        _lastSectionPreviewFrame = TimeSpan.Zero;
    }

    private void AdvanceSectionPreview(object? sender, EventArgs e)
    {
#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.Drag);
#endif
        var hits = _sectionPreviewAnimationHits;
        if (hits is null || hits.Length == 0)
        {
            ClearSectionPreview();
            return;
        }

        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        var elapsed = _lastSectionPreviewFrame == TimeSpan.Zero
            ? 1.0 / 60.0
            : Math.Clamp((now - _lastSectionPreviewFrame).TotalSeconds, 0, 0.05);
        _lastSectionPreviewFrame = now;
        var smoothing = 1 - Math.Exp(-elapsed / 0.035);
        var settled = true;
        foreach (var section in hits)
        {
            var current = _sectionPreviewPositions.TryGetValue(section.Marker, out var position)
                ? position : section.Bounds.X;
            if (_sectionDragging && ReferenceEquals(section.Marker, _sectionDragMarker))
            {
                _sectionPreviewPositions[section.Marker] = _sectionDragX;
                continue;
            }

            var target = _sectionSettling
                ? section.Bounds.X
                : _sectionPreviewTargets.TryGetValue(section.Marker, out var previewTarget)
                    ? previewTarget : section.Bounds.X;
            var difference = target - current;
            if (Math.Abs(difference) > 0.25)
            {
                _sectionPreviewPositions[section.Marker] = current + difference * smoothing;
                settled = false;
            }
            else
            {
                _sectionPreviewPositions[section.Marker] = target;
            }
        }

        if (_sectionSettling && settled)
        {
            ClearSectionPreview();
            return;
        }
        if (_sectionSettling) InvalidateVisual();
    }

    private void PublishSectionDragPreview(IReadOnlyList<SectionHit> hits)
    {
        if (!_sectionDragging || _sectionDragSnapshot is null || _sectionDragStartBar < 0 ||
            _sectionDragEndBar <= _sectionDragStartBar)
            return;
        var insertionBar = _sectionDropBefore >= 0 && _sectionDropBefore < hits.Count
            ? hits[_sectionDropBefore].FirstBar : BarCount;
        SectionDragPreviewChanged?.Invoke(this, new SectionDragPreview(_sectionDragSnapshot,
            _sectionDragX, _sectionDragSnapshot.Geometry.XOfBar(insertionBar),
            _sectionDragX + _sectionDragGrabOffset));
    }

    public void UpdateSectionDragPointer(double pointerX)
    {
#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.Drag);
#endif
        if (!_sectionDragging) return;
        var hits = _sectionDragHitsSnapshot ?? SectionHits().ToArray();
        var insertion = hits.Length;
        foreach (var hit in hits)
        {
            if (pointerX < hit.Bounds.X + hit.Bounds.Width / 2) { insertion = hit.MarkerIndex; break; }
        }
        _sectionDropBefore = insertion;
        _sectionDragX = pointerX - _sectionDragGrabOffset;
        if (_sectionDragMarker is not null) _sectionPreviewPositions[_sectionDragMarker] = _sectionDragX;
        if (insertion != _lastSectionTarget)
        {
            _lastSectionTarget = insertion;
            UpdateSectionPreviewTargets(hits, insertion);
        }
        PublishSectionDragPreview(hits);
    }

    private void SettleSectionPreview()
    {
        _sectionDragging = false;
        _sectionSettling = true;
        _dragLanes.Clear(); // bars moved on drop: re-record lanes once for the settle animation
        _sectionDropBefore = -1;
        var hits = SectionHits();
        _sectionPreviewAnimationHits = hits.ToArray();
        foreach (var section in hits)
        {
            if (!_sectionPreviewPositions.ContainsKey(section.Marker))
                _sectionPreviewPositions[section.Marker] = section.Bounds.X;
            _sectionPreviewTargets[section.Marker] = section.Bounds.X;
        }
        StartSectionPreviewAnimation();
        InvalidateVisual();
    }

    private void ClearSectionPreview()
    {
        if (_renderingSectionPreview) CompositionTarget.Rendering -= AdvanceSectionPreview;
        _renderingSectionPreview = false;
        _sectionSettling = false;
        _sectionDragging = false;
        _sectionDragMarker = null;
        _sectionDragSnapshot = null;
        _sectionDragHitsSnapshot = null;
        _sectionPreviewAnimationHits = null;
        _sectionDragGrabOffset = 0;
        _sectionDragX = 0;
        _lastSectionPreviewFrame = TimeSpan.Zero;
        if (DragTrackFrom < 0) EndLaneAnimation();
        _sectionPreviewPositions.Clear();
        _sectionPreviewTargets.Clear();
        InvalidateVisual();
    }

    private SectionHit? SectionAt(Point point)
    {
        foreach (var section in SectionHits())
            if (section.Bounds.Contains(point)) return section;
        return null;
    }

    /// <summary>Section header hit-test, shared with the section drawing geometry.</summary>
    internal int SectionStartBarAt(double x, double y)
        => SectionAt(new Point(x, y))?.FirstBar ?? -1;

    /// <summary>Returns the sorted marker index for a section header hit, or -1 outside section blocks.</summary>
    internal int SectionMarkerAt(double x, double y)
        => SectionAt(new Point(x, y))?.MarkerIndex ?? -1;

    // Ghost of the dragged section tab plus the bar it will start on (plain drag only).
    private void DrawMarkerDragGhost(DrawingContext dc)
    {
        var edges = SectionEdges;
        if (!edges.MarkerDragging || edges.MarkerHit.Marker is null) return;
        var bounds = edges.MarkerHit.Bounds;
        var ghost = new Rect(edges.MarkerX, bounds.Y, bounds.Width, bounds.Height);
        dc.PushOpacity(0.78);
        DrawSectionBlock(dc, edges.MarkerHit, ghost, true, -1);
        dc.Pop();
        if (edges.MarkerTargetBar >= 0)
        {
            var x = Math.Round(XOfBar(edges.MarkerTargetBar)) + 0.5;
            dc.DrawLine(Draw.Pen(_theme.Accent, 2), new Point(x, ArrangementPanel.RulerHeight), new Point(x, ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight));
        }
    }

    // The section lane's hover hint: SectionTipController.
    private SectionTipController SectionTip => _sectionTipController ??= new SectionTipController(this, this);
    private SectionTipController? _sectionTipController;
    bool ISectionTipHost.SectionGestureActive => _dragging || SectionEdges.MarkerDragging || _sectionDragging;
    int ISectionTipHost.HoverSectionIndex => _hoverSectionIndex;

    private void UpdateSectionHover(int sectionIndex)
    {
        if (_hoverSectionIndex != sectionIndex)
        {
            _hoverSectionIndex = sectionIndex;
            SectionTip.Schedule(sectionIndex);
            RefreshOverlay();
        }
        var hits = SectionHits();
        var hit = sectionIndex >= 0 && sectionIndex < hits.Count && hits[sectionIndex].MarkerIndex == sectionIndex
            ? hits[sectionIndex] : default;
        Cursor = sectionIndex >= 0 && hit.Marker is not null && hit.Locked
            ? Cursors.No
            : sectionIndex >= 0 ? Cursors.Hand : Cursors.Arrow;
    }

    public int TrackAt(double y)
    {
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        if (Project is null || y < gridTop) return -1;
        return ArrangementPanel.RowIndexAt(Project, y + VerticalScrollOffset - gridTop);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        SectionTip.Close();
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        if (IsInAddLane(p)) { e.Handled = true; Dispatcher.BeginInvoke(new Action(RaiseAddLaneClicked)); return; }   // the Add-track lane: not a bar, a track or a clip
        if (!AreaMove.Active && ClipGestures.MouseDown(e, p)) return;
        if (!AreaMove.Active && SectionEdges.EdgeAt(p) is { } edge)
        {
            SectionEdges.BeginResize(edge.Marker, edge.Right);
            SectionResizeStarting?.Invoke(this, EventArgs.Empty);
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (AreaMove.Active) { AreaMove.Finish(AreaMove.TargetAt(p.X)); e.Handled = true; return; }
        var section = SectionAt(p);
        _sectionPressOriginIndex = section?.MarkerIndex ?? -1;
        _pressedSectionIndex = _sectionPressOriginIndex;
        UpdateSectionHover(_sectionPressOriginIndex);
        InvalidateVisual();
        var bar = section?.FirstBar ?? BarAt(p.X);
        _dragStartBar = bar;
        _dragStart = p;
        _dragStartTicks = Environment.TickCount64;
        _dragMode = 0;
        _dragging = true;
        CaptureMouse();
        if (section is null)
        {
            BarClicked?.Invoke(this, bar);
            var track = TrackAt(p.Y);
            if (track >= 0) TrackClicked?.Invoke(this, track);
            _dragTrackFrom = track;
            _dragTrackTo = track;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        UpdateHover(p);
        var inLane = !_dragging && SectionEdges.ResizeMarker is null && IsInAddLane(p);
        NotifyAddLaneHot(inLane);
        if (inLane) { Cursor = Cursors.Hand; return; }
        if (!_dragging && SectionEdges.ResizeMarker is null && ClipGestures.MouseMove(e, p)) return;
        if (SectionEdges.ResizeMarker is not null)
        {
            SectionEdges.ResizeMove(p.X);
            return;
        }
        var edgeUnderPointer = SectionEdges.EdgeAt(p);
        Cursor = edgeUnderPointer is not null ? Cursors.SizeWE : null;
        if (edgeUnderPointer is not null && !_dragging)
        {
            UpdateSectionHover(SectionAt(p)?.MarkerIndex ?? -1);
            Cursor = Cursors.SizeWE;
            return;
        }
        if (AreaMove.Active)
        {
            // Only repaint when the drop position changes bar boundary.
            AreaMove.PointerMoved(p.X);
            return;
        }
        if (_sectionDragging)
        {
            UpdateSectionDragPointer(p.X);
            return;
        }
        if (SectionEdges.MarkerDragging)
        {
            SectionEdges.UpdateMarkerDrag(p.X);
            return;
        }
        var section = SectionAt(p);
        UpdateSectionHover(section?.MarkerIndex ?? -1);
        if (_dragging && e.LeftButton == MouseButtonState.Pressed && _sectionPressOriginIndex >= 0)
        {
            var hits = SectionHits();
            if (!_sectionDragging && _sectionPressOriginIndex < hits.Count &&
                !hits[_sectionPressOriginIndex].Locked &&
                Math.Abs(p.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance)
            {
                if (_sectionPressOriginIndex >= hits.Count) return;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    SectionEdges.BeginMarkerDrag(hits[_sectionPressOriginIndex], _dragStart.X, p.X);
                    return;
                }
                BeginSectionDrag(hits, p);
            }
            if (_sectionDragging)
            {
                return;
            }
            return; // A section block is a button until a deliberate horizontal drag starts.
        }
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        if (_dragMode == 0)
        {
            // Decide the gesture by the dominant axis: sideways scrubs a bar range, up/down reorders.
            var dx = Math.Abs(p.X - _dragStart.X);
            var dy = Math.Abs(p.Y - _dragStart.Y);
            if (dy > 6 && dy > dx) _dragMode = 2;
            // A 6-DIP threshold made tiny mouse jitter near a bar boundary turn a click into
            // a multi-bar range drag. Require a deliberate horizontal movement before arming it.
            else if (dx >= Math.Max(12, SystemParameters.MinimumHorizontalDragDistance * 2) && e.LeftButton == MouseButtonState.Pressed &&
                     Environment.TickCount64 - _dragStartTicks >= RangeDragHoldMs) _dragMode = 1;
            else return;
        }

        if (_dragMode == 1)
        {
            var bar = BarAt(p.X);
            if (bar != _dragStartBar)
            {
                var start = Math.Min(_dragStartBar, bar);
                var end = Math.Max(_dragStartBar, bar);
                RangeDragged?.Invoke(this, (start, end));
            }
        }
        else if (_dragMode == 2 && _dragTrackFrom >= 0)
        {
            var t = TrackAtClamped(p.Y);
            if (t != _dragTrackTo)
            {
                _dragTrackTo = t;
            }
            SetDragPreview(_dragTrackFrom, _dragTrackTo, p.Y - _dragStart.Y);
            TrackDragPreviewChanged?.Invoke(this, (_dragTrackFrom, _dragTrackTo, p.Y - _dragStart.Y, true));
        }
    }

    /// <summary>Starts moving the pressed section: the preview positions, the snapshot for the drag overlay and the started event.</summary>
    private void BeginSectionDrag(IReadOnlyList<SectionHit> hits, Point p)
    {
        _sectionDragMarker = hits[_sectionPressOriginIndex].Marker;
        _sectionDragGrabOffset = _dragStart.X - hits[_sectionPressOriginIndex].Bounds.X;
        _sectionDragX = p.X - _sectionDragGrabOffset;
        _sectionDragStartBar = hits[_sectionPressOriginIndex].FirstBar;
        _sectionDragEndBar = _sectionPressOriginIndex + 1 < hits.Count
            ? hits[_sectionPressOriginIndex + 1].FirstBar : BarCount;
        _lastSectionTarget = -1;
        _sectionPreviewPositions.Clear();
        _sectionPreviewTargets.Clear();
        foreach (var hit in hits)
        {
            _sectionPreviewPositions[hit.Marker] = hit.Bounds.X;
            _sectionPreviewTargets[hit.Marker] = hit.Bounds.X;
        }
        _sectionDragHitsSnapshot = hits.ToArray();
        _sectionDragSnapshot = new SectionDragSnapshot(Project!, EnsureTimelineGeometry(),
            _sectionDragStartBar, _sectionDragEndBar, VerticalScrollOffset,
            hits[_sectionPressOriginIndex].Color, hits[_sectionPressOriginIndex].Title);
        _sectionDragging = true;
        BeginLaneAnimation();
        SectionDragStarted?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        PublishSectionDragPreview(hits);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (ClipGestures.MouseUp()) { e.Handled = true; return; }
        if (SectionEdges.ResizeMarker is not null)
        {
            SectionEdges.EndResize();
            ReleaseMouseCapture();
            SectionResized?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        var mode = _dragMode;
        var pressed = _dragging;
        var from = _dragTrackFrom;
        var to = _dragTrackTo;
        var sectionFrom = _sectionPressOriginIndex;
        var sectionDrop = _sectionDropBefore;
        if (SectionEdges.MarkerDragging)
        {
            var targetBar = SectionEdges.MarkerTargetBar;
            var ordinal = SectionEdges.MarkerHit.MarkerIndex;
            SectionEdges.EndMarkerDrag();
            _dragging = false;
            _pressedSectionIndex = -1;
            _sectionPressOriginIndex = -1;
            ReleaseMouseCapture();
            if (ordinal >= 0 && targetBar >= 0) SectionMarkerMoved?.Invoke(this, (ordinal, targetBar));
            return;
        }
        var sectionDragged = _sectionDragging;
        var sectionMarker = _sectionDragMarker;
        var clickedSection = SectionAt(e.GetPosition(this));
        _dragging = false;
        _sectionDragging = false;
        ReleaseMouseCapture();
        _dragMode = 0;
        _dragTrackFrom = -1;
        _dragTrackTo = -1;
        _pressedSectionIndex = -1;
        _sectionPressOriginIndex = -1;
        _sectionDropBefore = -1;
        UpdateSectionHover(SectionAt(e.GetPosition(this))?.MarkerIndex ?? -1);
        InvalidateVisual();
        SetDragPreview(-1, -1, 0);
        if (mode == 2)
        {
            TrackDragPreviewChanged?.Invoke(this, (from, to, 0, false));
            if (from >= 0 && to >= 0 && to != from) TrackReordered?.Invoke(this, (from, to));
        }
        else if (sectionDragged)
        {
            _sectionHitsCache = null;
            // A plain drag moves the section together with its bars.
            if (sectionFrom >= 0 && sectionDrop >= 0) SectionReordered?.Invoke(this, (sectionFrom, sectionDrop));
            if (sectionMarker is not null)
                SettleSectionPreview();
            SectionDragPreviewChanged?.Invoke(this, null);
            _sectionHitsCache = null;
        }
        else if (sectionFrom >= 0)
        {
            if (clickedSection is { } clicked)
                BarClicked?.Invoke(this, clicked.FirstBar);
            PlainClicked?.Invoke(this, EventArgs.Empty);
        }
        // Mode 0 = the press never passed the drag threshold (the range-drag threshold above is kept):
        // like a click on empty score paper, it clears the selected range.
        else if (pressed && mode == 0) PlainClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        ClipGestures.CaptureLost();
        if (SectionEdges.MarkerDragging) { SectionEdges.EndMarkerDrag(); _dragging = false; _sectionPressOriginIndex = -1; return; }
        if (!_sectionDragging) return;
        _dragging = false;
        _pressedSectionIndex = -1;
        _sectionPressOriginIndex = -1;
        SettleSectionPreview();
        SectionDragPreviewChanged?.Invoke(this, null);
        SectionDragCancelled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        NotifyAddLaneHot(false);
        ClearHover();
        if (!_dragging) UpdateSectionHover(-1);
    }

    private int TrackAtClamped(double y)
    {
        if (Project is null || Project.Tracks.Count == 0) return -1;
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var offset = y + VerticalScrollOffset - gridTop;
        if (offset < 0) return 0;
        var index = ArrangementPanel.RowIndexAt(Project, offset);
        return index < 0 ? Project.Tracks.Count - 1 : index;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var p = e.GetPosition(this);
        if (AreaMove.Active) { AreaMove.Finish(-1); e.Handled = true; return; }
        if (ClipGestures.RightClick(p)) { e.Handled = true; return; }
        var section = SectionAt(p);
        var onSectionLane = p.Y >= ArrangementPanel.RulerHeight && p.Y < ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        if (onSectionLane)
            SectionLaneContextRequested?.Invoke(this, (section?.MarkerIndex ?? -1, BarAt(p.X)));
        else if (section is { } hit)
            SectionContextRequested?.Invoke(this, hit.MarkerIndex);
        else
            ContextRequested?.Invoke(this, (BarAt(p.X), TrackAt(p.Y)));
        e.Handled = true;
    }
}
