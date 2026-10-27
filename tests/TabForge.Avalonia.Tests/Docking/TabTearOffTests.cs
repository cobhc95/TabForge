using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: tab drags across windows: a dock tab dropped outside floats at the drop point (with the drag ghost shown on the way),
/// a floating tab dragged back docks, Escape cancels; a song tab torn off opens a new window holding it, a lone tab moves its window,
/// and the last song moved to another window closes the source and leaves the window registry. Ghost renders go to
/// %TEMP%\tf-avalonia-tests\dock.
/// Tests: DockPaneDragFlow, DockDragGhost, DocumentTabDragFlow, TabWindowRegistry, TabStripView (drag), MainWindowTabs.Detach.
/// </summary>
public class TabTearOffTests
{
    private static DockPreviewWindow OpenDock(bool light = false)
    {
        var window = new DockPreviewWindow { RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        return window;
    }

    private static Point CentreOf(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

    private static void PressAndDrag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 10, from.Y + 10), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaneDraggedOutsideShowsTheGhostAndFloatsAtTheDropPoint(bool light)
    {
        var window = OpenDock(light);
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var outside = new Point(window.Bounds.Width + 150, 120);
        PressAndDrag(window, CentreOf(surface.Tabs[2], window), outside);

        var ghost = window.Controller.DragGhost;
        Assert.NotNull(ghost);
        Assert.Equal(DockPaneTable.Rows.First(r => r.Id == "rhythm").PaneTitle, ghost!.Text);
        Assert.Null(window.View.DropPreviewRect);
        var dir = Directory.CreateDirectory(Path.Combine(DialogTestKit.PngDirectory, "dock")).FullName;
        using (var frame = ghost.CaptureRenderedFrame()) frame!.Save(Path.Combine(dir, "drag-ghost-" + (light ? "light" : "dark") + ".png"));

        window.MouseUp(outside, MouseButton.Left);
        Assert.Null(window.Controller.DragGhost);
        var floating = Assert.Single(window.State.Floating);
        Assert.Equal(new[] { "rhythm" }, DockWorkspaceState.EnumeratePanelIds(floating.Root));
        var screen = window.PointToScreen(outside);
        Assert.Equal(screen.X / window.RenderScaling, floating.Left, 3);
        Assert.Equal(screen.Y / window.RenderScaling, floating.Top, 3);
        Assert.True(window.Controller.FloatingWindows[floating.Id].IsVisible);
    }

    [AvaloniaFact]
    public void FloatingTabDraggedBackOverTheMainWindowShowsThePreviewAndDocks()
    {
        var window = OpenDock();
        Assert.True(window.Controller.FloatPanelAt("rhythm", new Point(window.Bounds.Width + 300, 50)));
        var floating = window.State.Floating.Single();
        var floatWindow = window.Controller.FloatingWindows[floating.Id];
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();   // lay the new floating window out before reading its tab
        var surface = (DockTabSurface)floatWindow.View.HostControl(floating.Root!.HostId)!;
        var timeline = window.View.HostControl("default-timeline")!;
        var overTimeline = window.PointToScreen(timeline.TranslatePoint(new Point(20, timeline.Bounds.Height / 2), window)!.Value);
        var target = floatWindow.PointToClient(overTimeline);

        PressAndDrag(floatWindow, CentreOf(surface.Tabs[0], floatWindow), target);
        Assert.NotNull(window.View.DropPreviewRect);
        Assert.Null(window.Controller.DragGhost);
        floatWindow.MouseUp(target, MouseButton.Left);

        Assert.Empty(window.State.Floating);
        Assert.Empty(window.Controller.FloatingWindows);
        Assert.Equal(new[] { "rhythm" }, DockLayoutTree.FindPanelHost(window.State.Root, "rhythm")!.Panels);
    }

    [AvaloniaFact]
    public void EscapeCancelsADragOutsideTheWindow()
    {
        var window = OpenDock();
        var surface = (DockTabSurface)window.View.HostControl("default-tool-palette")!;
        var outside = new Point(window.Bounds.Width + 150, 120);
        PressAndDrag(window, CentreOf(surface.Tabs[2], window), outside);
        Assert.NotNull(window.Controller.DragGhost);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(window.Controller.DragGhost);
        window.MouseUp(outside, MouseButton.Left);
        Assert.Empty(window.State.Floating);
        Assert.Contains("rhythm", DockLayoutTree.FindNode(window.State.Root, "default-tool-palette")!.Panels);
    }

    [AvaloniaFact]
    public void SongTabDraggedBeyondTheToleranceOpensANewWindowHoldingIt()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var moving = source.Songs.Songs[1];
        var tab = source.TabStrip.Tabs[1];
        var below = new Point(CentreOf(tab, source).X, source.TabStrip.Bounds.Height + 200);
        PressAndDrag(source, CentreOf(tab, source), below);
        Assert.True(source.TabDrag.IsDragging, "the tab drag started");
        Assert.Equal(2, source.Windows.Windows.Count);   // the new window exists while the drag runs
        Assert.Same(moving, Assert.Single(source.Windows.Windows[1].Songs.Songs));
        Assert.False(source.TabStrip.Tabs[1].IsVisible);   // the source tab is hidden until release
        source.MouseUp(below, MouseButton.Left);

        Assert.Equal(2, source.Windows.Windows.Count);
        var torn = source.Windows.Windows[1];
        Assert.True(torn.IsVisible);
        Assert.Same(moving, Assert.Single(torn.Songs.Songs));
        Assert.DoesNotContain(moving, source.Songs.Songs);
        Assert.Single(source.Songs.Songs);
        Assert.Same(source.Windows, torn.Windows);
    }

