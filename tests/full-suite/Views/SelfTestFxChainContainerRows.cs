using TabForge.Models;
using TabForge.Plugins;

namespace TabForge;

// Owns: containers in a real FX window: container rows and collapse, the container pane, grouping, dissolving, dragging into and out
//     of a container, an empty container that stays, each as one undo step that undo takes back; a bypassed container's instrument
//     no longer counts as playing.
// Does not own: the container model as data (TestChainContainers), row selection (TestFxChainListSelection).
// Tests: TestFxChainContainerRows.
public static partial class SelfTest
{
    private static void TestFxChainContainerRows()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var track = doc.Project.Tracks[0];
            foreach (var name in new[] { "A", "B", "C", "D" }) track.Rig.Plugins.Add(new PluginSlot { Name = name, Path = "", Format = "VST3" });
            track.Rig.Plugins[0].Type = PluginSlotType.Instrument;
            track.SoundSource = SoundSources.Plugins;
            w.MixerHost.OpenFxChain(track);
            var fx = w.MixerHost.Windows.FxWindowOf(track) ?? throw new InvalidOperationException("the FX window did not open");
            SmSettle();
            var list = fx.ChainList; var boxes = fx.Containers;
            RigPreset Rig() => doc.Project.Tracks[0].Rig;
            string Shape() => CcShape(ChainTree.Tree(Rig()));
            string Rows() => string.Join(" ", Enumerable.Range(0, list.Count).Select(i => list.TagAt(i) is PluginSlot p ? p.Name : "[" + ((ChainContainer)list.TagAt(i)!).Name + "]"));
            var undo0 = doc.Undo.UndoCount;

            // Group B and C: a container row with its two plug-ins under it; the pane shows the container.
            list.Click(1); list.Click(2, shift: true);
            boxes.GroupSelected();
            SmSettle();
            Check("fx containers: grouping two rows makes a container row with them under it, in one undo step, and shows its pane",
                Shape() == "A Container[B C] D" && Rows() == "A [Container] B C D" && doc.Undo.UndoCount == undo0 + 1 && boxes.Shown is { Count: 2 } && boxes.Pane.Visibility == System.Windows.Visibility.Visible, $"{Shape()} / {Rows()} / {doc.Undo.UndoCount - undo0}");
            var box = Rig().Containers![0];

            // Rows across a container edge are not grouped (nothing is pulled out of a container others stay in).
            list.Click(0); list.Click(2, ctrl: true);
            boxes.GroupSelected();
            SmSettle();
            Check("fx containers: rows that do not share a container are not grouped", Shape() == "A Container[B C] D" && doc.Undo.UndoCount == undo0 + 1, Shape());

            // Drag A onto the container row: it lands inside, at the end, in one step.
            list.Click(0);
            list.SimulateDragStart(0);
            list.SimulateDragMove(list.RowCentre(1));
            var target = list.DragTarget;
            list.SimulateDragEnd();
            SmSettle();
            Check("fx containers: dropping a plug-in on a container row puts it inside in one undo step", target.Into == 1 && Shape() == "Container[B C A] D" && doc.Undo.UndoCount == undo0 + 2, $"{target} {Shape()}");

            // The drop zones while D is dragged over "[Container] B C A": the middle of the container row is into it, its top edge in front of
            // it, between two of its plug-ins an indented line, and at its end the pointer's horizontal position chooses inside or behind.
            list.Click(4); list.SimulateDragStart(4);
            ((int Insert, int Into) To, (int Depth, double Left, string Badge) Marks) Probe(double y, double x = 0) { list.SimulateDragMove(y, x); return (list.DragTarget, list.DropMarks); }
            var (rowTop, rowHeight) = (list.RowTop(0), list.RowHeight(0));
            var middle = Probe(rowTop + rowHeight * 0.5);
            var upper = Probe(rowTop + rowHeight * 0.1);
            var between = Probe(list.RowTop(2) + 1, 200);
            var endInside = Probe(list.RowBottom(3) - 2, 200);
            var endBehind = Probe(list.RowBottom(3) - 2, 4);
            list.HandleEscape();
            Check("fx containers: the middle of a container row drops into it, with the badge and no line", middle.To == (0, 0) && middle.Marks.Badge == "Drop inside" && middle.Marks.Depth < 0, $"{middle}");
            Check("fx containers: the top edge of a container row drops in front of it: a full-width line, no container lit", upper.To == (0, -1) && upper.Marks.Depth == 0 && upper.Marks.Badge.Length == 0 && upper.Marks.Left < 8, $"{upper}");
            Check("fx containers: between two plug-ins of a container the line is indented", between.To == (2, -1) && between.Marks.Depth == 1 && between.Marks.Left > upper.Marks.Left + 8, $"{between}");
            Check("fx containers: at a container's end a pointer over its plug-ins drops inside at the end, a pointer left of them behind it",
                endInside.To == (5, 0) && endInside.Marks.Depth == 1 && endInside.Marks.Badge.Length > 0 && endBehind.To == (5, -1) && endBehind.Marks.Depth == 0 && endBehind.Marks.Badge.Length == 0, $"{endInside} / {endBehind}");
            Check("fx containers: Esc ends the drag and changes nothing", !list.IsDragging && Shape() == "Container[B C A] D" && list.DropMarks.Badge.Length == 0, Shape());

