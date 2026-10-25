using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the FX chain list checks: click, Ctrl and Shift selection, select all, the rubber band, the focused row, the drop rules (FxChainDropMath), the drag
/// of the selected rows (a drop reorders the chain as one undo step, into a container, Esc cancels) and the rows' texts.
/// Does not own: the list, the rows or the drop math (production classes).
/// Tests: this class.
/// </summary>
public class FxChainListTests
{
    private static (FxChainWindow Window, FxChainList List, TabForge.Models.TrackModel Track) Open(int plugins = 3)
    {
        var chain = Enumerable.Range(0, plugins).Select(i => new PluginSlot { Name = $"P{i}", Vendor = i == 0 ? "Acme" : "", Type = i == 0 ? PluginSlotType.Instrument : PluginSlotType.Effect }).ToList();
        var track = FxTestKit.Track(chain);
        var window = FxTestKit.Window(track);
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, FxTestKit.List(window), track);
    }

    [AvaloniaFact]
    public void ClickCtrlAndShiftSelectRowsAndFocusTheLastClicked()
    {
        var (window, list, _) = Open(4);
        list.Click(0);
        list.Click(2, shift: true);
        Assert.Equal(new[] { 0, 1, 2 }, list.SelectedRows);
        list.Click(1, ctrl: true);
        Assert.Equal(new[] { 0, 2 }, list.SelectedRows);
        Assert.Equal(2, list.FocusedIndex);   // toggling another row off leaves the focus where it was
        list.Click(3, ctrl: true);
        Assert.Equal(new[] { 0, 2, 3 }, list.SelectedRows);
        Assert.Equal(3, list.FocusedIndex);
        list.SelectAll();
        Assert.Equal(4, list.SelectedRows.Count);
        Assert.True(list.HandleEscape());
        Assert.Single(list.SelectedRows);
        Assert.False(list.HandleEscape());
        window.Close();
    }

    [AvaloniaFact]
    public void ArrowKeysMoveTheFocusAndShiftExtends()
    {
        var (window, list, _) = Open(4);
        list.Click(0);
        list.Focus();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(1, list.FocusedIndex);
        window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.None, null);
        Assert.Equal(new[] { 1, 2 }, list.SelectedRows);   // from the row the arrow moved to
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        Assert.Equal(4, list.SelectedRows.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void RubberBandSelectsTheRowsItTouchesAndCtrlAdds()
    {
        var (window, list, _) = Open(4);
        list.SimulateBand(list.RowTop(1) + 2, list.RowTop(2) + 2);
        Assert.Equal(new[] { 1, 2 }, list.SelectedRows);
        list.SimulateBandEnd();
        list.SimulateBand(list.RowTop(0) + 2, list.RowTop(0) + 4, add: true);
        Assert.Equal(new[] { 0, 1, 2 }, list.SelectedRows);
        list.SimulateBandEnd();
        window.Close();
    }

    [AvaloniaFact]
    public void RowsShowKindNameAndVendor()
    {
        var (window, list, _) = Open(3);
        var kinds = list.GetLogicalDescendantsOf<TextBlock>().Where(t => t.Name == "RowKind").Select(t => t.Text).ToList();
        Assert.Equal(new[] { "INST", "FX", "FX" }, kinds);
        Assert.Equal("Acme", list.GetLogicalDescendantsOf<TextBlock>().Single(t => t.Name == "RowVendor").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void DropRulesPickIntoBeforeAndEndOfContainer()
    {
        var rows = new[]
        {
            new DropRow(0, 20, 0, false, false),    // a plug-in
            new DropRow(21, 20, 0, true, false),    // a container
            new DropRow(42, 20, 1, false, false),   // a plug-in inside it
            new DropRow(63, 20, 0, false, false),   // a plug-in after it
        };
        Assert.Equal(new DropTarget(1, 1, -1), FxChainDropMath.At(rows, 0, 31));               // the middle of a container: into it
        Assert.Equal(new DropTarget(1, -1, 0), FxChainDropMath.At(rows, 0, 22));               // its top edge: in front of it
        Assert.Equal(new DropTarget(3, 1, 1), FxChainDropMath.At(rows, 40, 60));               // below its last plug-in, right of the indent: at its end
        Assert.Equal(new DropTarget(3, -1, 0), FxChainDropMath.At(rows, 2, 60));               // left of the indent: behind the container
        Assert.Equal(new DropTarget(4, -1, 0), FxChainDropMath.At(rows, 0, 200));              // below everything: the end
        Assert.Equal(new[] { 1, 0, 2 }, FxChainDropMath.Reorder(new[] { 0, 1, 2 }, new[] { 1 }, 0));
    }

    [AvaloniaFact]
    public void DraggingTheSelectedRowsReordersTheChainAsOneUndoStep()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = new FxChainWindow(host, track);
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = FxTestKit.List(window);
        list.SimulateDragStart(0);
        Assert.True(list.IsDragging);
        list.SimulateDragMove(list.RowCentre(2) + 30);
        Assert.Equal(3, list.DragTarget.Insert);
        Assert.Equal(-1, list.DragTarget.Into);
        Assert.True(list.DropMarks.Depth >= 0);
        list.SimulateDragEnd();
        Assert.Equal(new[] { "Sample reverb", "Sample delay", "Sample piano" }, track.Rig.Plugins.Select(p => p.Name));
        Assert.True(document.Undo.CanUndo);
        DocumentEdits.Undo(document);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, document.Project.Tracks[0].Rig.Plugins.Select(p => p.Name));
        Assert.False(document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void ARealMouseDragMovesTheRowAndMovingNothingChangesNothing()
    {
        var (window, list, track) = Open(3);
        var from = list.RowAt(0);
        var start = from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        for (var step = 1; step <= 6; step++) window.MouseMove(new Point(start.X, start.Y + step * 25), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X, start.Y + 150), MouseButton.Left);
        Assert.Equal("P0", track.Rig.Plugins[^1].Name);

        var before = track.Rig.Plugins.Select(p => p.Name).ToList();
        var row = list.RowAt(1);
        var middle = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        Assert.Equal(before, track.Rig.Plugins.Select(p => p.Name));
        Assert.Equal(1, list.FocusedIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void DroppingOntoAContainerPutsTheRowsInsideIt()
    {
        var (window, list, track) = Open(3);
        window.Run(TabForge.Services.Features.FxChainIds.NewContainer);
        var container = (ChainContainer)list.Focused!;
        Assert.Equal(4, list.Count);
        var index = list.IndexOf(container);
        var plugin = list.IndexOf(track.Rig.Plugins[2]);
        list.Click(plugin);
        list.SimulateDragStart(plugin);
        list.SimulateDragMove(list.RowCentre(index));
        Assert.Equal(index, list.DragTarget.Into);
        Assert.Equal("Drop inside", list.DropMarks.Badge);
        list.SimulateDragEnd();
        Assert.Equal(1, track.Rig.Containers![0].Count);
        Assert.Equal("P2", ChainTree.Tree(track.Rig).SelectMany(i => i.Children).Single().Plugin!.Name);
        window.Close();
    }
}

internal static class ListTestExtensions
{
    public static System.Collections.Generic.IEnumerable<T> GetLogicalDescendantsOf<T>(this Control control) where T : Control =>
        global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control).OfType<T>();
}