    [AvaloniaFact]
    public void EscapeCancelsASongTabDrag()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var below = new Point(CentreOf(source.TabStrip.Tabs[1], source).X, source.TabStrip.Bounds.Height + 200);
        PressAndDrag(source, CentreOf(source.TabStrip.Tabs[1], source), below);
        source.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(source.TabDrag.IsDragging);
        source.MouseUp(below, MouseButton.Left);
        Assert.Single(source.Windows.Windows);
        Assert.Equal(2, source.Songs.Songs.Count);
    }

    [AvaloniaFact]
    public void ALoneTabTornOffMovesItsWindow()
    {
        var source = new MainWindow();
        source.Show();
        var torn = source.TabDrag.TearOff(0, new PixelPoint(400, 300));
        Assert.Same(source, torn);
        Assert.Equal(new PixelPoint(400, 300), source.Position);
        Assert.Single(source.Windows.Windows);
    }

    [AvaloniaFact]
    public void MovingTheLastTabToAnotherWindowClosesTheSourceAndUpdatesTheRegistry()
    {
        var source = new MainWindow();
        source.Show();
        var target = new MainWindow(windows: source.Windows);
        target.Show();
        var changes = 0;
        source.Windows.Changed += () => changes++;
        var moving = source.Songs.Active;

        source.TabDrag.MoveTo(0, target, 1);

        Assert.False(source.IsVisible);
        Assert.Same(target, Assert.Single(source.Windows.Windows));
        Assert.Equal(1, changes);
        Assert.Equal(2, target.Songs.Songs.Count);
        Assert.Same(moving, target.Songs.Active);
    }

    [AvaloniaFact]
    public void DroppingALoneTabOverAnotherWindowsStripMergesItAndClosesTheEmptySource()
    {
        // Both windows carry a playback session, as the app does: closing the emptied source reads the shown song through it.
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine());
        var source = new MainWindow(playback: sessions);
        source.Show();
        var target = new MainWindow(windows: source.Windows, playback: sessions);
        target.Show();
        target.Position = new PixelPoint(source.Position.X + 1200, source.Position.Y);
        target.UpdateLayout();
        source.UpdateLayout();
        var moving = source.Songs.Songs[0];
        var onTarget = target.TabStrip.Tabs[0].PointToScreen(new Point(5, target.TabStrip.Tabs[0].Bounds.Height / 2));
        source.TabDrag.Move(0, BelowStrip(source, source.TabStrip.Tabs[0]));

        source.TabDrag.Move(0, onTarget);
        Assert.True(target.TabStrip.IsDropHighlightShown);
        Assert.True(target.TabStrip.IsInsertionMarkerShown);

        source.TabDrag.Drop(0, onTarget);

        Assert.Equal(2, target.Songs.Songs.Count);
        Assert.Same(moving, target.Songs.Songs[0]);
        Assert.Empty(source.Songs.Songs);
        Assert.False(source.IsVisible);
        Assert.Same(target, Assert.Single(source.Windows.Windows));
        Assert.False(target.TabStrip.IsDropHighlightShown);
        Assert.False(target.TabStrip.IsInsertionMarkerShown);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task ClosingTheLastTabWithCloseWindowSettingClosesTheWindowWithPlaybackSessions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-close-last-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var tabs = new TabForge.Services.TabSettings { LastTabClosed = TabForge.Services.LastTabActions.CloseWindow };
            TabForge.Services.SettingsFileService.SaveAtomic(path, new TabForge.Services.AppSettings { Tabs = tabs });
            using var settings = MainWindowSettings.Open(path);
            using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine());
            var window = new MainWindow(settings, playback: sessions);
            window.Show();

            Assert.True(await window.Songs.CloseActive());
            Assert.Empty(window.Songs.Songs);
            Assert.False(window.IsVisible);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void FretboardStripWithNoSongLeftFollowsNoPlaybackAndDoesNotThrow()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine());
        var window = new MainWindow(playback: sessions);
        window.Show();
        var strip = window.GetLogicalDescendants().OfType<TabForge.Avalonia.Platform.InstrumentPanelStrip>().Single();

        window.Songs.Detach(0);
        strip.RebindPlayback();

        Assert.Empty(window.Songs.Songs);
    }

    private static PixelPoint BelowStrip(MainWindow window, Control tab) =>
        window.PointToScreen(new Point(CentreOf(tab, window).X, window.TabStrip.Bounds.Height + 200));

    [AvaloniaFact]
    public void ATornWindowDoesNotActivateDuringTheGesture()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var moving = source.Songs.Songs[1];
        var below = BelowStrip(source, source.TabStrip.Tabs[1]);

        source.TabDrag.Move(1, below);
        var torn = source.Windows.Windows[1];

        Assert.False(torn.ShowActivated, "the torn window must not take the gesture's activation");
        Assert.False(torn.IsActive);

        source.TabDrag.Drop(1, below);

        Assert.Same(moving, Assert.Single(torn.Songs.Songs));
    }

    [AvaloniaFact]
    public void ASecondDragFromTheTornWindowMovesItNormally()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var below = BelowStrip(source, source.TabStrip.Tabs[1]);
        source.TabDrag.Move(1, below);
        source.TabDrag.Drop(1, below);
        var torn = source.Windows.Windows[1];
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        torn.Position = new PixelPoint(0, 0);
        torn.UpdateLayout();
        var tabOrigin = torn.TabStrip.Tabs[0].TranslatePoint(default, torn)!.Value;
        var destination = new PixelPoint(240, 160);

        torn.TabDrag.Move(0, destination);
        Assert.True(torn.TabDrag.IsDragging);
        Assert.Equal(new PixelPoint(destination.X - (int)tabOrigin.X, destination.Y - (int)tabOrigin.Y), torn.Position);

        torn.TabDrag.Drop(0, destination);
        Assert.False(torn.TabDrag.IsDragging);
        Assert.Single(torn.Songs.Songs);
        Assert.Same(torn, source.Windows.Windows[1]);
    }

    [AvaloniaFact]
    public void TornWindowFollowsThePointerByTheSameDistance()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var below = BelowStrip(source, source.TabStrip.Tabs[1]);
        source.TabDrag.Move(1, below);
        var torn = source.Windows.Windows[1];
        var before = torn.Position;

        source.TabDrag.Move(1, new PixelPoint(below.X + 40, below.Y + 25));

        Assert.Equal(new PixelPoint(before.X + 40, before.Y + 25), torn.Position);
        source.TabDrag.Cancel();
    }

    [AvaloniaFact]
    public void DroppingOverAnotherWindowsStripMergesTheSongAtTheSlotAndShowsThePreview()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var target = new MainWindow(windows: source.Windows);
        target.Show();
        target.Position = new PixelPoint(source.Position.X + 1200, source.Position.Y);
        target.UpdateLayout();
        var moving = source.Songs.Songs[1];
        source.TabDrag.Move(1, BelowStrip(source, source.TabStrip.Tabs[1]));
        var torn = source.Windows.Windows.Single(w => w != source && w != target);
        var onTarget = target.TabStrip.Tabs[0].PointToScreen(new Point(5, target.TabStrip.Tabs[0].Bounds.Height / 2));

        source.TabDrag.Move(1, onTarget);
        Assert.True(target.TabStrip.IsDropHighlightShown);
        Assert.True(target.TabStrip.IsInsertionMarkerShown);
        Assert.Equal(0.7, torn.Opacity);

        source.TabDrag.Drop(1, onTarget);
        Assert.False(target.TabStrip.IsDropHighlightShown);
        Assert.False(torn.IsVisible);
        Assert.Equal(2, target.Songs.Songs.Count);
        Assert.Same(moving, target.Songs.Songs[0]);
        Assert.Single(source.Songs.Songs);
        Assert.Equal(2, source.Windows.Windows.Count);
    }

    [AvaloniaFact]
    public void DroppingATornSongBackOnItsOwnStripGivesItBackToTheSourceWindow()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.UpdateLayout();
        var moving = source.Songs.Songs[1];
        source.TabDrag.Move(1, BelowStrip(source, source.TabStrip.Tabs[1]));
        var torn = source.Windows.Windows[1];
        var first = source.TabStrip.Tabs[0];
        var rightOfFirst = first.PointToScreen(new Point(first.Bounds.Width - 5, first.Bounds.Height / 2));

        source.TabDrag.Move(1, rightOfFirst);
        source.TabDrag.Drop(1, rightOfFirst);

        Assert.Single(source.Windows.Windows);
        Assert.False(torn.IsVisible);
        Assert.Equal(2, source.Songs.Songs.Count);
        Assert.Same(moving, source.Songs.Songs[1]);
        Assert.True(source.TabStrip.Tabs[1].IsVisible);
    }

    [AvaloniaFact]
    public void DraggingWithinTheStripReordersTheTabOnRelease()
    {
        var source = new MainWindow();
        source.Show();
        source.Songs.New();
        source.Songs.New();
        source.UpdateLayout();
        var first = source.Songs.Songs[0];
        var last = source.TabStrip.Tabs[2];
        var pastLast = last.PointToScreen(new Point(last.Bounds.Width - 5, last.Bounds.Height / 2));

        source.TabDrag.Move(0, pastLast);
        source.TabDrag.Drop(0, pastLast);

        Assert.Same(first, source.Songs.Songs[2]);
        Assert.Single(source.Windows.Windows);
    }
}
