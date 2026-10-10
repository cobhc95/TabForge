using System.Windows;
using System.Windows.Input;
using TabForge.Models;

namespace TabForge.Views;

// Owns: the hover shade: which bar cell the pointer is over (HoverCellAt, UpdateHover), the cell bounds, and the HoverCellChanged
//   event.
// Does not own: drawing the shade (the panel moves one shared rectangle) and the bar marker (ArrangementPanel.Timeline.cs).
// Tests: TestTimelineHoverAndBarMarker.

internal sealed partial class TrackTimeline
{
    private int _hoverBar = -1, _hoverTrack = -1;

    /// <summary>The pointer moved onto another bar cell (bar, track), or off every cell (-1, -1). Also raised by <see cref="RefreshHover"/>.</summary>
    public event EventHandler<(int bar, int track)>? HoverCellChanged;

    /// <summary>The bar cell under <paramref name="p"/> (timeline coordinates, scroll offsets included), or (-1, -1) outside the cells.</summary>
    internal (int Bar, int Track) HoverCellAt(Point p)
    {
        if (Project is null) return (-1, -1);
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        if (p.Y < gridTop || p.X < 0) return (-1, -1);
        var inGrid = p.Y + VerticalScrollOffset - gridTop;
        var track = ArrangementPanel.RowIndexAt(Project, inGrid);
        if (track < 0) return (-1, -1);
        var inRow = inGrid - ArrangementPanel.RowTopOf(Project, track);
        if (inRow < 0 || inRow >= ArrangementPanel.RowHeightFor(Project)) return (-1, -1);   // group header or audio lane
        var bars = BarCount;
        if (bars == 0 || p.X >= XOfBar(bars)) return (-1, -1);
        return (BarAt(p.X), track);
    }

    /// <summary>The bar cell's rectangle in timeline coordinates (same inset as the drawn cell), or null when it does not exist.</summary>
    internal Rect? CellBounds(int bar, int track)
    {
        if (Project is null || bar < 0 || bar >= BarCount || track < 0 || track >= Project.Tracks.Count) return null;
        var top = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowTopOf(Project, track) - VerticalScrollOffset;
        return new Rect(XOfBar(bar), top + 1, Math.Max(1, WidthOfBar(bar) - 1), ArrangementPanel.RowHeightFor(Project) - 2);
    }

    private bool HoverSuppressed => _dragging || AreaMove.Active || _sectionDragging || SectionEdges.MarkerDragging || SectionEdges.ResizeMarker is not null ||
        Mouse.LeftButton == MouseButtonState.Pressed;

    private void UpdateHover(Point p)
    {
        var cell = HoverSuppressed ? (-1, -1) : HoverCellAt(p);
        if (cell.Item1 == _hoverBar && cell.Item2 == _hoverTrack) return;
        _hoverBar = cell.Item1; _hoverTrack = cell.Item2;
        HoverCellChanged?.Invoke(this, cell);
    }

    /// <summary>Test hook: what the mouse-move handler does for a pointer at <paramref name="p"/>.</summary>
    internal void SimulateHover(Point p) => UpdateHover(p);

    /// <summary>Re-places the hover after the view scrolled, zoomed or changed shape, from where the pointer is now.</summary>
    internal void RefreshHover()
    {
        var cell = IsMouseOver && !HoverSuppressed && Project is not null ? HoverCellAt(Mouse.GetPosition(this)) : (-1, -1);
        _hoverBar = cell.Item1; _hoverTrack = cell.Item2;
        HoverCellChanged?.Invoke(this, cell);
    }

    internal void ClearHover()
    {
        if (_hoverBar < 0 && _hoverTrack < 0) return;
        _hoverBar = _hoverTrack = -1;
        HoverCellChanged?.Invoke(this, (-1, -1));
    }
}
