using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the dock edits of DockWorkspaceController: the drop in each zone, tab drag with Escape, close and reset, the capture/restore
/// round trip, the debounced LayoutChanged, the tab menu and the splitter clamp. Renders go to %TEMP%\tf-avalonia-tests\DockWorkspaceControllerTests.
/// Tests: DockWorkspaceController, DockPanelMenu, DockPaneSetup, DockTabSurface (drag).
/// </summary>
public class DockWorkspaceControllerTests
{
    // Generated host ids (Guid) differ per run, so they are blanked; named hosts keep their ids.
    private static string Json(DockWorkspaceState state) =>
        Regex.Replace(JsonSerializer.Serialize(state), "\"HostId\":\"[0-9a-f]{32}\"", "\"HostId\":\"generated\"");

    private static DockPreviewWindow Open(bool light = false)
    {
        var window = new DockPreviewWindow { RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        return window;
    }

    private static Point CentreOf(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

    [AvaloniaTheory]
    [InlineData(DockDropZone.Left)]
    [InlineData(DockDropZone.Right)]
    [InlineData(DockDropZone.Top)]
    [InlineData(DockDropZone.Bottom)]
    [InlineData(DockDropZone.Center)]
    public void DropInEachZoneMovesThePanelLikeTheTreeRules(DockDropZone zone)
    {
        var window = Open();
        Assert.True(window.Controller.DockPanelTo("rhythm", "default-timeline", zone));

        var expected = DockLayoutTree.CreateDefaultState();
        DockWorkspaceRules.RemoveEverywhere(expected, "rhythm");
        var target = DockLayoutTree.FindNode(expected.Root, "default-timeline")!;
        if (zone == DockDropZone.Center) DockLayoutTree.AddToTabs(target, "rhythm");
        else
        {
            var newHost = DockLayoutTree.FindPanelHost(window.State.Root, "rhythm")!.HostId;
            var node = DockLayoutTree.Tabs(newHost, "rhythm");
            var orientation = DockDropRules.IsHorizontal(zone) ? "Horizontal" : "Vertical";
            var before = zone is DockDropZone.Left or DockDropZone.Top;
            DockLayoutTree.ReplaceNode(expected, "default-timeline", before
                ? DockLayoutTree.Split(orientation, DockLayoutTree.EdgeFraction, node, DockLayoutTree.CloneNode(target)!)
                : DockLayoutTree.Split(orientation, 1 - DockLayoutTree.EdgeFraction, DockLayoutTree.CloneNode(target)!, node));
        }
        Assert.Equal(Json(expected), Json(window.State));
        Assert.NotNull(window.View.HostControl(DockLayoutTree.FindPanelHost(window.State.Root, "rhythm")!.HostId));
    }

    [AvaloniaFact]
    public void ForbiddenDropsLeaveTheTreeAlone()
    {
        var window = Open();
        var before = Json(window.State);
        Assert.False(window.Controller.DockPanelTo("rhythm", "score-editor", DockDropZone.Center));   // the editor takes no tabs
        Assert.False(window.Controller.DockPanelTo("timeline", "default-timeline", DockDropZone.Left));   // a lone panel on itself
        Assert.False(window.Controller.DockPanelTo("band", "default-timeline", DockDropZone.Left));   // closed by default
        Assert.False(window.Controller.DockPanelTo("rhythm", "no-such-host", DockDropZone.Left));
        Assert.Equal(before, Json(window.State));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraggingATabShowsTheOverlayAndDropsOnRelease(bool light)
    {
        var window = Open(light);
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var timeline = window.View.HostControl("default-timeline")!;
        var from = CentreOf(surface.Tabs[2], window);
        var origin = timeline.TranslatePoint(default, window)!.Value;
        var nearLeftEdge = new Point(origin.X + 20, origin.Y + timeline.Bounds.Height / 2);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X - 30, from.Y + 30));
        window.MouseMove(nearLeftEdge);
        Assert.True(window.Controller.IsDragging);
        var preview = window.View.DropPreviewRect!.Value;
        var host = new UiRect(origin.X, origin.Y, timeline.Bounds.Width, timeline.Bounds.Height);
        Assert.Equal(DockDropRules.PreviewRect(host, DockDropZone.Left, DockLayoutTree.EdgeFraction), preview);
        Shot(window, light ? "drag-light" : "drag-dark");
        window.MouseUp(nearLeftEdge, MouseButton.Left);
        Assert.False(window.Controller.IsDragging);
        Assert.Null(window.View.DropPreviewRect);
        var moved = DockLayoutTree.FindPanelHost(window.State.Root, "rhythm")!;
        Assert.Equal(new[] { "rhythm" }, moved.Panels);
        Assert.DoesNotContain("rhythm", DockLayoutTree.FindNode(window.State.Root, "default-tool-palette")!.Panels);
    }

    [AvaloniaFact]
    public void TabHeaderHoversAndPressSelectsAtOnceWithoutRebuildingContent()
    {
        var window = Open();
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var tab = (Border)surface.Tabs[2];
        var rest = tab.Background;
        var at = CentreOf(tab, window);
        window.MouseMove(at);
        Assert.NotEqual(rest, tab.Background);
        var content = ((Border)((DockPanel)surface.Child!).Children[1]).Child;
        var tabBefore = surface.Tabs[2];
        window.MouseDown(at, MouseButton.Left);   // same dispatcher turn: no jobs run between
        Assert.Equal(tab.Name!.Substring("DockTab_".Length), surface.Selected);
        Assert.Same(tabBefore, surface.Tabs[2]);
        Assert.NotSame(content, ((Border)((DockPanel)surface.Child!).Children[1]).Child);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(CentreOf(surface.Tabs[0], window), MouseButton.Left);
        window.MouseUp(CentreOf(surface.Tabs[0], window), MouseButton.Left);
        Assert.Same(content, ((Border)((DockPanel)surface.Child!).Children[1]).Child);
    }

    [AvaloniaFact]
    public void DisposeDuringAPaneDragClosesTheGhost()
    {
        var window = Open();
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var from = CentreOf(surface.Tabs[2], window);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X - 30, from.Y + 30));
        window.MouseMove(new Point(-50, -50));   // outside the dock: the ghost follows the pointer
        var ghost = window.Controller.DragGhost;
        Assert.NotNull(ghost);

