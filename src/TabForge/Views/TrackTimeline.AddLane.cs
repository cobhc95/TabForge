using System.Windows;
using System.Windows.Media;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// TrackTimeline: the timeline half of the Add-track lane (the strip under the last row; the track list draws the other half).
// A click on it asks the host to add a track; media dropped on it makes an audio track (see DropPreviewAt).
internal sealed partial class TrackTimeline
{
    private bool _addLaneShown;

    /// <summary>The Add-track lane under the last row is drawn and takes clicks and drops.</summary>
    public bool AddLaneShown
    {
        get => _addLaneShown;
        set { if (_addLaneShown == value) return; _addLaneShown = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>A click on the lane: the host shows the add-track prompt.</summary>
    public event Action? AddLaneClicked;

    private double _addLaneFill = AddTrackLane.Height;

    /// <summary>The height the panel gives the lane: everything left below the last row (never less than one row).</summary>
    internal double AddLaneFill
    {
        get => _addLaneFill;
        set { if (Math.Abs(_addLaneFill - value) < 0.01) return; _addLaneFill = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    internal double AddLaneHeight => _addLaneShown ? Math.Max(AddTrackLane.Height, _addLaneFill) : 0;

    /// <summary>Top of the lane in timeline coordinates (rows end flush against it).</summary>
    internal double AddLaneTop =>
        ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowsHeight(Project) - VerticalScrollOffset;

    /// <summary>The point is inside the lane (timeline coordinates).</summary>
    internal bool IsInAddLane(Point p)
    {
        if (!_addLaneShown || Project is null || Project.Tracks.Count == 0) return false;
        var top = AddLaneTop;
        return p.Y >= top && p.Y < top + AddLaneHeight;
    }

    /// <summary>Raises the click (also the test seam).</summary>
    internal void RaiseAddLaneClicked() => AddLaneClicked?.Invoke();

    private void DrawAddLane(DrawingContext dc, double width)
    {
        if (!_addLaneShown) return;
        var top = AddLaneTop;
        var h = AddLaneHeight;
        // One zone with the track list's half: the same subtle panel colour, a soft glow on hover, an accent outline while a file is dragged over it.
        // The label lives on the list's half only.
        dc.DrawRectangle(Application.Current.TryFindResource("Panel2Brush") as Brush ?? Draw.Solid(_theme.Board, 0.55), null, new Rect(0, top, width, h));
        if (_addLaneHot && !_addLaneDrag) dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.10), null, new Rect(0, top, width, h));
        if (_addLaneDrag)
        {
            dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.14), null, new Rect(0, top, width, h));
            var pen = Draw.Pen(_theme.Accent, 2);
            dc.DrawLine(pen, new Point(0, top + 1), new Point(width, top + 1));
            dc.DrawLine(pen, new Point(0, top + h - 1), new Point(width, top + h - 1));
            dc.DrawLine(pen, new Point(width - 1, top + 1), new Point(width - 1, top + h - 1));
        }
    }

    private bool _addLaneHot, _addLaneDrag;

    /// <summary>The pointer is over the zone (set by the panel when it is over the list's half).</summary>
    internal bool AddLaneHot
    {
        get => _addLaneHot;
        set { if (_addLaneHot == value) return; _addLaneHot = value; RefreshOverlay(); }
    }

    /// <summary>A file dragged over the zone would make an audio track here (both halves light up).</summary>
    internal bool AddLaneDrag => _addLaneDrag;

    /// <summary>The pointer entered or left the zone on the timeline's half.</summary>
    internal event Action<bool>? AddLaneHotChanged;

    /// <summary>The zone's drag state changed (the panel mirrors it onto the list's half).</summary>
    internal event Action<bool>? AddLaneDragChanged;

    private void NotifyAddLaneHot(bool hot)
    {
        if (_addLaneHot == hot) return;
        AddLaneHot = hot;
        AddLaneHotChanged?.Invoke(hot);
    }

    internal void SetAddLaneDrag(bool on)
    {
        if (_addLaneDrag == on) return;
        _addLaneDrag = on;
        RefreshOverlay();
        AddLaneDragChanged?.Invoke(on);
    }
}
