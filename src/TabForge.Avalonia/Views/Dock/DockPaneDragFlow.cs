using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using TabForge.Avalonia.Shell;
using TabForge.Docking;

namespace TabForge.Avalonia.Views.Docking;

/// <summary>What a pane drag asks of the dock workspace.</summary>
internal interface IDockPaneDragHost
{
    bool CanDrop(string panelId, string targetHostId, DockDropZone zone);
    bool DockPanelTo(string panelId, string targetHostId, DockDropZone zone, double fraction = DockLayoutTree.EdgeFraction, int? tabIndex = null);
    bool FloatPanelAt(string panelId, Point dipTopLeft);
    ThemeVariant Theme { get; }
}

// Owns: one dock tab drag across windows: over the main dock it shows the drop-zone preview, outside it the drag ghost; the drop
//   docks the pane (also one dragged from a floating window) or floats it at the drop point; Escape cancels until release.
// Does not own: the layout edits (IDockPaneDragHost, DockWorkspaceController), the press gesture (DockTabSurface), the ghost window (DockDragGhost).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests, DockWorkspaceControllerTests.
/// <summary>Routes the tab drags of the main dock and of every floating window, in screen pixels. UI thread only.</summary>
internal sealed class DockPaneDragFlow
{
    private readonly IDockPaneDragHost _host;
    private readonly DockWorkspaceView _main;
    private DockDragGhost? _ghost;
    private readonly DragSession<string> _drag;

    public DockPaneDragFlow(IDockPaneDragHost host, DockWorkspaceView main)
    {
        _host = host;
        _main = main;
        _drag = new DragSession<string>(() =>
        {
            _main.HideDropPreview();
            _ghost?.Hide();
        });
    }

    public bool IsDragging => _drag.IsDragging;

    /// <summary>Closes the ghost window for good; a later drag makes a new one.</summary>
    public void CloseGhost()
    {
        _ghost?.Close();
        _ghost = null;
    }

    public DockDragGhost? Ghost => _ghost is { IsVisible: true } ? _ghost : null;

    /// <summary>Follows the drags of one tab group (of the main dock or a floating window).</summary>
    public void Hook(DockTabSurface surface)
    {
        surface.TabDragMoved += (id, e) => Move(id, surface.PointToScreen(e.GetPosition(surface)));
        surface.TabDragEnded += (id, e) => Drop(id, surface.PointToScreen(e.GetPosition(surface)));
    }

    /// <summary>Escape cancels a running drag; added as a tunnelling key handler on each window that hosts dock tabs.</summary>
    public void OnKeyDown(object? sender, KeyEventArgs e) => _drag.OnKeyDown(sender, e);

    public void Move(string panelId, PixelPoint screen)
    {
        if (!_drag.TryFollow(panelId)) return;
        if (MainPoint(screen) is { } point)
        {
            _ghost?.Hide();
            if (_main.DropTargetAt(point) is { } hit && _host.CanDrop(panelId, hit.HostId, hit.Zone)) _main.ShowDropPreview(hit.HostId, hit.Zone, DockLayoutTree.EdgeFraction, hit.TabIndex, hit.InStrip);
            else _main.HideDropPreview();
            return;
        }
        _main.HideDropPreview();
        _ghost ??= new DockDragGhost();
        _ghost.Follow(DockPaneTable.Rows.FirstOrDefault(r => r.Id == panelId)?.PaneTitle ?? panelId, screen, _host.Theme);
    }

    public void Drop(string panelId, PixelPoint screen)
    {
        if (!_drag.Drop(panelId)) return;
        if (MainPoint(screen) is not { } point)
        {
            var scaling = TopLevel.GetTopLevel(_main)?.RenderScaling ?? 1;
            _host.FloatPanelAt(panelId, new Point(screen.X / scaling, screen.Y / scaling));
        }
        else if (_main.DropTargetAt(point) is { } hit) _host.DockPanelTo(panelId, hit.HostId, hit.Zone, DockLayoutTree.EdgeFraction, hit.TabIndex);
    }

    public void Cancel() => _drag.Cancel();

    /// <summary>The point in the main dock under a screen position, or null when it lies outside the dock.</summary>
    private Point? MainPoint(PixelPoint screen)
    {
        if (TopLevel.GetTopLevel(_main) is null) return null;
        var point = _main.PointToClient(screen);
        return new Rect(_main.Bounds.Size).Contains(point) ? point : null;
    }
}
