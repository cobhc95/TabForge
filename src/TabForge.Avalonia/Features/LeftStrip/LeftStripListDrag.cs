using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: dragging in the Customize strip window: a row of the On the strip list moves to where the insertion line shows (or leaves the strip when
//   dropped outside the list), a row of the Available list is dropped into the strip at the line; while dragging, a ghost copy of the row follows the
//   pointer, the dragged row is dimmed in place, and the list scrolls near its top and bottom edges. The reorder is applied once, on release.
// Does not own: the row look (the window passes a builder), the lists' content and buttons (LeftStripCustomizeWindow), the cursor (never changed).
// Tests: no dedicated test class (pointer drags need a real pointer).
// Threads: UI thread only.
internal sealed class LeftStripListDrag
{
    private const double StartDistance = 5, EdgeZone = 28, MaxScrollStep = 14, LineHeight = 2, GhostOpacity = 0.85, GhostOutOfListOpacity = 0.4, DimmedRowOpacity = 0.35;

    private readonly Control _root;
    private readonly ListBox _strip;
    private readonly ObservableCollection<string> _on;
    private readonly Func<string, Control> _row;
    private readonly Action<int> _applied;
    private readonly Border _ghost = new() { IsVisible = false, Padding = new Thickness(8, 3), CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall), BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly Border _line = new() { Height = LineHeight, IsVisible = false, IsHitTestVisible = false };
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(UiTiming.SectionDragFrameMs) };

    private (ListBox List, string Id, int Index, Point Start)? _press;
    private bool _dragging;
    private Point _last;
    private int _insertAt;
    private bool _overStrip;
    private double _scrollStep;
    private ListBoxItem? _dimmed;
    private ScrollViewer? _scroller;

    /// <param name="root">The window's content: the pointer is captured to it while dragging, and the overlay positions are in its coordinates.</param>
    /// <param name="row">Builds the row's look (icon and name) for an id; used for the ghost.</param>
    /// <param name="applied">Runs after a drop changed the strip list, with the index to select in it (-1 for none); the window refills the Available list.</param>
    public LeftStripListDrag(Control root, ListBox strip, ListBox available, ObservableCollection<string> on, Func<string, Control> row, Action<int> applied)
    {
        _root = root;
        _strip = strip;
        _on = on;
        _row = row;
        _applied = applied;
        _ghost[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoft");
        _ghost[!Border.BorderBrushProperty] = new DynamicResourceExtension("Accent");
        _line[!Border.BackgroundProperty] = new DynamicResourceExtension("Accent");
        Overlay.Children.Add(_line);
        Overlay.Children.Add(_ghost);
        _scrollTimer.Tick += (_, _) => ScrollTick();
        strip.AddHandler(InputElement.PointerPressedEvent, (_, e) => Pressed(strip, e), RoutingStrategies.Tunnel);
        available.AddHandler(InputElement.PointerPressedEvent, (_, e) => Pressed(available, e), RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(drop: false), RoutingStrategies.Tunnel);
    }

    /// <summary>The layer above the window's content that holds the ghost and the insertion line; it takes no input.</summary>
    public Canvas Overlay { get; } = new() { IsHitTestVisible = false };

    private void Pressed(ListBox list, PointerPressedEventArgs e)
    {
        _press = null;
        if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        var hit = list.InputHitTest(e.GetPosition(list)) as Visual;
        while (hit is not null && hit is not ListBoxItem) hit = hit.GetVisualParent();
        if (hit is ListBoxItem { DataContext: string id } item) _press = (list, id, list.IndexFromContainer(item), e.GetPosition(_root));
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_press is not { } press) return;
        _last = e.GetPosition(_root);
        if (!_dragging)
        {
            if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed) { _press = null; return; }
            if (Math.Abs(_last.X - press.Start.X) < StartDistance && Math.Abs(_last.Y - press.Start.Y) < StartDistance) return;
            Begin(e, press);
        }
        UpdateTarget();
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) { _press = null; return; }
        _last = e.GetPosition(_root);
        UpdateTarget();
        End(drop: true);
        e.Pointer.Capture(null);
    }

    private void Begin(PointerEventArgs e, (ListBox List, string Id, int Index, Point Start) press)
    {
        _dragging = true;
        e.Pointer.Capture(_root as IInputElement);
        _scroller = _strip.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        _dimmed = press.List.ContainerFromIndex(press.Index) as ListBoxItem;
        if (_dimmed is not null) _dimmed.Opacity = DimmedRowOpacity;
        _ghost.Child = _row(press.Id);
        _ghost.Width = Math.Max(120, press.List.Bounds.Width - 8);
        _ghost.IsVisible = true;
    }

    /// <summary>Moves the ghost to the pointer and the insertion line to where a drop would land.</summary>
    private void UpdateTarget()
    {
        var strip = BoundsOf(_strip);
        Canvas.SetLeft(_ghost, _last.X + 10);
        Canvas.SetTop(_ghost, _last.Y - 14);
        _overStrip = strip.Contains(_last);
        var fromStrip = _press is { } p && ReferenceEquals(p.List, _strip);
        _ghost.Opacity = fromStrip && !_overStrip ? GhostOutOfListOpacity : GhostOpacity;
        _line.IsVisible = _overStrip;
        _scrollStep = 0;
        if (!_overStrip) { _scrollTimer.Stop(); return; }
        var lineY = strip.Top + 1;
        _insertAt = 0;
        var any = false;
        foreach (var item in _strip.GetVisualDescendants().OfType<ListBoxItem>().OrderBy(i => _strip.IndexFromContainer(i)))
        {
            var box = BoundsOf(item);
            var index = _strip.IndexFromContainer(item);
            any = true;
            if (_last.Y < box.Center.Y) { _insertAt = index; lineY = box.Top; goto placed; }
            _insertAt = index + 1;
            lineY = box.Bottom;
        }
        if (!any) _insertAt = _on.Count;
    placed:
        lineY = Math.Clamp(lineY, strip.Top + 1, strip.Bottom - 1);
        Canvas.SetLeft(_line, strip.Left + 2);
        Canvas.SetTop(_line, lineY - LineHeight / 2);
        _line.Width = Math.Max(0, strip.Width - 4);
        if (_last.Y < strip.Top + EdgeZone) _scrollStep = -MaxScrollStep * (1 - (_last.Y - strip.Top) / EdgeZone);
        else if (_last.Y > strip.Bottom - EdgeZone) _scrollStep = MaxScrollStep * (1 - (strip.Bottom - _last.Y) / EdgeZone);
        _scrollStep = Math.Clamp(_scrollStep, -MaxScrollStep, MaxScrollStep);
        if (_scrollStep != 0 && _scroller is not null) { if (!_scrollTimer.IsEnabled) _scrollTimer.Start(); }
        else _scrollTimer.Stop();
    }

    private void ScrollTick()
    {
        if (_scroller is null || _scrollStep == 0) { _scrollTimer.Stop(); return; }
        var offset = _scroller.Offset;
        var max = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        _scroller.Offset = new Vector(offset.X, Math.Clamp(offset.Y + _scrollStep, 0, max));
        UpdateTarget();
    }

    private void End(bool drop)
    {
        var press = _press;
        var wasDragging = _dragging;
        _press = null;
        _dragging = false;
        _scrollTimer.Stop();
        _ghost.IsVisible = false;
        _line.IsVisible = false;
        if (_dimmed is not null) _dimmed.Opacity = 1;
        _dimmed = null;
        if (!wasDragging || !drop || press is not { } p) return;
        if (ReferenceEquals(p.List, _strip))
        {
            if (p.Index < 0 || p.Index >= _on.Count) return;
            if (_overStrip)
            {
                var to = _insertAt > p.Index ? _insertAt - 1 : _insertAt;
                if (to != p.Index) _on.Move(p.Index, Math.Clamp(to, 0, _on.Count - 1));
                _applied(Math.Clamp(to, 0, _on.Count - 1));
            }
            else
            {
                _on.RemoveAt(p.Index);
                _applied(Math.Min(p.Index, _on.Count - 1));
            }
        }
        else if (_overStrip)
        {
            var at = Math.Clamp(_insertAt, 0, _on.Count);
            _on.Insert(at, p.Id);
            _applied(at);
        }
    }

    private Rect BoundsOf(Control control) => new(control.TranslatePoint(default, _root) ?? default, control.Bounds.Size);
}
