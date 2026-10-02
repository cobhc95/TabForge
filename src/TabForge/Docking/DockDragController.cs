using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static TabForge.Docking.DockLayoutTree;

namespace TabForge.Docking;

/// <summary>What the tab drag needs from the workspace: the registered panels, the hosts on screen, the layout and the two ways a drag ends.</summary>
internal interface IDockDragHost
{
    Window Owner { get; }
    IReadOnlyList<DockHostView> Hosts { get; }
    IReadOnlyList<DockSlot> Slots { get; }
    IReadOnlyList<DockFloatingState> Floating { get; }
    DockPanelRegistration Panel(string id);
    void SelectPanel(string id);
    Size MinimumSize(DockNodeState node);
    double SoloHostMinHeight(string panelId);
    void DropOn(DockDropDestination destination, string panelId);
    void FloatAt(string panelId, Point screenPoint);
}

// Owns: the drag of a dock panel: pointer tracking, drop targets and the preview.
// Does not own: the dock layout persistence and the panel contents.
// Tests: TestDockRatioNotRewrittenByAutoFit.
/// <summary>Dragging a dock tab: arming, the ghost window, the drop zone under the pointer and its preview.</summary>
internal sealed class DockDragController
{
    internal const double SplitterSize = 5;
    internal const double EdgeFraction = DockLayoutTree.EdgeFraction;

    private readonly IDockDragHost _host;
    private DockTabSurface? _dragSource;
    private string? _dragPanelId;
    private Point _dragStart;
    private bool _dragArmed;
    private bool _dragging;
    private DockDropDestination? _destination;
    private DockDragGhostWindow? _ghost;

    internal DockDragController(IDockDragHost host) => _host = host;

    internal bool IsDragging => _dragging;

