using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What section edge resizing and marker-only dragging need from the timeline (implemented by <see cref="TrackTimeline"/>).</summary>
internal interface ISectionEdgeHost
{
    SongProject? Project { get; }
    List<TrackTimeline.SectionHit> SectionHits();
    int BarAt(double x);
    double XOfBar(int bar);
    void InvalidateSectionHits();
    void InvalidateVisual();
}

// Owns: the section edge resize state (marker, which edge, last snapped boundary) and the Ctrl+drag marker-only move state
// (dragged tab, grab offset, ghost x, target bar), with their snapping rules.
// Does not own: the section model rules (SectionLayout), drawing the ghost (TrackTimeline reads MarkerHit, MarkerX, MarkerTargetBar),
// the press and the events (TrackTimeline), the whole-section drag (TrackTimeline).
// Tests: TestClipAndSectionEdits, TestTimelineHoverAndBarMarker.
/// <summary>Section lane gestures that change a single marker: dragging a section edge, and Ctrl+drag of a tab into free bars.</summary>
internal sealed class SectionEdgeController
{
    private const double EdgeGrip = 6;
    private readonly ISectionEdgeHost _host;
    private bool _resizeRightEdge;
    private int _resizeLastBar = -1;
    private double _markerGrabOffset;

    public SectionEdgeController(ISectionEdgeHost host) => _host = host;

    /// <summary>The section whose edge is being dragged (null = no resize).</summary>
    internal MarkerModel? ResizeMarker { get; private set; }
    internal bool MarkerDragging { get; private set; }
    internal TrackTimeline.SectionHit MarkerHit { get; private set; }
    internal double MarkerX { get; private set; }
    internal int MarkerTargetBar { get; private set; } = -1;

    /// <summary>Section edge under the pointer: (marker, right edge?) or null.</summary>
    internal (MarkerModel Marker, bool Right)? EdgeAt(Point p)
    {
        if (p.Y < ArrangementPanel.RulerHeight || p.Y > ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight) return null;
        foreach (var hit in _host.SectionHits())
        {
            if (hit.Locked) continue;
            if (Math.Abs(p.X - hit.Bounds.Right) <= EdgeGrip) return (hit.Marker, true);
            if (Math.Abs(p.X - hit.Bounds.Left) <= EdgeGrip && hit.FirstBar > 0) return (hit.Marker, false);
        }
        return null;
    }

    internal void BeginResize(MarkerModel marker, bool rightEdge)
    {
        ResizeMarker = marker;
        _resizeRightEdge = rightEdge;
        _resizeLastBar = -1;
    }

    /// <summary>Snap to the nearest bar boundary; the model changes and the timeline repaints only when it changes.</summary>
    internal void ResizeMove(double x)
    {
        var bar = _host.BarAt(x);
        var boundary = x - _host.XOfBar(bar) > (_host.XOfBar(bar + 1) - _host.XOfBar(bar)) / 2 ? bar + 1 : bar;
        if (boundary == _resizeLastBar) return;
        _resizeLastBar = boundary;
        // Model rule lives in SectionLayout.ResizeEdge; the control only snaps and repaints.
        if (_host.Project is not { } project || ResizeMarker is null) return;
        SectionLayout.ResizeEdge(project, ResizeMarker, _resizeRightEdge, boundary);
        _host.InvalidateSectionHits();
        _host.InvalidateVisual();
    }

    internal void EndResize() => ResizeMarker = null;

    internal void BeginMarkerDrag(TrackTimeline.SectionHit hit, double pressX, double pointerX)
    {
        MarkerDragging = true;
        MarkerHit = hit;
        _markerGrabOffset = pressX - hit.Bounds.X;
        UpdateMarkerDrag(pointerX);
    }

    internal void UpdateMarkerDrag(double pointerX)
    {
        // The tab lands where its left edge is and snaps bar by bar, so the timeline repaints only when
        // the target bar changes (never per mouse move).
        // Only through free bars: the same range the move itself uses (no overlap, no space = no move).
        if (_host.Project is null || SectionLayout.MoveRange(_host.Project, MarkerHit.Marker) is not var (min, max)) return;
        var target = Math.Clamp(_host.BarAt(pointerX - _markerGrabOffset + 1), min, max);
        if (target == MarkerTargetBar) return;
        MarkerTargetBar = target;
        MarkerX = _host.XOfBar(target) + 1;
        _host.InvalidateVisual();
    }

    internal void EndMarkerDrag()
    {
        MarkerDragging = false;
        MarkerTargetBar = -1;
        _host.InvalidateVisual();
    }
}