        window.Controller.Dispose();

        Assert.Null(window.Controller.DragGhost);
        Assert.False(ghost!.IsVisible);
        Assert.False(window.Controller.IsDragging);
    }

    [AvaloniaFact]
    public void EscapeCancelsTheDrag()
    {
        var window = Open();
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var timeline = window.View.HostControl("default-timeline")!;
        var from = CentreOf(surface.Tabs[2], window);
        var over = timeline.TranslatePoint(new Point(20, timeline.Bounds.Height / 2), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        var before = Json(window.State);   // the press selects the tab at once; the cancelled drag changes nothing more
        window.MouseMove(new Point(from.X - 30, from.Y + 30));
        window.MouseMove(over);
        Assert.NotNull(window.View.DropPreviewRect);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(window.Controller.IsDragging);
        Assert.Null(window.View.DropPreviewRect);
        window.MouseMove(new Point(over.X + 5, over.Y));
        Assert.Null(window.View.DropPreviewRect);   // the rest of the press does not start again
        window.MouseUp(over, MouseButton.Left);
        Assert.Equal(before, Json(window.State));
    }

    [AvaloniaFact]
    public void HideThenResetRestoresThePanel()
    {
        var window = Open();
        var controller = window.Controller;
        Assert.True(controller.ClosePanel("rhythm"));
        Assert.DoesNotContain("rhythm", DockWorkspaceRules.PanelsOf(window.State));
        Assert.Contains("rhythm", window.State.ClosedPanels);
        controller.ResetPanel("rhythm");
        Assert.Contains("rhythm", DockWorkspaceRules.PanelsOf(window.State));
        Assert.DoesNotContain("rhythm", window.State.ClosedPanels);
        Assert.Contains("rhythm", DockLayoutTree.FindNode(window.State.Root, "default-tool-palette")!.Panels);
    }

    [AvaloniaFact]
    public void ResetAllReturnsTheDefaultLayout()
    {
        var window = Open();
        window.Controller.DockPanelTo("rhythm", "default-timeline", DockDropZone.Bottom);
        window.Controller.ResetAllPanels();
        Assert.Equal(Json(DockWorkspaceController.DefaultLayout()), Json(window.State));
        Assert.NotNull(window.View.HostControl("default-timeline"));
    }

    [AvaloniaFact]
    public void CaptureThenRestoreGivesTheSameState()
    {
        var window = Open();
        window.Controller.DockPanelTo("rhythm", "default-timeline", DockDropZone.Right);
        window.Controller.ClosePanel("lyrics");
        var captured = DockWorkspaceRules.Capture(window.State);
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var again = new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup, captured);
        Assert.Equal(Json(captured), Json(DockWorkspaceRules.Capture(again.State)));
    }

    [AvaloniaFact]
    public void LayoutChangedCarriesTheCaptureOnceAfterTheDelay()
    {
        var window = Open();
        var seen = new List<string>();
        window.Controller.LayoutChanged += state => seen.Add(Json(state));
        window.Controller.DockPanelTo("rhythm", "default-timeline", DockDropZone.Left);
        window.Controller.ClosePanel("lyrics");
        Assert.Empty(seen);
        window.Controller.FlushLayoutChanged();
        window.Controller.FlushLayoutChanged();
        Assert.Equal(new[] { Json(DockWorkspaceRules.Capture(window.State)) }, seen);
    }

    [AvaloniaFact]
    public void TabMenuListsThePresenterEntriesAndRunsThem()
    {
        var window = Open();
        var menu = window.View.MenuOf!("rhythm")!;
        Assert.Equal(new[] { "Reset this panel to default position", "Close panel", DockPanelMenu.FloatLabel, "", "Reset all panels to default positions" },
            menu.Items.Select(i => i is MenuItem m ? (string)m.Header! : "").ToArray());
        Click(menu, "Close panel");
        Assert.Contains("rhythm", window.State.ClosedPanels);
        Click(menu, "Reset this panel to default position");
        Assert.DoesNotContain("rhythm", window.State.ClosedPanels);
        Click(menu, "Reset all panels to default positions");
        Assert.Equal(Json(DockWorkspaceController.DefaultLayout()), Json(window.State));
        Click(menu, DockPanelMenu.FloatLabel);
        Assert.Equal(new[] { "rhythm" }, DockWorkspaceState.EnumeratePanelIds(Assert.Single(window.State.Floating).Root));
        window.Controller.ResetAllPanels();
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        Assert.NotNull(surface.Tabs[0].ContextMenu);
    }

    [AvaloniaFact]
    public void SplitterDragStopsAtTheTreeClamp()
    {
        var window = Open();
        var root = window.State.Root!;
        var available = window.View.Bounds.Height - DockSplitter.BarThickness;
        var y = Math.Round(available * root.Ratio) + 2;
        window.MouseDown(new Point(300, y), MouseButton.Left);
        window.MouseMove(new Point(300, y - 5000), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(300, y - 5000), MouseButton.Left);
        Assert.Equal(0.02, root.Ratio, 6);
    }

    [AvaloniaFact]
    public void SetupRejectsUnknownIdsAndMissingContent()
    {
        var setup = new DockPaneSetup();
        Assert.Throws<ArgumentException>(() => setup.Register("not-a-pane", () => new Border()));
        Assert.Throws<InvalidOperationException>(() => setup.ContentOf("rhythm"));
    }

    private static void Click(ContextMenu menu, string header)
    {
        var item = menu.Items.OfType<MenuItem>().First(i => (string?)i.Header == header);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void Shot(Window w, string name)
    {
        var dir = DialogTestKit.RenderDirectory(nameof(DockWorkspaceControllerTests));
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, name + ".png"));
    }
}
