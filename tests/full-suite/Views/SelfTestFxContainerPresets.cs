using TabForge.Models;
using TabForge.Plugins;

namespace TabForge;

// Owns: container presets in a real FX window: a container saved as a preset and loaded into another container (the same tree and
//     states, new identities), a plug-in that is not trusted left out and listed, the load as one undo step that undo takes back.
// Does not own: the container model (TestChainContainers), the chain file format and its bounds (the FX chain tests).
// Tests: TestFxContainerPresets.
public static partial class SelfTest
{
    private static void TestFxContainerPresets()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var track = doc.Project.Tracks[0];
            foreach (var n in new[] { "A", "B", "C", "D" }) track.Rig.Plugins.Add(new PluginSlot { Name = n, Path = "", Format = "VST3", State = "state of " + n, OutputDb = n == "C" ? -3 : 0 });
            track.Rig.Plugins[0].Type = PluginSlotType.Instrument;
            track.SoundSource = SoundSources.Plugins;
            w.MixerHost.OpenFxChain(track);
            var fx = w.MixerHost.Windows.FxWindowOf(track) ?? throw new InvalidOperationException("the FX window did not open");
            SmSettle();
            var list = fx.ChainList; var boxes = fx.Containers; var presets = boxes.Presets;
            RigPreset Rig() => doc.Project.Tracks[0].Rig;
            string Shape() => CcShape(ChainTree.Tree(Rig()));
            var said = new List<string>();
            presets.Say = said.Add;
            presets.Trusted = _ => true;
            var name = "Round trip " + Guid.NewGuid().ToString("N")[..6];

            list.Click(1); list.Click(2, shift: true);
            boxes.GroupSelected();
            SmSettle();
            var first = Rig().Containers![0];
            first.Mix = 40;
            Check("container presets: a container with plug-ins is saved and listed", presets.Save(name) && PresetFolderStore.Containers().Names().Contains(name) && said.Count == 0, string.Join(" | ", said));

            list.Click(list.Count - 1);
            boxes.New();
            SmSettle();
            var undo0 = doc.Undo.UndoCount;
            Check("container presets: an empty container is shown and saves nothing", boxes.Shown is { Count: 0 } && !presets.Save(name + " empty") && said.Count == 1, Shape());
            said.Clear();
            Check("container presets: loading fills the other container in place, in one undo step", presets.Load(name) && Shape() == "A Container[B C] D Container[B C]" && doc.Undo.UndoCount == undo0 + 1 && said.Count == 0, $"{Shape()} {doc.Undo.UndoCount - undo0}");
            var plugins = Rig().Plugins;
            Check("container presets: the loaded plug-ins carry the saved states and settings under new identities, and the container its mix and preset name",
                plugins.Count == 6 && plugins[4].State == "state of B" && plugins[5].State == "state of C" && plugins[5].OutputDb == -3 && plugins.Select(p => p.Id).Distinct().Count() == 6
                && Rig().Containers is [_, { Mix: 40 } second] && second.Preset == name && second.Id != first.Id, Shape());
            SmSettle();
            Check("container presets: the closed box names the loaded preset", presets.Shown == name, presets.Shown);

            presets.Trusted = p => p.Name != "C";
            Check("container presets: a plug-in that is not trusted is left out and listed once", presets.Load(name) && Shape() == "A Container[B C] D Container[B]" && said.Count == 1 && said[0].Contains("C (", StringComparison.Ordinal) && !said[0].Contains("B (", StringComparison.Ordinal), $"{Shape()} / {string.Join(" | ", said)}");
            fx.Close();

            Documents.DocumentEdits.Undo(doc);
            Check("container presets: undo brings back what the container held before the load", Shape() == "A Container[B C] D Container[B C]", Shape());
            Documents.DocumentEdits.Undo(doc);
            Check("container presets: a second undo empties it again", Shape() == "A Container[B C] D Container[]", Shape());
            PresetFolderStore.Containers().Delete(name);
        }
        finally { SmCloseWindow(w); }
    }
}
