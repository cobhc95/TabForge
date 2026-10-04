using System.Windows;

namespace TabForge.Views;

// Owns: diagnostics seams that run the track-row drag and section drag without a pressed mouse button (animated capture).
// Does not own: the gestures (ArrangementPanel.Interaction.cs, TrackTimeline.Interaction.cs).
// Tests: none (driven through --capture "frames").
public sealed partial class ArrangementPanel
{
    /// <summary>Picks up the track row <paramref name="index"/> (pointer at its middle).</summary>
    internal void SimulateTrackDragStart(int index)
    {
        if (_project is null || index < 0 || index >= _trackRows.Count) return;
        var y = RowTopOf(_project, index) + TrackRowHeight / 2.0;
        BeginControlTrackDrag(index, _trackRows[index], new Point(20, y));
        _dragArmed = true;
        TrackDragStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves the picked-up row so the pointer is at <paramref name="y"/> (content coordinates of the track controls).</summary>
    internal void SimulateTrackDragMove(double y)
    {
        _dragTargetTrack = TrackIndexAtY(y);
        UpdateDragVisual(new Point(20, y));
    }

    /// <summary>Drops the row where it is and reorders the tracks.</summary>
    internal void SimulateTrackDragEnd()
    {
        var from = _dragFromTrack;
        var to = _dragTargetTrack;
        _dragFromTrack = -1; _dragTargetTrack = -1; _dragArmed = false; _dragCaptureRow = null;
        UpdateDragVisual();
        if (from >= 0 && to >= 0 && to != from) TrackReordered?.Invoke(this, (from, to));
    }

    /// <summary>Zooms the timeline out by <paramref name="steps"/> wheel steps.</summary>
    internal void SimulateZoomOut(int steps) { for (var i = 0; i < steps; i++) ZoomTimeline(ZoomStep(false), 0); }

    internal double TrackRowCentreY(int index) => _project is null ? 0 : RowTopOf(_project, index) + TrackRowHeight / 2.0;
    internal bool SimulateSectionDragStart(int section, double x) => _timeline.SimulateSectionDragStart(section, x);
    internal void SimulateSectionDragMove(double x) => _timeline.UpdateSectionDragPointer(x);
    internal void SimulateSectionDragEnd() => _timeline.SimulateSectionDragEnd();
}
