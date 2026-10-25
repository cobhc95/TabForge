using System;
using Avalonia;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: dragging a section's edge knob on the ruler: the boundary follows the pointer bar by bar, clamped at the neighbour (a gap may open when shrinking, one bar at least), shown as a preview
//   (TimelineView.SectionResizePreview, one repaint per bar change) and written once on release as one undo step. Only the section's start or length changes; no bars or notes move. The pointer turns to the
//   resize arrow only over a knob.
// Does not own: the rule (SectionLayout.EdgeRange/ResizeClamped), the knob drawing (SectionKnobs) or the section move drag (TimelineInputController.Drag).
// Tests: TabForge.Avalonia.Tests.SectionResizeTests.
public sealed partial class TimelineInputController
{
    private int _resizeSection;
    private bool _resizeRight;
    private (int Min, int Max) _resizeRange;

    // A press on a knob starts the resize at once (no drag threshold: the knobs are tiny and have no click meaning). False when the press is not on a knob.
    private bool TryBeginSectionResize(Point p, IPointer pointer)
    {
        if (_view.SectionKnobAt(p.X, p.Y) is not var (index, right) || _view.SectionMarkerAt(index) is not { } marker) return false;
        if (SectionLayout.EdgeRange(_document.Project, marker, right) is not var (min, max)) return false;
        _resizeSection = index;
        _resizeRight = right;
        _resizeRange = (min, max);
        _gesture = Gesture.SectionResize;
        _pressOnGrid = _pressOnLanes = _pressOnRuler = false;
        _dragSlot = -1;
        _pressRow = -1;
        pointer.Capture(_view);
        SetHover(-1, -1);
        return true;
    }

    // The bar line nearest the pointer, kept inside the allowed range.
    private int ResizeBoundaryAt(double x)
    {
        var bar = Math.Clamp(_view.BarAtX(x), 0, Math.Max(0, _view.BarCount - 1));
        var boundary = x - _view.BarX(bar) > (_view.BarX(bar + 1) - _view.BarX(bar)) / 2 ? bar + 1 : bar;
        return Math.Clamp(boundary, _resizeRange.Min, _resizeRange.Max);
    }

    private void UpdateSectionResize(Point p)
    {
        if (_view.SectionMarkerAt(_resizeSection) is not { } marker) return;
        var boundary = ResizeBoundaryAt(p.X);
        var start = Math.Clamp(marker.MeasureIndex, 0, _view.BarCount);
        var end = _view.SectionEndBar(_resizeSection);
        var preview = _resizeRight ? (_resizeSection, start, boundary) : (_resizeSection, boundary, end);
        if (_view.SectionResizePreview == preview) return;
        _view.SectionResizePreview = preview;
        _view.InvalidateVisual();
    }

    // The release writes the previewed edge as one undo step; a release on the starting bar changes nothing and records nothing.
    private void CommitSectionResize()
    {
        var preview = _view.SectionResizePreview;
        _view.SectionResizePreview = null;
        if (preview is not { } shown) { Repaint(); return; }
        var index = shown.Index;
        var boundary = _resizeRight ? shown.End : shown.Start;
        var changed = DocumentEdits.Run(_document, song =>
        {
            var sorted = SectionLayout.Sorted(song);
            return index < sorted.Count && SectionLayout.ResizeClamped(song, sorted[index], _resizeRight, boundary);
        }, label: "Resize section").Changed;
        if (changed)
        {
            _view.Reload(_document.Project);
            _header?.Reload(_document.Project);
            SongEdited?.Invoke();
        }
        Repaint();
    }
}
