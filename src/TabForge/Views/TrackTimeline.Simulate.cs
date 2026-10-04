using System.Windows;
using System.Windows.Input;

namespace TabForge.Views;

// Owns: diagnostics seams that run the section-drag gesture without a pressed mouse button (animated capture).
// Does not own: the gesture itself (TrackTimeline.Interaction.cs).
// Tests: none (driven through --capture "frames").
internal sealed partial class TrackTimeline
{
    /// <summary>Presses the section at <paramref name="sectionIndex"/> and starts dragging it with the pointer at <paramref name="pointerX"/>.</summary>
    internal bool SimulateSectionDragStart(int sectionIndex, double pointerX)
    {
        var hits = SectionHits();
        if (sectionIndex < 0 || sectionIndex >= hits.Count) return false;
        _dragging = true;
        _sectionPressOriginIndex = sectionIndex;
        _dragStart = new Point(hits[sectionIndex].Bounds.X + hits[sectionIndex].Bounds.Width / 2, 0);
        BeginSectionDrag(hits, new Point(pointerX, 0));
        return true;
    }

    /// <summary>Presses the right (or left) edge of the section at <paramref name="sectionIndex"/> as the mouse-down on the edge does.</summary>
    internal bool SimulateSectionResizeStart(int sectionIndex, bool rightEdge)
    {
        var hits = SectionHits();
        if (sectionIndex < 0 || sectionIndex >= hits.Count) return false;
        SectionEdges.BeginResize(hits[sectionIndex].Marker, rightEdge);
        SectionResizeStarting?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Moves the pressed section edge to <paramref name="x"/> as the mouse-move does.</summary>
    internal void SimulateSectionResizeMove(double x) => SectionEdges.ResizeMove(x);

    /// <summary>Releases the simulated drag as the mouse-up does (drop, settle animation, reorder event).</summary>
    internal void SimulateSectionDragEnd() =>
        RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
}
