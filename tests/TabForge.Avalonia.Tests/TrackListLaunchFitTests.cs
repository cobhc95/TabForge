using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track list fit in the real main window at the owner's size (2000 x 1100, the ten-track demo with the Master row): on a launch with a saved
/// workspace layout that gives the timeline a small share, and after a song switch, every track row and the Master row are visible.
/// Does not own: the fit maths (DockSizeRules), the fit controller (MainWindowTrackListFit), the splitter drag (DockSplitPanel).
/// Tests: TabForge.Avalonia.Tests.TrackListLaunchFitTests.
/// </summary>
public class TrackListLaunchFitTests
{
    /// <summary>The share of the vertical split the first half keeps; the timeline is the second half, so this leaves it a tenth of the window.</summary>
    private const double SavedFirstHalfRatio = 0.9;

    /// <summary>Pixels the pane may fall short of its natural height (the splitter rounds to whole pixels).</summary>
    private const double ContentSlack = 2;

    /// <summary>The owner's window size, in DIP.</summary>
    private const double OwnerWidth = 2000;
    private const double OwnerHeight = 1100;

    /// <summary>The owner's session has ten tracks (the demo's five grown to ten).</summary>
    private const int OwnerTrackCount = 10;

    /// <summary>Layout passes before the assertions: the dock lays out, the fit runs, then the split settles.</summary>
    private const int LayoutPasses = 3;

    [AvaloniaFact]
    public void ALaunchWithASavedSplitThatCrampsTheTimelineShowsEveryRowAndTheMasterRow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-track-launch-fit-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            settings.Settings.Timeline.ShowMasterTrack = true;   // hidden by default; this fit check opts in so the Master row is covered
            var layout = DockWorkspaceController.DefaultLayout();
            SplitAbove(layout.Root, "timeline")!.Ratio = SavedFirstHalfRatio;
            settings.SaveLayout(layout);

            var main = OpenWindow(settings, TenTrackSong());
            Settle(main);
            AssertEveryRowVisible(main);
            main.Close();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void SwitchingToAnotherSongShowsEveryRowAndTheMasterRow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-track-switch-fit-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            settings.Settings.Timeline.ShowMasterTrack = true;   // hidden by default; this fit check opts in so the Master row is covered
            var main = OpenWindow(settings, TenTrackSong());
            Settle(main);
            var other = TenTrackSong();
            main.Songs.Open(other);
            Settle(main);
            Assert.Same(other.Project, main.Songs.Active.Project);
            AssertEveryRowVisible(main);
            main.Close();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static MainWindow OpenWindow(MainWindowSettings? settings, SongDocument song)
    {
        var main = new MainWindow(settings, first: song) { Width = OwnerWidth, Height = OwnerHeight };
        main.Show();
        return main;
    }

    /// <summary>Runs the UI thread's queued jobs and lays out until the window settles, as the owner's launch does.</summary>
    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < LayoutPasses; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    private static SongDocument TenTrackSong()
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count < OwnerTrackCount) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        return new SongDocument(() => null) { Project = project };
    }

    private static void AssertEveryRowVisible(MainWindow main)
    {
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        Assert.True(arrangement.Timeline.MasterLaneShown, "the Master row is shown once enabled");
        Assert.Equal(OwnerTrackCount, arrangement.Timeline.RowCount);
        Assert.True(arrangement.Bounds.Height >= arrangement.NaturalHeight - ContentSlack,
            $"timeline pane shows {arrangement.Bounds.Height} of its natural {arrangement.NaturalHeight}");
        Assert.Equal(0, arrangement.Timeline.MaxRowScrollOffset, 3);
    }

    /// <summary>The vertical split whose second half holds the panel, searched from the root; null when none does.</summary>
    private static DockNodeState? SplitAbove(DockNodeState? node, string panelId)
    {
        if (node is null || node.Kind != "split") return null;
        if (node.Orientation == "Vertical" && node.Second is { Kind: "tabs" } lower && lower.Panels.Contains(panelId, StringComparer.Ordinal)) return node;
        return SplitAbove(node.First, panelId) ?? SplitAbove(node.Second, panelId);
    }
}
