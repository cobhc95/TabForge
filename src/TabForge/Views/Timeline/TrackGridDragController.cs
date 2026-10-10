using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>What the track grid drag needs from its window.</summary>
internal interface ITrackGridDragHost
{
    DataGrid TrackGrid { get; }
    /// <summary>Moves the track at <paramref name="from"/> to <paramref name="to"/> as one edit.</summary>
    void MoveTrackTo(int from, int to);
}

// Owns: dragging a row of the practice panel's track grid to reorder tracks (arming, live selection feedback, the drop).
// Does not own: the move itself (the window's track edit), the grid's columns.
// Tests: TestInteractions.
internal sealed class TrackGridDragController
{
    private readonly ITrackGridDragHost _host;
    private Point _start;
    private int _from = -1;
    private int _target = -1;
    private bool _armed;

    public TrackGridDragController(ITrackGridDragHost host) => _host = host;

    public void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _from = RowIndexAt(e.OriginalSource as DependencyObject);
        _target = _from;
        _armed = false;
        _start = e.GetPosition(_host.TrackGrid);
    }

    public void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_from < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var grid = _host.TrackGrid;
        var p = e.GetPosition(grid);
        if (!_armed && Math.Abs(p.Y - _start.Y) < 6) return;
        _armed = true;
        var target = RowIndexAt(grid.InputHitTest(p) as DependencyObject);
        if (target >= 0 && target != _target)
        {
            _target = target;
            grid.SelectedIndex = target;   // live feedback: selection follows the drag
        }
        e.Handled = true;
    }

    public void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var from = _from;
        var to = _target;
        var armed = _armed;
        _from = -1;
        _target = -1;
        _armed = false;
        if (armed && from >= 0 && to >= 0 && to != from)
        {
            _host.MoveTrackTo(from, to);
            e.Handled = true;
        }
    }

    private static int RowIndexAt(DependencyObject? source)
    {
        while (source is not null and not DataGridRow)
            source = VisualTreeHelper.GetParent(source);
        return source is DataGridRow row ? row.GetIndex() : -1;
    }
}
