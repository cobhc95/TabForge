using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the dock rendering: the default layout's host sizes from its ratios, splitter drags changing the ratio, tab clicks, and the
/// drop preview matching DockDropRules. Renders go to %TEMP%\tf-avalonia-tests\DockWorkspaceViewTests.
/// Tests: DockWorkspaceView, DockSplitPanel, DockSplitter, DockTabSurface, DockPreviewWindow.
/// </summary>
public class DockWorkspaceViewTests
{
    private static DockPreviewWindow Open(bool light)
    {
        var window = new DockPreviewWindow { RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        return window;
    }

    private static Rect BoundsIn(Control host, Visual root) => new(host.TranslatePoint(default, root)!.Value, host.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultLayoutSizesFollowTheRatios(bool light)
    {
        var window = Open(light);
        var view = window.View;
        var width = view.Bounds.Width;
        var height = view.Bounds.Height;
        // Root is a vertical split (timeline below); the upper part is a horizontal split (right column).
        var upperHeight = Math.Round((height - DockSplitter.BarThickness) * 0.74);
        var rightWidth = width - DockSplitter.BarThickness - Math.Round((width - DockSplitter.BarThickness) * 0.79);
        var timeline = BoundsIn(view.HostControl("default-timeline")!, view);
        var tools = BoundsIn(view.HostControl("default-tool-palette")!, view);
        var editor = BoundsIn(view.HostControl("score-editor")!, view);
        Assert.Equal(upperHeight + DockSplitter.BarThickness, timeline.Y);
        Assert.Equal(rightWidth, tools.Width);
        Assert.Equal(width - rightWidth, tools.X, 3);
        Assert.True(editor.Width > 0 && editor.Right < tools.X);
        var surface = Assert.IsType<DockTabSurface>(view.HostControl("default-tool-palette"));
        Assert.Equal(new[] { "Tools", "Structure", "Rhythm", "Layout" }, surface.Tabs.Select(t => ((TextBlock)((Border)t).Child!).Text));
        Shot(window, light ? "default-light" : "default-dark");
    }

    /// <summary>The fretboard row locks and unlocks in place (no rebuild), and a split half is held between its live minimum and maximum.</summary>
    [AvaloniaFact]
    public void FretboardRowLockAndSplitHeightLimitsApplyInPlace()
    {
        var setup = new DockPaneSetup();
        setup.Register("instrument", () => new Border { Name = "Pane_instrument", Height = 120 });
        setup.RegisterEditor(() => new Border());
        setup.RegisterPlaceholdersForTheRest();
        var view = new DockWorkspaceView(setup.ContentOf);
        var controller = new DockWorkspaceController(view, setup);
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        var host = view.HostControl("default-instrument")!;
        var split = Assert.IsType<DockSplitPanel>(host.Parent);
        controller.SetNaturalHeightLock("instrument", true);
        window.UpdateLayout();
        Assert.InRange(host.Bounds.Height, 120, 120 + DockTabSurface.StripHeight + 2);
        controller.SetNaturalHeightLock("instrument", false);
        window.UpdateLayout();
        Assert.Equal(Math.Round(split.AvailableNow * split.Node.Ratio), host.Bounds.Height, 1);

        var node = new DockNodeState { Kind = "split", Orientation = "Vertical", Ratio = 0.5 };
        var upper = new Border();
        var lower = new Border();
        var limited = new DockSplitPanel(node, upper, lower, firstMinimum: () => 150, firstMaximum: () => 300);
        var limitWindow = new Window { Width = 400, Height = 900, Content = limited };
        limitWindow.Show();
        node.Ratio = 0.9;
        limitWindow.UpdateLayout();
        Assert.Equal(300, upper.Bounds.Height, 1);
        node.Ratio = 0.5;
        limitWindow.UpdateLayout();
        var splitterY = upper.Bounds.Height + DockSplitter.BarThickness / 2;
        limitWindow.MouseDown(new Point(200, splitterY), MouseButton.Left);
        limitWindow.MouseMove(new Point(200, splitterY - 400), RawInputModifiers.LeftMouseButton);
        limitWindow.MouseUp(new Point(200, splitterY - 400), MouseButton.Left);
        limitWindow.UpdateLayout();
        Assert.Equal(150, upper.Bounds.Height, 1);
    }

    /// <summary>A drag that loses its pointer capture ends: later moves do not keep resizing.</summary>
    [AvaloniaFact]
    public void SplitterDragEndsWhenCaptureIsLost()
    {
        var node = new DockNodeState { Kind = "split", Orientation = "Vertical", Ratio = 0.5 };
        var upper = new Border();
        var limited = new DockSplitPanel(node, upper, new Border());
        var window = new Window { Width = 400, Height = 900, Content = limited };
        window.Show();
        var splitter = limited.GetVisualDescendants().OfType<DockSplitter>().Single();
        IPointer? pointer = null;
        var ended = 0;
        var moved = 0;
        splitter.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, handledEventsToo: true);
        splitter.DragEnded += () => ended++;
        splitter.DragMoved += _ => moved++;
        var y = upper.Bounds.Height + DockSplitter.BarThickness / 2;
        window.MouseDown(new Point(200, y), MouseButton.Left);
        pointer!.Capture(null);
        window.MouseMove(new Point(200, y + 40));
        Assert.Equal(1, ended);
        Assert.Equal(0, moved);
    }

    [AvaloniaFact]
    public void ClickingATabShowsThatPane()
    {
        var window = Open(false);
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var tab = surface.Tabs[2];
        var point = tab.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal("rhythm", surface.Selected);
        Assert.Equal("rhythm", surface.Node.SelectedPanel);
    }

    [AvaloniaFact]
    public void DraggingASplitterChangesTheRatio()
    {
        var window = Open(false);
        var view = window.View;
        var root = window.State.Root!;
        var available = view.Bounds.Height - DockSplitter.BarThickness;
        var y = Math.Round(available * root.Ratio) + 2;
        var start = new Point(300, y);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(300, y + 50), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(300, y + 50), MouseButton.Left);
        // The second pane (the timeline in this layout) keeps its minimum height: the drag stops there, within the chrome's few DIPs.
        var minimumRatio = 1 - TabForge.Avalonia.Platform.TimelineView.MinimumPaneHeight / available;
        Assert.Equal(Math.Min(0.74 + 50 / available, minimumRatio), root.Ratio, 2);
        var timeline = BoundsIn(view.HostControl("default-timeline")!, view);
        Assert.Equal(Math.Round(available * root.Ratio) + DockSplitter.BarThickness, timeline.Y);
    }

    [AvaloniaTheory]
    [InlineData(DockDropZone.Left)]
    [InlineData(DockDropZone.Bottom)]
    [InlineData(DockDropZone.Center)]
    public void DropPreviewIsTheDockDropRulesRectangle(DockDropZone zone)
    {
        var window = Open(true);
        var view = window.View;
        Assert.Null(view.DropPreviewRect);
        view.ShowDropPreview("default-timeline", zone);
        var host = BoundsIn(view.HostControl("default-timeline")!, view);
        var expected = DockDropRules.PreviewRect(new UiRect(host.X, host.Y, host.Width, host.Height), zone, DockLayoutTree.EdgeFraction);
        Assert.Equal(expected, view.DropPreviewRect);
        if (zone == DockDropZone.Left) Shot(window, "drop-left-light");
        view.HideDropPreview();
        Assert.Null(view.DropPreviewRect);
    }

    private static void Shot(Window w, string name)
    {
        var dir = DialogTestKit.RenderDirectory(nameof(DockWorkspaceViewTests));
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, name + ".png"));
    }
}
