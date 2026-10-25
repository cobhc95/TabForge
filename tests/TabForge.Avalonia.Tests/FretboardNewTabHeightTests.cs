using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard pane's height in the real main window across tabs: a new tab and a switch back to the first tab show the fretboard at the
/// default height, and a splitter move made in one tab is kept when the window goes to the other tab and back.
/// Does not own: the dock's ratio maths (DockSizeRules), the fit of the timeline (TrackListOwnerLayoutTests) or the pane's drawing.
/// Tests: TabForge.Avalonia.Tests.FretboardNewTabHeightTests.
/// </summary>
public sealed class FretboardNewTabHeightTests
{
    private const double Tolerance = 2;
    private const int OwnerTrackCount = 10;

    /// <summary>The window as the owner's launch opens it: a ten-track demo song shown first, so the timeline's fit moves the split beside the fretboard.</summary>
    private static MainWindow Open()
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count < OwnerTrackCount) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = 1500, Height = 960 };
        main.Show();
        Settle(main);
        return main;
    }

    /// <summary>Runs the UI thread's queued jobs and lays out until the window stops changing, as the owner's launch does.</summary>
    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    private static double FretboardHeight(MainWindow main) => main.GetVisualDescendants().OfType<InstrumentPanelStrip>().Single().Bounds.Height;

    /// <summary>Moves the splitter beside the fretboard the way a drag does: the split's ratio changes, and the fit no longer matches it.</summary>
    private static void DragFretboardSplitter(MainWindow main, double ratioChange)
    {
        var split = main.GetVisualDescendants().OfType<DockSplitPanel>().Single(s =>
            s.Node.Orientation == "Vertical" && s.Node.First is { Kind: "tabs" } upper && upper.Panels.Contains("instrument"));
        split.Node.Ratio = Math.Clamp(split.Node.Ratio + ratioChange, 0.05, 0.95);
        split.Rearrange();
        // let go of the splitter as a real drag does (its DragEnded tells the dock the layout was edited)
        var splitter = split.Children.OfType<DockSplitter>().Single();
        var ended = (Action?)typeof(DockSplitter).GetField("DragEnded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(splitter);
        ended?.Invoke();
    }

    [AvaloniaFact]
    public void NewTabShowsTheFretboardAtTheDefaultHeight()
    {
        var main = Open();
        var defaultHeight = FretboardHeight(main);
        Assert.True(Math.Abs(defaultHeight - InstrumentPanelView.PanelHeight) <= Tolerance, $"first tab fretboard {defaultHeight}");

        main.Songs.New();
        Settle(main);
        var newTabHeight = FretboardHeight(main);
        Assert.True(Math.Abs(newTabHeight - InstrumentPanelView.PanelHeight) <= Tolerance, $"new tab fretboard {newTabHeight}");

        main.Songs.Switch(0);
        Settle(main);
        var switchedHeight = FretboardHeight(main);
        Assert.True(Math.Abs(switchedHeight - InstrumentPanelView.PanelHeight) <= Tolerance, $"switched-back fretboard {switchedHeight}");
        main.Close();
    }

    [AvaloniaFact]
    public void FirstTabWithASavedLayoutStartsAtTheSameHeightAsANewTabAndAnOpenedSong()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "fretsize-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var layout = DockWorkspaceController.DefaultLayout();
            var pending = new Stack<DockNodeState>();
            pending.Push(layout.Root!);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node.Orientation == "Vertical" && node.First is { Kind: "tabs" } upper && upper.Panels.Contains("instrument")) node.Ratio = 0.5;
                if (node.First is not null) pending.Push(node.First);
                if (node.Second is not null) pending.Push(node.Second);
            }
            SettingsFileService.SaveAtomic(path, new AppSettings { Workspace = DockWorkspaceRules.Capture(layout) });
            var settings = MainWindowSettings.Open(path);
            var main = new MainWindow(settings, first: new SongDocument(() => null) { Project = DemoSongFactory.Create() }) { Width = 1500, Height = 960 };
            main.Show();
            Settle(main);
            Assert.True(Math.Abs(FretboardHeight(main) - InstrumentPanelView.PanelHeight) <= Tolerance, $"first tab fretboard {FretboardHeight(main)}");

            main.Songs.New();
            Settle(main);
            Assert.True(Math.Abs(FretboardHeight(main) - InstrumentPanelView.PanelHeight) <= Tolerance, $"new tab fretboard {FretboardHeight(main)}");
            main.Close();
            settings.Dispose();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public void AUserResizeInOneTabIsKeptWhenTheWindowGoesToAnotherTabAndBack()
    {
        var main = Open();
        main.Songs.New();
        Settle(main);
        DragFretboardSplitter(main, 0.12);
        Settle(main);
        var resized = FretboardHeight(main);
        Assert.True(Math.Abs(resized - InstrumentPanelView.PanelHeight) > 10, $"the drag did not resize the fretboard: {resized}");

        main.Songs.Switch(0);
        Settle(main);
        Assert.True(Math.Abs(FretboardHeight(main) - InstrumentPanelView.PanelHeight) <= Tolerance, $"the other tab's fretboard {FretboardHeight(main)}");

        main.Songs.Switch(1);
        Settle(main);
        Assert.True(Math.Abs(FretboardHeight(main) - resized) <= Tolerance, $"the resized tab's fretboard {FretboardHeight(main)}, was {resized}");
        main.Close();
    }

    [AvaloniaFact]
    public void ASplitterHeightDraggedThisSessionSurvivesAWindowResize()
    {
        var main = Open();
        DragFretboardSplitter(main, 0.12);
        Settle(main);
        var dragged = FretboardHeight(main);
        Assert.True(Math.Abs(dragged - InstrumentPanelView.PanelHeight) > 10, $"the drag did not resize the fretboard: {dragged}");

        main.Height += 40;
        Settle(main);
        Assert.True(Math.Abs(FretboardHeight(main) - dragged) <= Tolerance, $"the fretboard {FretboardHeight(main)} after a resize, was {dragged}");
        main.Close();
    }
}