            // A bypassed container: the plug-ins inside stop playing and keep their own switches.
            boxes.SetEnabled(box, false);
            SmSettle();
            Check("fx containers: a container's tick bypasses the plug-ins inside in one undo step and keeps their own switches",
                !Rig().Plugins[0].Plays && Rig().Plugins[3].Plays && Rig().Plugins.All(p => p.Enabled) && doc.Undo.UndoCount == undo0 + 3, $"{doc.Undo.UndoCount - undo0}");

            // Collapse hides the children (no undo step); dragging every plug-in out leaves the empty container.
            list.Click(2);
            boxes.ToggleCollapsed(box);
            Check("fx containers: a collapsed container hides its rows", Rows() == "[Container] D" && box.Collapsed && doc.Undo.UndoCount == undo0 + 3, Rows());
            Check("fx containers: collapsing a container that hides the focused plug-in focuses the container's row", ReferenceEquals(list.Focused, box) && boxes.Shown == box, $"{list.FocusedIndex}");
            boxes.ToggleCollapsed(box);
            list.Click(1); list.Click(3, shift: true);
            list.SimulateDragStart(1);
            list.SimulateDragMove(list.RowBottom(list.Count - 1) + 30);
            list.SimulateDragEnd();
            SmSettle();
            Check("fx containers: dragging its plug-ins out leaves the empty container, and they play again", Shape() == "Container[] D B C A" && Rig().Containers is [{ Count: 0 }] && Rig().Plugins.All(p => p.Plays), Shape());
            var saved = System.Text.Json.JsonSerializer.Deserialize<RigPreset>(System.Text.Json.JsonSerializer.Serialize(Rig()))!;
            Check("fx containers: an empty container is saved and read back in its place", CcShape(ChainTree.Tree(saved)) == "Container[] D B C A", CcShape(ChainTree.Tree(saved)));
            boxes.New();
            SmSettle();
            Check("fx containers: New container adds an empty one and focuses it", Rig().Containers is { Count: 2 } && list.Focused is ChainContainer, Shape());
            boxes.Dissolve();
            SmSettle();
            list.Click(list.IndexOf(Rig().Containers![0]));
            boxes.DeleteSelected();
            SmSettle();
            Check("fx containers: dissolve and delete remove the containers and keep the plug-ins outside them", Shape() == "D B C A" && Rig().Containers is null && doc.Undo.UndoCount == undo0 + 7, $"{Shape()} {doc.Undo.UndoCount - undo0}");
            fx.Close();

            // Undo walks back through every step: the undo step holds the containers, not only the plug-in list.
            for (var i = 0; i < 4; i++) Documents.DocumentEdits.Undo(doc);
            Check("fx containers: undo brings back the bypassed container with its plug-ins inside", Shape() == "Container[B C A] D" && Rig().Containers is [{ Enabled: false }], Shape());
            Documents.DocumentEdits.Undo(doc);
            Documents.DocumentEdits.Undo(doc);
            Check("fx containers: undo puts the dragged plug-in back outside the container", Shape() == "A Container[B C] D", Shape());
            Documents.DocumentEdits.Undo(doc);
            Check("fx containers: undo takes the grouping back", Shape() == "A B C D" && Rig().Containers is null, Shape());
        }
        finally { SmCloseWindow(w); }
    }
}
