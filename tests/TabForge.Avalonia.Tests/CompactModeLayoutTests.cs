using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the compact mode layout in the main window: the header strip above the panes, the timeline pane fitted to its content height, and both
/// restored on exit.
/// Does not own: the layout snapshot (DockWorkspaceController), the timeline's content height (TimelineView).
/// Tests: MainWindowCompactMode, CompactTrackStrip, ArrangementView.
/// </summary>
public class CompactModeLayoutTests
{
    /// <summary>The slack, in DIP, the timeline pane may exceed its content height by.</summary>
    private const double ContentSlack = 8;

    private static void ToggleCompact(MainWindow main)
    {
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == "View.CompactMode");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static double TopIn(Visual control, Visual window) => control.TranslatePoint(default, window)!.Value.Y;

    [AvaloniaFact]
    public void EnteringCompactModePutsTheStripAboveTheInstrumentAndFitsTheTimelineToItsContent()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        DockTabSurface Pane(string id) => main.GetVisualDescendants().OfType<DockTabSurface>().Single(s => s.Selected == id);
        var timelineBefore = Pane("timeline").Bounds.Height;

        ToggleCompact(main);
        var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        var content = arrangement.Timeline.Height;
        Assert.True(strip.IsVisible);
        Assert.True(TopIn(strip, main) < TopIn(Pane("instrument"), main));
        // The pane holds the stacked transport bar above the timeline, so the fit is the transport plus the timeline's visible height.
        Assert.True(Pane("timeline").Bounds.Height <= arrangement.Transport.Bounds.Height + content + ContentSlack,
            $"timeline pane {Pane("timeline").Bounds.Height} vs transport {arrangement.Transport.Bounds.Height} plus content {content}");
        // The timeline is not cropped: its visible height reaches its compact content height.
        Assert.True(content >= arrangement.Timeline.ContentHeight - ContentSlack, $"timeline shows {content} of {arrangement.Timeline.ContentHeight}");

