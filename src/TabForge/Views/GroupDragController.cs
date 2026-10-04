using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>What a group-header drag needs from the arrangement panel (implemented by <see cref="ArrangementPanel"/>).</summary>
internal interface IGroupDragHost
{
    /// <summary>The track list's stack of headers, rows and the Add-track lane.</summary>
    Panel TrackListPanel { get; }
    void RaiseGroupMoved(int start, int count, int target);
}

// Owns: dragging a whole group by its header in the track list: the dragged elements, the drop target and the caret.
// Does not own: the group header itself (ArrangementPanel.Groups), moving the tracks (MainWindow via GroupMoved).
// Tests: TestTrackRowsEndFlush, TestTrackListFit.
internal sealed class GroupDragController
{
    private readonly IGroupDragHost _host;
    private int _start = -1, _target = -1;
    private Point _origin;
    private readonly List<UIElement> _elements = new();
    private Border? _caret;

    public GroupDragController(IGroupDragHost host) => _host = host;

    /// <summary>Makes <paramref name="header"/> drag its group; <paramref name="ignorePress"/> leaves presses on its own controls alone.</summary>
    public void Attach(Border header, SongProject project, int start, int count, Func<MouseButtonEventArgs, bool> ignorePress)
    {
        var controls = _host.TrackListPanel;
        header.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (ignorePress(e)) return;
            _start = start; _target = -1;
            _origin = e.GetPosition(controls);
            header.CaptureMouse();
            e.Handled = true;
        };
        header.MouseMove += (_, e) =>
        {
            if (_start != start || !header.IsMouseCaptured) return;
            var p = e.GetPosition(controls);
            var dy = p.Y - _origin.Y;
            if (_target < 0 && Math.Abs(dy) < 4) return;
            // The header and its rows follow the pointer; a caret shows where the group will land.
            if (_elements.Count == 0)
            {
                var from = controls.Children.IndexOf(header);
                for (var k = from; k < controls.Children.Count; k++)
                {
                    var el = controls.Children[k];
                    if (k > from && el is Border { Tag: "group-header" or "add-lane" }) break;
                    _elements.Add(el);
                    if (el is UIElement u) { u.Opacity = 0.85; Panel.SetZIndex(u, 10); }
                }
            }
            foreach (var el in _elements)
                if (el.RenderTransform is TranslateTransform t) t.Y = dy; else el.RenderTransform = new TranslateTransform(0, dy);
            _target = DropIndex(project, p.Y);
            ShowCaret(project, _target);
            e.Handled = true;
        };
        header.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_start != start) return;
            var target = _target;
            End();
            header.ReleaseMouseCapture();
            if (target >= 0 && (target < start || target > start + count)) _host.RaiseGroupMoved(start, count, target);
            e.Handled = true;
        };
        header.LostMouseCapture += (_, _) => { if (_start == start) End(); };
    }

    /// <summary>Where a dragged group goes: before the group header (or the end) nearest the pointer.</summary>
    private static int DropIndex(SongProject project, double y)
    {
        var best = project.Tracks.Count; var bestDistance = double.MaxValue;
        foreach (var run in ArrangementPanel.GroupRuns(project).Select(r => r.Start).Append(project.Tracks.Count))
        {
            var top = run < project.Tracks.Count ? ArrangementPanel.RowTopOf(project, run) - ArrangementPanel.GroupHeaderHeight : ArrangementPanel.RowsHeight(project);
            var distance = Math.Abs(y - top);
            if (distance < bestDistance) { bestDistance = distance; best = run; }
        }
        return best;
    }

    private void ShowCaret(SongProject project, int target)
    {
        var controls = _host.TrackListPanel;
        if (_caret is null)
        {
            _caret = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), IsHitTestVisible = false, Margin = new Thickness(4, 0, 4, 0) };
            _caret.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        }
        controls.Children.Remove(_caret);
        var before = target < project.Tracks.Count ? ArrangementPanel.RowTopOf(project, target) - ArrangementPanel.GroupHeaderHeight : ArrangementPanel.RowsHeight(project);
        _caret.RenderTransform = new TranslateTransform(0, 0);
        // Put the caret into the panel next to the element at that height (StackPanel order).
        var index = 0; double y = 0;
        foreach (UIElement el in controls.Children)
        {
            if (y >= before - 0.5) break;
            y += el.Visibility == Visibility.Collapsed ? 0 : (el as FrameworkElement)?.ActualHeight + ((el as FrameworkElement)?.Margin.Top ?? 0) ?? 0;
            index++;
        }
        controls.Children.Insert(Math.Min(index, controls.Children.Count), _caret);
    }

    private void End()
    {
        foreach (var el in _elements) { el.Opacity = 1; Panel.SetZIndex(el, 0); if (el.RenderTransform is TranslateTransform t) t.Y = 0; }
        _elements.Clear();
        if (_caret is not null) _host.TrackListPanel.Children.Remove(_caret);
        _start = -1;
        _target = -1;
    }
}
