using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: leaving the Band view into compact mode: closing the Band panel by hand (its tab's Close or the Panels menu) puts back the score and the
/// fretboard the Band layout replaced, and a dock split takes a pane's height limit into account when the pane sets it after the first arrange
/// (so no half is left taller than its content, with empty space under it).
/// Does not own: the Band layout and its memory (MainWindowBandView), the split maths (DockSplitPanel, DockSizeRules) or compact mode's own exit.
/// Tests: MainWindowBandView, DockWorkspaceView, DockSplitPanel.
/// </summary>
public class BandExitIntoCompactTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-band-exit-" + Guid.NewGuid().ToString("N"));

    public BandExitIntoCompactTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    private static MenuItem Row(MainWindow main, string commandId) =>
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId);

    private static void Pick(MainWindow main, string commandId)
    {
        Row(main, commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private bool EditorShown(MainWindow main) => main.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "DockEditor");

    [AvaloniaFact]
    public void ClosingTheBandPaneByHandInCompactModeBringsTheScoreAndFretboardBack()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(settings, first: song) { Width = 1400, Height = 900 };
        main.Show();
        Settle(main);
        Assert.True(EditorShown(main));
        Pick(main, "View.CompactMode");
        Pick(main, "View.BandView");
        Settle(main);
        Assert.False(EditorShown(main), "the Band layout replaced the score");

        // The Band pane closed by hand (its tab's Close or View > Panels): the panels the Band layout replaced come back.
        main.DockWorkspace.SetPanelVisible("band", false);
        main.DockWorkspace.FlushLayoutChanged();
        Settle(main);

        Assert.True(EditorShown(main), "the score did not come back after the Band pane was closed by hand");
        Assert.True(main.DockWorkspace.IsPanelVisible("instrument"), "the fretboard pane did not come back");
        // Compact mode is still on, so its side panels stay hidden.
        Assert.False(main.DockWorkspace.IsPanelVisible("tools"));
        song.MarkClean();
        main.Close();
    }

    /// <summary>A split whose lower half holds a pane that only learns its height limit after the first layout pass (as the arrangement does).</summary>
    [AvaloniaFact]
    public void ASplitShrinksAHalfWhenItsPaneSetsItsMaximumHeightAfterTheFirstArrange()
    {
        // The tab host's own chrome (its border; a lone pane hides the tab strip) plus the layout's rounding.
        const double HostChromeAndRounding = 4;
        var top = new Border();
        var pane = new Border();
        var view = new DockWorkspaceView(id => id == "timeline" ? pane : top);
        var window = new Window { Width = 400, Height = 400, Content = view };
        window.Show();
        view.Show(new DockWorkspaceState
        {
            Root = new DockNodeState
            {
                Kind = "split", Orientation = "Vertical", Ratio = 0.2,
                First = new DockNodeState { Kind = "tabs", Panels = { "band" } },
                Second = new DockNodeState { Kind = "tabs", Panels = { "timeline" } },
            },
        });
        window.UpdateLayout();
        var surface = view.GetVisualDescendants().OfType<DockTabSurface>().Single(s => s.Node.Panels.Contains("timeline"));
        Assert.True(surface.Bounds.Height > 250, $"the split started at its ratio: {surface.Bounds.Height}");

        pane.MaxHeight = 100;
        window.UpdateLayout();

        Assert.True(surface.Bounds.Height <= 100 + HostChromeAndRounding, $"the pane's half kept {surface.Bounds.Height} for a 100-tall pane");
        window.Close();
    }
}