        ToggleCompact(main);
        Assert.False(strip.IsVisible);
        Assert.Equal(timelineBefore, Pane("timeline").Bounds.Height, 1);
        main.Close();
    }

    /// <summary>The owner's window size, in DIP.</summary>
    private const double OwnerWidth = 2000;
    private const double OwnerHeight = 1100;

    [AvaloniaFact]
    public void EnteringCompactModeKeepsTheFretboardPaneWhereItIs()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-compact-layout-" + System.Guid.NewGuid().ToString("N"));
        try
        {
        // Two launches on one settings file: the fresh default layout, then the saved one the owner's app starts with.
        for (var launch = 0; launch < 2; launch++)
        {
            using var settings = MainWindowSettings.Open(System.IO.Path.Combine(folder, "settings.json"));
            var main = new MainWindow(settings, first: new TabForge.Documents.SongDocument(() => null) { Project = TabForge.Presets.DemoSongFactory.Create() });
            main.Show();
            main.Width = OwnerWidth;
            main.Height = OwnerHeight;
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            DockTabSurface Pane(string id) => main.GetVisualDescendants().OfType<DockTabSurface>().Single(s => s.Selected == id);
            Control Editor() => main.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DockEditor");
            var heightBefore = Pane("instrument").Bounds.Height;

            ToggleCompact(main);
            main.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();
            var stripBottom = TopIn(strip, main) + strip.Bounds.Height;
            // The fretboard stays the top pane, right under the strip, above the score, at its normal height.
            Assert.Equal(stripBottom, TopIn(Pane("instrument"), main), 1);
            Assert.True(TopIn(Pane("instrument"), main) < TopIn(Editor(), main), $"launch {launch}: fretboard at {TopIn(Pane("instrument"), main)}, score at {TopIn(Editor(), main)}");
            Assert.Equal(heightBefore, Pane("instrument").Bounds.Height, 1);
            ToggleCompact(main);
            main.Close();
        }
        }
        finally
        {
            if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void CompactModeShowsTheStripUnderTheTabsWithTheToolbarIconsBeforeItsClose()
    {
        var main = new MainWindow { Width = OwnerWidth, Height = OwnerHeight };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var row = main.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "MainMenuRow");
        var rowTop = TopIn(row, main);
        Control Toggles() => main.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == MenuRowToggles.PanelName);

        ToggleCompact(main);
        main.UpdateLayout();
        var strip = main.GetVisualDescendants().OfType<CompactTrackStrip>().Single();
        Assert.False(row.IsVisible);
        Assert.Equal(rowTop, TopIn(strip, main), 1);   // no empty row above the strip
        Assert.Same(strip, Toggles().Parent);
        Assert.True(TopIn(Toggles(), main) >= TopIn(strip, main) && Toggles().TranslatePoint(default, main)!.Value.X < strip.Bounds.Width - 20);

        ToggleCompact(main);
        main.UpdateLayout();
        Assert.True(row.IsVisible);
        Assert.NotSame(strip, Toggles().Parent);
        main.Close();
    }

    [AvaloniaFact]
    public void LeavingCompactModeFitsTheTrackListToItsRowsAgain()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-compact-fit-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(folder, "settings.json"));
        settings.Settings.Timeline.ShowMasterTrack = true;
        var layout = DockWorkspaceController.DefaultLayout();
        // The owner's launch: the saved split cramps the timeline, and the launch fit replaces it so every row shows.
        SavedSplitAbove(layout.Root, "timeline")!.Ratio = 0.9;
        settings.SaveLayout(layout);
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(settings, first: song) { Width = 2000, Height = 1100 };
        main.Show();
        Settle(main);
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.Bounds.Height >= arrangement.NaturalHeight - 2, "the launch fit did not run");
        var split = arrangement.GetVisualAncestors().OfType<DockSplitPanel>().First();
        // A splitter the user drags: the fit no longer holds, so the next fit must replace it.
        var beforeDrag = arrangement.Bounds.Height;
        Drag(main, split.Splitter, 3000);
        Assert.True(arrangement.Bounds.Height < beforeDrag - 2,
            $"the splitter drag did not shrink the pane ({beforeDrag} -> {arrangement.Bounds.Height})");

        ToggleCompact(main);
        Settle(main);
        Assert.True(System.Math.Abs(arrangement.Bounds.Height - arrangement.NaturalHeight) <= ContentSlack,
            $"compact pane {arrangement.Bounds.Height} vs natural {arrangement.NaturalHeight}");
        ToggleCompact(main);
        Settle(main);

        Assert.True(arrangement.Header.IsVisible, "the track-list column is hidden after compact mode left");
        Assert.True(arrangement.Bounds.Height >= arrangement.NaturalHeight - 2,
            $"the pane shows {arrangement.Bounds.Height} of its natural {arrangement.NaturalHeight} after compact mode left");
        Assert.Equal(0, arrangement.Timeline.MaxRowScrollOffset, 3);
        main.Close();
    }

    [AvaloniaFact]
    public void CompactLeavingWhileTheBandViewStaysAndThenBandLeavingFitsTheTrackList()
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song) { Width = 2000, Height = 1100 };
        main.Show();
        Settle(main);
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        arrangement.ShowMasterLane(true);
        Settle(main);
        var split = arrangement.GetVisualAncestors().OfType<DockSplitPanel>().First();
        var beforeDrag = arrangement.Bounds.Height;
        Drag(main, split.Splitter, 150);
        Assert.True(arrangement.Bounds.Height < beforeDrag - 2,
            $"the splitter drag did not shrink the pane ({beforeDrag} -> {arrangement.Bounds.Height})");

        Pick(main, "View.BandView");
        Pick(main, "View.CompactMode");
        Settle(main);
        Pick(main, "View.BandView");
        Settle(main);
        Pick(main, "View.CompactMode");
        Settle(main);

        Assert.True(arrangement.Header.IsVisible, "the track-list column is hidden after compact mode left");
        Assert.True(arrangement.Bounds.Height >= arrangement.NaturalHeight - 2,
            $"the pane shows {arrangement.Bounds.Height} of its natural {arrangement.NaturalHeight} after compact mode left");
        Assert.Equal(0, arrangement.Timeline.MaxRowScrollOffset, 3);
        main.Close();
    }

    private static void Pick(MainWindow main, string commandId)
    {
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
    }

    private static DockNodeState? SavedSplitAbove(DockNodeState? node, string panelId)
    {
        if (node is null || node.Kind != "split") return null;
        if (node.Orientation == "Vertical" && node.Second is { Kind: "tabs" } lower && lower.Panels.Contains(panelId, System.StringComparer.Ordinal)) return node;
        return SavedSplitAbove(node.First, panelId) ?? SavedSplitAbove(node.Second, panelId);
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    private static void Drag(MainWindow window, DockSplitter splitter, double distance)
    {
        var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, distance / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(start + new Vector(0, distance), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(0, distance), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void ResizingInCompactModeDoesNotCropTheTimeline()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        ToggleCompact(main);

        main.Height = 760;
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();

        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.Timeline.Height >= arrangement.Timeline.ContentHeight - ContentSlack, $"timeline shows {arrangement.Timeline.Height} of {arrangement.Timeline.ContentHeight} after resize");
        main.Close();
    }
}
