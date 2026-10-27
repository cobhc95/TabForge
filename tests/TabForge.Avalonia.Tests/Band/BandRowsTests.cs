using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the rows of a Band pane: the snap of a dragged height to whole rows and its limits, the right-click menu, the count saved with the song
/// (round trip, an old file reading as the default), one undo step, the other panes and the scroll extent, and the follow staying inside a resized pane.
/// Tests: BandRowRules, BandView (BandView.Rows.cs), BandTrackPanel, BandRowsMenu, SongProject.BandPaneRows.
/// </summary>
public sealed class BandRowsTests
{
    private static SongDocument DemoDocument() => new(() => null) { Project = DemoSongFactory.Create() };

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static (BandWindow Window, BandView View, BandTrackPanel Panel, SongDocument Document) Show()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        Settle(window);
        return (window, window.View, window.View.PanelFor(window.View.ShownTrackIndices[0])!, document);
    }

    // Drags the pane bottom edge until the pointer is the given height below the pane top.
    private static void DragEdgeTo(BandWindow window, BandTrackPanel panel, double height)
    {
        var grip = panel.GetVisualDescendants().OfType<BandFretboardGrip>().Single(g => g.Name == BandTrackPanel.CardGripName);
        var centre = new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2);
        var from = grip.TranslatePoint(centre, window)!.Value;
        var to = new Point(from.X, from.Y + height - grip.TranslatePoint(centre, panel)!.Value.Y);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
    }

    private static Guid IdOf(SongDocument document, BandTrackPanel panel) => document.Project.Tracks[panel.TrackIndex].Id;

    [Fact]
    public void SnapPicksTheMostWholeRowsThatFitWithinTheLimits()
    {
        double Height(int rows) => 100 * rows + 20;
        Assert.Equal(2, BandRowRules.Snap(Height(3) - 1, Height, 10));
        Assert.Equal(3, BandRowRules.Snap(Height(3), Height, 10));
        Assert.Equal(BandRowRules.MinRows, BandRowRules.Snap(-50, Height, 10));
        Assert.Equal(BandRowRules.MaxRows, BandRowRules.Snap(100000, Height, 10));
        Assert.Equal(4, BandRowRules.Snap(100000, Height, 4));
    }

    [AvaloniaFact]
    public void DraggingTheEdgeSnapsToWholeRowsAndIsOneUndoStep()
    {
        var (window, _, panel, document) = Show();
        document.MarkClean();
        var before = panel.SystemCount;
        var target = before == 3 ? 2 : 3;
        DragEdgeTo(window, panel, panel.NaturalHeight(target) + 3);
        Assert.Equal(target, panel.SystemCount);
        Assert.InRange(panel.Bounds.Height, panel.NaturalHeight(target) - 2, panel.NaturalHeight(target) + 2);   // whole rows: no row is cut
        Assert.Equal(target, BandRowRules.RowsOf(document.Project, IdOf(document, panel)));
        Assert.True(document.IsDirty);
        Assert.NotNull(DocumentEdits.Undo(document));
        Settle(window);
        Assert.Equal(before, panel.SystemCount);
        Assert.Null(BandRowRules.RowsOf(document.Project, IdOf(document, panel)));
        window.Close();
    }

    [AvaloniaFact]
    public void DraggingFarKeepsTheMaximumAndFarUpKeepsOneRow()
    {
        var (window, _, panel, document) = Show();
        DragEdgeTo(window, panel, -100000);
        Assert.Equal(BandRowRules.MinRows, panel.SystemCount);
        DragEdgeTo(window, panel, 100000);
        Assert.InRange(panel.SystemCount, 2, BandRowRules.MaxRows);
        Assert.Equal(panel.SystemCount, BandRowRules.RowsOf(document.Project, IdOf(document, panel)));
        window.Close();
    }

    [AvaloniaFact]
    public void TheHeaderMenuListsTheCountsChecksTheCurrentOneAndPicksDirectly()
    {
        var (window, _, panel, document) = Show();
        var header = panel.GetVisualDescendants().OfType<Grid>().Single(g => g.ContextMenu?.Items.Count == BandRowRules.MaxRows);
        var menu = header.ContextMenu!;
        menu.Open(header);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(BandRowRules.MaxRows, items.Count);
        Assert.Equal(panel.SystemCount, items.Single(i => i.IsChecked).Tag);
        var pick = items.First(i => (int)i.Tag! != panel.SystemCount);
        pick.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);
        Assert.Equal((int)pick.Tag!, panel.SystemCount);
        Assert.Equal(panel.SystemCount, BandRowRules.RowsOf(document.Project, IdOf(document, panel)));
        window.Close();
    }

    [AvaloniaFact]
    public void OtherPanesKeepTheirRowsAndTheViewScrollsWhenTaller()
    {
        var (window, view, panel, _) = Show();
        var others = view.ShownTrackIndices.Select(view.PanelFor).Where(p => p != panel).Select(p => (Pane: p!, Rows: p!.SystemCount, p.Bounds.Height)).ToList();
        var extent = view.Extent.Height;
        DragEdgeTo(window, panel, panel.NaturalHeight(BandRowRules.MaxRows));
        foreach (var other in others)
        {
            Assert.Equal(other.Rows, other.Pane.SystemCount);
            Assert.Equal(other.Height, other.Pane.Bounds.Height, 1);
        }
        Assert.True(view.Extent.Height >= extent);
        window.Close();
    }

    [AvaloniaFact]
    public void FollowStaysInsideTheResizedPane()
    {
        var (window, view, panel, _) = Show();
        DragEdgeTo(window, panel, panel.NaturalHeight(3) + 3);
        var offset = view.Offset;
        var track = panel.Staff.Probe.Track!;
        panel.ShowSystemsAround(track.Measures.Count - 1);
        Settle(window);
        Assert.Equal(offset, view.Offset);
        var systems = panel.Staff.Probe.Layout.GetLayout(track).SystemCount;
        var expected = BandTrackPanel.SystemWindowFromFirst(panel.Staff, systems, panel.SystemCount)!.Value;
        Assert.Equal(expected.Height, panel.WindowHeight, 1);
        Assert.Equal(expected.Top, panel.WindowTop, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCountRoundTripsThroughTheFileAndAnOldFileHasTheDefault()
    {
        var (window, _, panel, document) = Show();
        DragEdgeTo(window, panel, panel.NaturalHeight(3) + 3);
        var id = IdOf(document, panel);
        var folder = Path.Combine(Path.GetTempPath(), "tf-bandrows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "song.tforge");
            ProjectService.Save(path, document.Project);
            Assert.NotNull(BandRowRules.RowsOf(ProjectService.Load(path), id));
            Assert.Equal(BandRowRules.RowsOf(document.Project, id), BandRowRules.RowsOf(ProjectService.Load(path), id));
            using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            var json = JsonNode.Parse(gzip)!.AsObject();
            Assert.True(json.Remove(nameof(TabForge.Models.SongProject.BandPaneRows)));
            var old = Path.Combine(folder, "old.tforge");
            File.WriteAllText(old, json.ToJsonString());
            Assert.Null(BandRowRules.RowsOf(ProjectService.Load(old), id));
        }
        finally { Directory.Delete(folder, true); }
        window.Close();
    }
}
