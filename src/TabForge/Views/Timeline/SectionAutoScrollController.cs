using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TabForge.Views;

// Owns: scrolling the timeline sideways while a section is dragged near the left or right edge of the view.
// Does not own: the section drag itself (TrackTimeline) or its overlay (SectionDragOverlay).
// Tests: TestSectionAutoScroll.
/// <summary>Follows the section-drag pointer: one scroll step per rendered frame while the pointer is within the edge zone.</summary>
internal sealed class SectionAutoScrollController
{
    private const double Edge = 38;
    private readonly ScrollViewer _scroll;
    private readonly TrackTimeline _timeline;
    private bool _running;
    private double _pointerViewportX;

    public SectionAutoScrollController(ScrollViewer scroll, TrackTimeline timeline) { _scroll = scroll; _timeline = timeline; }

    /// <summary>The drag pointer moved (timeline x), or ended when null.</summary>
    public void Track(double? pointerX)
    {
        if (pointerX is not { } x)
        {
            if (_running) CompositionTarget.Rendering -= OnRender;
            _running = false;
            return;
        }
        _pointerViewportX = x - _scroll.HorizontalOffset;
        if (_running) return;
        CompositionTarget.Rendering += OnRender;
        _running = true;
    }

    private void OnRender(object? sender, EventArgs e) => Advance();

    internal void Advance()
    {
        var viewport = _scroll.ViewportWidth;
        if (viewport <= 1) return;
        var direction = _pointerViewportX < Edge ? -1 :
            _pointerViewportX > viewport - Edge ? 1 : 0;
        if (direction == 0) return;
        var penetration = direction < 0
            ? Math.Clamp((Edge - _pointerViewportX) / Edge, 0, 1)
            : Math.Clamp((_pointerViewportX - (viewport - Edge)) / Edge, 0, 1);
        var nextOffset = Math.Clamp(_scroll.HorizontalOffset + direction * (3 + 15 * penetration),
            0, _scroll.ScrollableWidth);
        if (Math.Abs(nextOffset - _scroll.HorizontalOffset) < 0.1) return;
        _scroll.ScrollToHorizontalOffset(nextOffset);
        _timeline.UpdateSectionDragPointer(_pointerViewportX + nextOffset);
    }
}
