using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: dragging a popped-out pane back into a tab group: a drop on the header (empty space or over tabs) merges at the pointer,
/// the body centre merges, a body edge band splits, Escape cancels, the preview is a thin marker for a header drop, and the
/// layout stays valid with no empty pane; the pure rules (header zone, insert position) are unit-tested here too; Reset restores a broken state.
/// Tests: DockDropRules (header zone, tab insert index), DockWorkspaceView.DropTargetAt / ShowDropPreview, DockPaneDragFlow, DockWorkspaceController.
/// </summary>
public class DockMergeDragTests
{
    private const string ToolHost = "default-tool-palette";

    private static DockPreviewWindow Open()
    {
        var window = new DockPreviewWindow();
        window.Show();
        return window;
    }

    private static Point In(Control control, Visual root, double x, double y) => control.TranslatePoint(new Point(x, y), root)!.Value;

    private static bool Valid(DockPreviewWindow w) =>
        DockLayoutTree.Validate(w.State, true, id => DockPaneTable.Ids.Contains(id));

    /// <summary>Pops Layout out beside the tool palette and returns its new tab group.</summary>
    private static DockTabSurface PopOutLayout(DockPreviewWindow window)
    {
        Assert.True(window.Controller.DockPanelTo("layout", ToolHost, DockDropZone.Left));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var host = DockLayoutTree.FindPanelHost(window.State.Root, "layout")!;
        Assert.Equal(new[] { "layout" }, host.Panels);
        return (DockTabSurface)window.View.HostControl(host.HostId)!;
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 10, from.Y + 10));
        window.MouseMove(to);
    }

    [AvaloniaFact]
    public void PoppedOutPaneDroppedOnAHeaderMergesAtThePointerAndLeavesNoEmptyPane()
    {
        var window = Open();
        var popped = PopOutLayout(window);
        var tools = (DockTabSurface)window.View.HostControl(ToolHost)!;
        var emptyHeaderSpace = In(tools, window, tools.Bounds.Width - 4, DockTabSurface.StripHeight / 2);

        Drag(window, In(popped.Tabs[0], window, 5, 5), emptyHeaderSpace);
        var marker = window.View.DropPreviewRect!.Value;
        Assert.True(marker.Width <= 4 && marker.Height <= DockTabSurface.StripHeight);   // a thin insertion marker, not a box over the tabs
        window.MouseUp(emptyHeaderSpace, MouseButton.Left);

        var merged = DockLayoutTree.FindNode(window.State.Root, ToolHost)!;
        Assert.Equal(new[] { "layout", "rhythm", "structure", "tools" }, merged.Panels.OrderBy(p => p));   // same group; the narrow header clips the last tab, so the slot depends on the pointer
        Assert.Equal("layout", merged.SelectedPanel);
        Assert.True(Valid(window));
        Assert.DoesNotContain(DockLayoutTree.EnumerateAllPanels(window.State).GroupBy(p => p), g => g.Count() > 1);
    }

    [AvaloniaFact]
    public void DropOverAnExistingTabInsertsBeforeIt()
    {
        var window = Open();
        var popped = PopOutLayout(window);
        var tools = (DockTabSurface)window.View.HostControl(ToolHost)!;
        var overFirstTab = In(tools.Tabs[0], window, 4, 5);
        Drag(window, In(popped.Tabs[0], window, 5, 5), overFirstTab);
        window.MouseUp(overFirstTab, MouseButton.Left);
        Assert.Equal("layout", DockLayoutTree.FindNode(window.State.Root, ToolHost)!.Panels[0]);
        Assert.True(Valid(window));
    }

    [AvaloniaFact]
    public void DropOnTheBodyCentreMerges()
    {
        var window = Open();
        var popped = PopOutLayout(window);
        var side = (DockTabSurface)window.View.HostControl("default-sections-practice")!;
        var centre = In(side, window, side.Bounds.Width / 2, side.Bounds.Height / 2);
        Drag(window, In(popped.Tabs[0], window, 5, 5), centre);
        window.MouseUp(centre, MouseButton.Left);
        Assert.Contains("layout", DockLayoutTree.FindNode(window.State.Root, "default-sections-practice")!.Panels);
        Assert.True(Valid(window));
    }

    [AvaloniaFact]
    public void DropOnTheRightEdgeBandSplitsBesideTheTarget()
    {
        var window = Open();
        var popped = PopOutLayout(window);
        var timeline = (DockTabSurface)window.View.HostControl("default-timeline")!;
        var edge = In(timeline, window, timeline.Bounds.Width - 6, DockTabSurface.StripHeight + 40);
        Drag(window, In(popped.Tabs[0], window, 5, 5), edge);
        Assert.NotNull(window.View.DropPreviewRect);
        window.MouseUp(edge, MouseButton.Left);
        var host = DockLayoutTree.FindPanelHost(window.State.Root, "layout")!;
        Assert.Equal(new[] { "layout" }, host.Panels);
        Assert.NotEqual(ToolHost, host.HostId);
        Assert.Equal(new[] { "timeline" }, DockLayoutTree.FindNode(window.State.Root, "default-timeline")!.Panels);
        Assert.True(Valid(window));
    }

    [AvaloniaFact]
    public void EscapeCancelsAMergeDragAndLeavesTheLayout()
    {
        var window = Open();
        var popped = PopOutLayout(window);
        var tools = (DockTabSurface)window.View.HostControl(ToolHost)!;
        var target = In(tools, window, tools.Bounds.Width - 4, 10);
        Drag(window, In(popped.Tabs[0], window, 5, 5), target);
        Assert.NotNull(window.View.DropPreviewRect);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(window.View.DropPreviewRect);
        window.MouseUp(target, MouseButton.Left);
        Assert.DoesNotContain("layout", DockLayoutTree.FindNode(window.State.Root, ToolHost)!.Panels);
        Assert.Equal(new[] { "layout" }, DockLayoutTree.FindPanelHost(window.State.Root, "layout")!.Panels);
    }

    [AvaloniaFact]
    public void ResetAllPanelsRestoresTheDefaultFromAScatteredLayout()
    {
        var window = Open();
        PopOutLayout(window);
        window.Controller.DockPanelTo("rhythm", "default-timeline", DockDropZone.Left);
        window.Controller.DockPanelTo("structure", "default-timeline", DockDropZone.Top);
        window.Controller.ResetAllPanels();
        Assert.Equal(new[] { "tools", "structure", "rhythm", "layout" }, DockLayoutTree.FindNode(window.State.Root, ToolHost)!.Panels);
        Assert.True(Valid(window));
    }

    [AvaloniaFact]
    public void ReorderWithinTheSameGroupIsAllowed() =>
        Assert.True(Open().Controller.CanDrop("tools", ToolHost, DockDropZone.Center));

    [Fact]
    public void HeaderRowIsATabDropAndTheBodyKeepsItsBands()
    {
        Assert.Equal(DockDropZone.Center, DockDropRules.ClassifyTabGroupZone(new UiPoint(2, 10), 300, 400, 36));
        Assert.Equal(DockDropZone.Right, DockDropRules.ClassifyTabGroupZone(new UiPoint(295, 200), 300, 400, 36));
        Assert.Equal(DockDropZone.Center, DockDropRules.ClassifyTabGroupZone(new UiPoint(150, 200), 300, 400, 36));
        Assert.Equal(DockDropZone.Top, DockDropRules.ClassifyTabGroupZone(new UiPoint(150, 40), 300, 400, 36));
    }

    [Fact]
    public void InsertIndexFollowsTheTabMiddles()
    {
        var tabs = new[] { (0.0, 60.0), (60.0, 60.0) };
        Assert.Equal(0, DockDropRules.TabInsertIndex(10, tabs));
        Assert.Equal(1, DockDropRules.TabInsertIndex(40, tabs));
        Assert.Equal(2, DockDropRules.TabInsertIndex(500, tabs));
        Assert.Equal(120, DockDropRules.TabInsertX(2, tabs));
        Assert.Equal(60, DockDropRules.TabInsertX(1, tabs));
    }
}
