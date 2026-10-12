using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Views.Fx;

namespace TabForge;

// Owns: the FX chain list as a selection in a real FX window: click, Ctrl+click, Shift+click, select all, the rubber band, Esc, the
//     focused row; and the selected rows moved together by a drag as one undo step, the bypass tick and Remove acting on all of them.
// Does not own: the window's lifetime (TestFxChainWindow), the chain model's containers (TestChainContainers).
// Tests: TestFxChainListSelection.
public static partial class SelfTest
{
    private static void TestFxChainListSelection()
    {
        Check("fx list: reorder keeps the moved rows together in their order",
            string.Join("", FxChainList.Reorder(new[] { "a", "b", "c", "d", "e" }, new[] { 1, 3 }, 0)) == "bdace"
            && string.Join("", FxChainList.Reorder(new[] { "a", "b", "c", "d", "e" }, new[] { 0, 1 }, 5)) == "cdeab"
            && string.Join("", FxChainList.Reorder(new[] { "a", "b", "c", "d", "e" }, new[] { 1, 2 }, 3)) == "abcde", "");

        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var track = doc.Project.Tracks[0];
            foreach (var name in new[] { "A", "B", "C", "D", "E" }) track.Rig.Plugins.Add(new PluginSlot { Name = name, Path = "", Format = "VST3" });
            w.MixerHost.OpenFxChain(track);
            var fx = w.MixerHost.Windows.FxWindowOf(track) ?? throw new InvalidOperationException("the FX window did not open");
            SmSettle();
            var list = fx.ChainList;
            string Sel() => string.Join("", list.SelectedTags.Cast<PluginSlot>().Select(s => s.Name));
            string Focus() => (list.Focused as PluginSlot)?.Name ?? "-";
            string Order() => string.Join("", doc.Project.Tracks[0].Rig.Plugins.Select(s => s.Name));

            list.Click(1);
            list.Click(3, ctrl: true);
            Check("fx list: click selects one and Ctrl+click adds a row; the row clicked last is the focused one", Sel() == "BD" && Focus() == "D", $"{Sel()} focus {Focus()}");
            list.Click(3, ctrl: true);
            Check("fx list: Ctrl+click on a selected row takes it out and the focus stays on a selected row", Sel() == "B" && Focus() == "B", $"{Sel()} focus {Focus()}");
            list.Click(4, shift: true);
            Check("fx list: Shift+click selects the range from the anchor (the row clicked last without Shift)", Sel() == "DE" && Focus() == "E", $"{Sel()} focus {Focus()}");
            list.SelectAll();
            Check("fx list: select all keeps the focused row", Sel() == "ABCDE" && Focus() == "E", $"{Sel()} focus {Focus()}");
            Check("fx list: Esc clears a multi-selection to the focused row, and does nothing more on one row", list.HandleEscape() && Sel() == "E" && !list.HandleEscape(), Sel());
            list.SimulateBand(list.RowCentre(1), list.RowCentre(2));
            list.SimulateBandEnd();
            Check("fx list: a rubber band selects the rows it touches", Sel() == "BC", Sel());

            // Drag B and D to the end: they move together, in their order, as one undo step; nothing changes until the drop.
            list.Click(1);
            list.Click(3, ctrl: true);
            var undoBefore = doc.Undo.UndoCount;
            list.SimulateDragStart(3);
            list.SimulateDragMove(list.RowBottom(4) + 30);
            var during = Order();
            list.SimulateDragEnd();
            SmSettle();
            Check("fx list: dragging two selected rows moves both to the drop position in one undo step, and only on the drop",
                during == "ABCDE" && Order() == "ACEBD" && doc.Undo.UndoCount == undoBefore + 1 && Sel() == "BD" && Focus() == "D", $"during {during}, after {Order()}, steps {doc.Undo.UndoCount - undoBefore}, sel {Sel()}");
            list.SimulateDragStart(4);
            list.SimulateDragMove(list.RowCentre(0) - 10);
            Check("fx list: Esc cancels a drag", list.HandleEscape() && !list.IsDragging && Order() == "ACEBD" && doc.Undo.UndoCount == undoBefore + 1, Order());

            // The tick of a selected row applies to every selected row, as one step.
            var listBox = LtField<ListBox>(fx, "_list")!;
            var tick = (CheckBox)((Panel)((Border)((ListBoxItem)listBox.Items[3]!).Content).Child).Children[0];
            tick.IsChecked = false;
            tick.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            SmSettle();
            var plugins = doc.Project.Tracks[0].Rig.Plugins;
            Check("fx list: the bypass tick of a selected row bypasses all selected rows in one step",
                !plugins[3].Enabled && !plugins[4].Enabled && plugins[0].Enabled && doc.Undo.UndoCount == undoBefore + 2 && Sel() == "BD", $"{string.Join(",", plugins.Select(p => p.Enabled))} steps {doc.Undo.UndoCount - undoBefore}");

            SmCall(fx, "RemoveSelected");
            SmSettle();
            Check("fx list: Remove removes all selected rows in one step", Order() == "ACE" && doc.Undo.UndoCount == undoBefore + 3 && list.SelectedRows.Count == 1, $"{Order()} steps {doc.Undo.UndoCount - undoBefore}");
            fx.Close();
            Documents.DocumentEdits.Undo(doc); Documents.DocumentEdits.Undo(doc); Documents.DocumentEdits.Undo(doc);
            Check("fx list: undoing the three steps brings the chain back", Order() == "ABCDE" && doc.Project.Tracks[0].Rig.Plugins.All(p => p.Enabled), Order());
        }
        finally { SmCloseWindow(w); }
    }
}
