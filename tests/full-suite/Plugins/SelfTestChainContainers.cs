using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

// Owns: FX chain containers as data: nesting and the tree, bypass inherited by the plug-ins inside, the saved forms (a rig, a chain
//     file) read back whole, read by a reader that does not know containers, and an older file; the importer keeping container blocks.
// Does not own: the list UI (TestFxChainListSelection), the importer's state decoding (TestReaperChainImport).
// Tests: TestChainContainers.
public static partial class SelfTest
{
    private static string CcShape(IEnumerable<ChainItem> items) =>
        string.Join(" ", items.Select(i => i.Plugin is { } p ? p.Name : $"{i.Container!.Name}[{CcShape(i.Children)}]"));

    private static void TestChainContainers()
    {
        RigPreset Rig() { var r = new RigPreset(); foreach (var n in new[] { "A", "B", "C", "D", "E" }) r.Plugins.Add(new PluginSlot { Name = n, Path = "", Format = "VST2" }); return r; }
        var rig = Rig();
        var outer = ChainTree.Group(rig, 1, 3, "Outer")!;
        var inner = ChainTree.Group(rig, 2, 1, "Inner")!;
        Check("chain containers: a container in a container gives the nested tree over the same flat list",
            CcShape(ChainTree.Tree(rig)) == "A Outer[B Inner[C] D] E" && string.Join("", rig.Plugins.Select(p => p.Name)) == "ABCDE", CcShape(ChainTree.Tree(rig)));

        // A bypassed container bypasses its plug-ins for the engine and leaves their own switches alone.
        outer.Enabled = false; inner.Mix = 40;
        ChainTree.Refresh(rig);
        var specs = PluginTrust.BuildSpecs(rig.Plugins, Array.Empty<string>(), new PluginSettings());
        Check("chain containers: a bypassed container bypasses the plug-ins inside for the engine and keeps their own switches",
            string.Join("", specs.Select(s => s.Enabled ? "1" : "0")) == "10001" && rig.Plugins.All(p => p.Enabled), string.Join("", specs.Select(s => s.Enabled ? "1" : "0")));

        // An instrument in a bypassed container does not play: the track's route falls back as for a bypassed instrument.
        var routed = new TabForge.Models.TrackModel { SoundSource = TabForge.Models.SoundSources.Plugins, MidiSound = true, Rig = Rig() };
        routed.Rig.Plugins[1].Type = PluginSlotType.Instrument;
        var mixer = new TabForge.Models.MixerOptions { PlayAllThroughEngine = true };
        var routeOn = TabForge.Models.MixerGroups.RouteOf(routed, mixer);
        ChainTree.Group(routed.Rig, 1, 1, "Box")!.Enabled = false;
        ChainTree.Refresh(routed.Rig);
        Check("chain containers: an instrument in a bypassed container does not count as playing, so the route is the GM fallback",
            routeOn == TabForge.Models.TrackRoute.Instrument && !TabForge.Models.MixerGroups.InstrumentPlays(routed) && TabForge.Models.MixerGroups.RouteOf(routed, mixer) != TabForge.Models.TrackRoute.Instrument && routed.Rig.Plugins[1].Enabled,
            $"{routeOn} -> {TabForge.Models.MixerGroups.RouteOf(routed, mixer)}");

        // Saved with the rig: read back whole; a reader that does not know the section reads the flat chain; an older file is unchanged.
        var json = JsonSerializer.Serialize(rig);
        var back = JsonSerializer.Deserialize<RigPreset>(json)!;
        ChainTree.Refresh(back);
        Check("chain containers: a saved rig reads back with its containers, bypass and mix",
            CcShape(ChainTree.Tree(back)) == "A Outer[B Inner[C] D] E" && back.Containers is [{ Enabled: false }, { Mix: 40 }] && !back.Plugins[2].Plays && !json.Contains("GroupBypassed"), json.Length.ToString());
        var node = JsonNode.Parse(json)!.AsObject(); node.Remove("Containers");
        var old = JsonSerializer.Deserialize<RigPreset>(node.ToJsonString())!;
        Check("chain containers: read without the container section the rig is the flat chain",
            old.Containers is null && string.Join("", old.Plugins.Select(p => p.Name)) == "ABCDE" && old.Plugins.All(p => p.Plays), "");
        Check("chain containers: a rig without containers is written without the section", !JsonSerializer.Serialize(Rig()).Contains("Container"), "");
        var broken = Rig(); broken.Containers = new() { new ChainContainer { Start = 3, Count = 9 }, new ChainContainer { Start = 0, Count = 2, Name = "Ok" }, new ChainContainer { Start = 1, Count = 2 } };
        ChainTree.Refresh(broken);
        Check("chain containers: containers that do not fit the list or cross another are dropped", CcShape(ChainTree.Tree(broken)) == "Ok[A B] C D E", CcShape(ChainTree.Tree(broken)));

        // A chain file stays a list of plug-ins: the containers ride on the first one.
        var folder = Path.Combine(Path.GetTempPath(), "tf-chaincont-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "nested.tfchain");
            PluginLibrary.SaveChain(file, rig.Plugins, rig.Containers);
            var text = File.ReadAllText(file);
            var loaded = PluginLibrary.LoadChain(file);
            var target = new RigPreset(); target.Plugins.Add(new PluginSlot { Name = "Z" });
            if (loaded is not null) { target.Plugins.AddRange(loaded); ChainTree.Reconcile(target, ChainTree.Tree(new List<PluginSlot> { target.Plugins[0] }, null)); }
            Check("chain containers: a chain file is a plug-in list that carries its containers; added to a chain they land where its plug-ins land",
                text.TrimStart().StartsWith('[') && rig.Plugins.All(p => p.ChainContainers is null) && CcShape(ChainTree.Tree(target)) == "Z A Outer[B Inner[C] D] E", CcShape(ChainTree.Tree(target)));
            var array = JsonNode.Parse(text)!.AsArray(); foreach (var item in array) item!.AsObject().Remove("ChainContainers");
            var flat = JsonSerializer.Deserialize<List<PluginSlot>>(array.ToJsonString())!;
            Check("chain containers: read without the container section the chain file is the flat chain", string.Join("", flat.Select(p => p.Name)) == "ABCDE", "");
            File.WriteAllText(file, JsonSerializer.Serialize(Rig().Plugins));
            Check("chain containers: a chain file without containers loads as before", PluginLibrary.LoadChain(file) is { Count: 5 } plain && plain.All(p => p.ChainContainers is null), "");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }

        // The importer keeps container blocks: name (the block's name, else its comment, else its preset), bypass, mix, nesting.
        const string fixture = """
            BYPASS 0 0
            <VST "VST: Tuner (Maker)" tuner.dll 0 "" 1<00> ""
            >
            BYPASS 1 0
            <CONTAINER Container ""
              CONTAINER_CFG 4 2 2 0
              <IN_PINS
              >
              BYPASS 0 0 0
              <VST "VST: Gate (Maker)" gate.dll 0 "" 2<00> ""
              >
              PRESETNAME "Tight gate"
              BYPASS 0 0 0
              <CONTAINER Container "Inner box"
                CONTAINER_CFG 2 2 2 0
                BYPASS 1 0 0
                <VST "VST: Eq (Maker)" eq.dll 0 "" 3<00> ""
                >
              >
              WET 0.25 0
            >
            PRESETNAME "Pre 1.0"
            <COMMENT
              UHJlLUFtcA==
            >
            BYPASS 0 0
            <VST "VST: Missing (Maker)" missing.dll 0 "" 4<00> ""
            >
            """;
        var parsed = ReaperChainImporter.Parse(fixture);
        var catalog = new[] { "tuner", "gate", "eq" }.Select(n => new VstPluginInfo(n, $@"C:\vst\{n}.dll", "VST2", "Maker")).ToList();
        var imported = ReaperChainImporter.Build(parsed, catalog);
        var importedRig = new RigPreset(); importedRig.Plugins.AddRange(imported.Chain); ChainTree.Reconcile(importedRig, new List<ChainItem>());
        var pre = importedRig.Containers?.FirstOrDefault(c => c.Name == "Pre-Amp"); var box = importedRig.Containers?.FirstOrDefault(c => c.Name == "Inner box");
        Check("chain import: container blocks stay containers with their name, preset, bypass, mix and nesting",
            CcShape(ChainTree.Tree(importedRig)) == "tuner Pre-Amp[gate Inner box[eq]]" && pre is { Enabled: false, Preset: "Pre 1.0", Mix: 100 } && box is { Enabled: true, Mix: 25 }
            && parsed.Entries[1].Preset == "Tight gate" && importedRig.Plugins[1].Enabled && !importedRig.Plugins[1].Plays && !importedRig.Plugins[2].Enabled,
            $"{CcShape(ChainTree.Tree(importedRig))} pre {pre?.Enabled}/{pre?.Preset}/{pre?.Mix} box {box?.Mix}");
        Check("chain import: internal routing is kept as text with one summary line",
            pre?.Routing?.StartsWith("CONTAINER_CFG 4 2 2 0") == true && imported.Report.Contains("1 container uses its own internal routing; their plug-ins run in series here."), imported.Report);

        // A closed container that holds nothing that came in does not take the plug-ins behind it; unnamed, it goes.
        const string hollow = """
            BYPASS 1 0
            <CONTAINER Container ""
              BYPASS 0 0 0
              <JS utility/volume ""
              >
            >
            BYPASS 0 0
            <VST "VST: Gate (Maker)" gate.dll 0 "" 2<00> ""
            >
            BYPASS 0 0
            <VST "VST: Eq (Maker)" eq.dll 0 "" 3<00> ""
            >
            """;
        var hollowParsed = ReaperChainImporter.Parse(hollow);
        var hollowRig = new RigPreset(); hollowRig.Plugins.AddRange(ReaperChainImporter.Build(hollowParsed, catalog).Chain); ChainTree.Reconcile(hollowRig, new List<ChainItem>());
        Check("chain import: a bypassed container with nothing imported inside leaves the plug-ins behind it outside and playing",
            hollowParsed.Groups is [{ Count: 0 }] && CcShape(ChainTree.Tree(hollowRig)) == "gate eq" && hollowRig.Plugins.All(p => p.Plays), CcShape(ChainTree.Tree(hollowRig)));

        // Default chains keep containers: packed on a copy, never on the live plug-ins; the same chain twice is not nested twice.
        var copy = System.Text.Json.JsonSerializer.Deserialize<List<PluginSlot>>(System.Text.Json.JsonSerializer.Serialize(rig.Plugins))!;
        ChainTree.Pack(copy, rig.Containers);
        var fromCopy = new RigPreset { Plugins = copy };
        ChainTree.Refresh(fromCopy);
        Check("chain containers: a packed copy of the list brings the containers and their bypass to the next rig, and the live list stays plain",
            CcShape(ChainTree.Tree(fromCopy)) == "A Outer[B Inner[C] D] E" && !fromCopy.Plugins[1].Plays && rig.Plugins.All(p => p.ChainContainers is null) && !ReferenceEquals(fromCopy.Containers![0], rig.Containers![0]), CcShape(ChainTree.Tree(fromCopy)));
        fromCopy.Plugins[0].ChainContainers = fromCopy.Containers!.ToList();
        ChainTree.Refresh(fromCopy);
        Check("chain containers: containers the rig already has are not taken in a second time", fromCopy.Containers is { Count: 2 } && CcShape(ChainTree.Tree(fromCopy)) == "A Outer[B Inner[C] D] E", CcShape(ChainTree.Tree(fromCopy)));
    }
}
