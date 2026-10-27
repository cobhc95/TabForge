using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
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
/// Owns: arming a track in the real main window with a cramped timeline pane shows the armed sub-row (the pane grows just enough).
/// Does not own: the fit maths (DockSizeRules), the fit itself (MainWindowTrackListFit), the armed row's drawing (ArmedRowPainter).
/// Tests: TabForge.Avalonia.Tests.TrackArmGrowTests.
/// </summary>
public class TrackArmGrowTests
{
    private const double Slack = 3;

    [AvaloniaFact]
    public void ArmingATrackInACrampedPaneGrowsItToShowTheArmedRow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-arm-grow-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            settings.Settings.Timeline.AutoFitTrackList = false;   // auto-fit would not grow the pane, so only the arm growth can show the armed row
            var layout = DockWorkspaceController.DefaultLayout();
            SplitAbove(layout.Root, "timeline")!.Ratio = 0.9;
            settings.SaveLayout(layout);
            var project = DemoSongFactory.Create();
            while (project.Tracks.Count > 1) project.Tracks.RemoveAt(project.Tracks.Count - 1);
            var main = new MainWindow(settings, first: new SongDocument(() => null) { Project = project }) { Width = 1600, Height = 1000 };
            main.Show();
            Settle(main);
            var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
            var before = arrangement.Bounds.Height;

            project.Tracks[0].RecordArm = true;   // what the record cell does; the pane is too short to click the cell, so its event is raised directly
            var raise = (Action<int>)arrangement.Input.GetType().GetField("TrackArmToggled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(arrangement.Input)!;
            raise(0);
            Settle(main);

            Assert.True(project.Tracks[0].RecordArm);
            Assert.True(before < arrangement.NaturalHeight - Slack, $"the pane started cramped ({before} of {arrangement.NaturalHeight})");
            Assert.True(arrangement.Bounds.Height >= arrangement.NaturalHeight - Slack,
                $"pane {arrangement.Bounds.Height} of natural {arrangement.NaturalHeight}");
            Assert.Equal(0, arrangement.Timeline.MaxRowScrollOffset, 3);
            main.Close();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static DockNodeState? SplitAbove(DockNodeState? node, string panelId)
    {
        if (node is null || node.Kind != "split") return null;
        if (node.Orientation == "Vertical" && node.Second is { Kind: "tabs" } lower && lower.Panels.Contains(panelId, StringComparer.Ordinal)) return node;
        return SplitAbove(node.First, panelId) ?? SplitAbove(node.Second, panelId);
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); main.UpdateLayout(); }
    }
}