    private static Brush ResourceBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    internal void ArmDrag(DockTabSurface source, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || source.IsClosed) return;
        _host.SelectPanel(source.PanelId);
        _dragSource = source;
        _dragPanelId = source.PanelId;
        _dragStart = e.GetPosition(source);
        _dragArmed = true;
        source.CaptureMouse();
        e.Handled = true;
    }

    internal void MoveDrag(DockTabSurface source, MouseEventArgs e)
    {
        if (!_dragArmed || !ReferenceEquals(source, _dragSource) || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(source);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            _ghost = new DockDragGhostWindow(_host.Owner, _host.Panel(_dragPanelId!).Title);
            _ghost.Show();
        }
        // The dragged tab can leave its window mid-drag (re-docked, closed): then use the real cursor.
        var screenPoint = Shell.ScreenPoints.TryToScreen(source, point, out var converted) ? converted : Shell.ScreenPoints.Cursor();
        UpdateDestination(screenPoint);
        _ghost?.MoveTo(screenPoint);
        e.Handled = true;
    }

    internal void EndTabDrag(DockTabSurface source, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(source, _dragSource)) return;
        if (_dragging && _dragPanelId is not null)
        {
            var finalPoint = Shell.ScreenPoints.TryToScreen(source, e.GetPosition(source), out var converted)
                ? converted : Shell.ScreenPoints.Cursor();
            UpdateDestination(finalPoint);
            if (_destination is { } destination)
                _host.DropOn(destination, _dragPanelId);
            else
                _host.FloatAt(_dragPanelId, finalPoint);
            e.Handled = true;
        }
        EndDrag();
    }

    internal void EndDrag()
    {
        if (_dragSource is { IsMouseCaptured: true } source) source.ReleaseMouseCapture();
        _dragSource = null;
        _dragPanelId = null;
        _dragArmed = false;
        _dragging = false;
        _destination = null;
        _ghost?.Close();
        _ghost = null;
        ClearPreview();
    }

    private void UpdateDestination(Point screenPoint)
    {
        _destination = null;
        ClearPreview();
        if (_dragPanelId is null) return;
        foreach (var host in _host.Hosts)
        {
            // Hosts rebuilt or closed during the drag are not on screen: skip them instead of throwing.
            if (host.Surface is null || !host.Surface.IsLoaded || !Shell.ScreenPoints.IsOnScreen(host.Surface)) continue;
            var targetRect = ScreenRect(host.Surface);
            if (!targetRect.Contains(screenPoint)) continue;
            if (!Shell.ScreenPoints.TryFromScreen(host.Surface, screenPoint, out var local)) continue;
            if (host.Node.Kind == "editor" && ClassifyZone(local, host.Surface.RenderSize) == DockDropZone.Center)
                return;
            if (host.Node.Kind == "tabs" && host.Node.Panels.Count == 1 && host.Node.Panels[0] == _dragPanelId)
                return;

            var overTabHeader = host.IsOverTabHeader(screenPoint);
            var zone = overTabHeader
                ? DockDropZone.Center
                : ClassifyZone(local, host.Surface.RenderSize);
            if (!CanDropOn(host, zone, host.Surface.RenderSize)) return;
            var fraction = zone == DockDropZone.Center ? 0 : SplitFractionFor(host.Surface, host.Node, zone,
                zone is DockDropZone.Left or DockDropZone.Right);
            var preview = PreviewRect(host.Surface, host.Slot.Root, zone, host.Node, fraction);
            ShowPreview(host.Slot, preview);
            _destination = new DockDropDestination(host.Node.HostId, zone, host.Slot.IsMain ? null :
                _host.Floating.FirstOrDefault(f => RootBelongsToSlot(host.Node, f.Root))?.Id,
                preview, fraction, zone == DockDropZone.Center && overTabHeader ? host.InsertionIndexAt(screenPoint) : null);
            return;
        }
    }

    private bool CanDropOn(DockHostView host, DockDropZone zone, Size size)
    {
        if (host.Node.Kind == "editor" && zone == DockDropZone.Center) return false;
        if (host.Node.Kind == "tabs" && host.Node.Panels.Count == 1 && host.Node.Panels[0] == _dragPanelId)
            return false;
        if (zone == DockDropZone.Center) return host.Node.Kind == "tabs";
        var panel = _host.Panel(_dragPanelId!);
        var existingMin = _host.MinimumSize(host.Node);
        var horizontal = zone is DockDropZone.Left or DockDropZone.Right;
        var available = (horizontal ? size.Width : size.Height) - SplitterSize;
        var needed = (horizontal ? panel.MinWidth + existingMin.Width : _host.SoloHostMinHeight(panel.Id) + existingMin.Height);
        return available >= needed;
    }

    private static DockDropZone ClassifyZone(Point point, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return DockDropZone.Center;
        var x = point.X / size.Width;
        var y = point.Y / size.Height;
        var candidates = new List<(DockDropZone Zone, double Distance)>();
        if (x <= EdgeFraction) candidates.Add((DockDropZone.Left, x / EdgeFraction));
        if (x >= 1 - EdgeFraction) candidates.Add((DockDropZone.Right, (1 - x) / EdgeFraction));
        if (y <= EdgeFraction) candidates.Add((DockDropZone.Top, y / EdgeFraction));
        if (y >= 1 - EdgeFraction) candidates.Add((DockDropZone.Bottom, (1 - y) / EdgeFraction));
        return candidates.Count == 0 ? DockDropZone.Center : candidates.MinBy(c => c.Distance).Zone;
    }

    private Rect PreviewRect(FrameworkElement host, Grid slotRoot, DockDropZone zone, DockNodeState node, double fraction)
    {
        var origin = host.TranslatePoint(new Point(0, 0), slotRoot);
        var width = host.ActualWidth;
        var height = host.ActualHeight;
        var horizontal = zone is DockDropZone.Left or DockDropZone.Right;
        if (zone == DockDropZone.Center) return new Rect(origin, new Size(width, height));
        if (horizontal)
        {
            var newWidth = Math.Max(0, (width - SplitterSize) * fraction);
            return zone == DockDropZone.Left
                ? new Rect(origin, new Size(newWidth, height))
                : new Rect(origin.X + width - newWidth, origin.Y, newWidth, height);
        }
        var newHeight = Math.Max(0, (height - SplitterSize) * fraction);
        return zone == DockDropZone.Top
            ? new Rect(origin, new Size(width, newHeight))
            : new Rect(origin.X, origin.Y + height - newHeight, width, newHeight);
    }

    private double SplitFractionFor(FrameworkElement host, DockNodeState node, DockDropZone zone, bool horizontal)
    {
        var existing = _host.MinimumSize(node);
        var newPanel = _host.Panel(_dragPanelId!);
        var available = (horizontal ? host.ActualWidth : host.ActualHeight) - SplitterSize;
        var newMinHeight = _host.SoloHostMinHeight(newPanel.Id);
        var total = horizontal ? newPanel.MinWidth + existing.Width : newMinHeight + existing.Height;
        if (available <= 0 || total <= 0) return EdgeFraction;
        var minFraction = (horizontal ? newPanel.MinWidth : newMinHeight) / available;
        var maxFraction = 1 - (horizontal ? existing.Width : existing.Height) / available;
        return minFraction <= maxFraction ? Math.Clamp(EdgeFraction, minFraction, maxFraction) : EdgeFraction;
    }

    private static Rect ScreenRect(FrameworkElement element) => Shell.ScreenPoints.ScreenRect(element);

    private static bool RootBelongsToSlot(DockNodeState host, DockNodeState? root) =>
        root is not null && FindNode(root, host.HostId) is not null;

    private void ShowPreview(DockSlot slot, Rect rect)
    {
        var border = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            BorderThickness = new Thickness(2),
            Background = ResourceBrush("AccentSoftBrush", Color.FromArgb(56, 76, 154, 255)),
            CornerRadius = new CornerRadius(5),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(76, 154, 255),
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.95
            }
        };
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        slot.Preview.Children.Add(border);
    }

    private void ClearPreview()
    {
        foreach (var slot in _host.Slots) slot.Preview.Children.Clear();
    }
}
