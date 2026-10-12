using System.IO;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Plugins;

namespace TabForge;

/// <summary>FX chain file for other hosts: what the exporter writes comes back through the importer unchanged; the engine's plug-in description travels whole.</summary>
public static partial class SelfTest
{
    private static void TestRfxChainExport()
    {
        var chunk = Enumerable.Range(0, 250).Select(i => (byte)(i * 7 + 3)).ToArray();
        var list = new byte[] { 0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE }.Concat(BitConverter.GetBytes(0.25f)).Concat(BitConverter.GetBytes(1f)).ToArray();
        byte[] comp = Enumerable.Range(0, 130).Select(i => (byte)(255 - i)).ToArray(), ctrl = { 1, 2, 3 };
        var vst3 = BitConverter.GetBytes(comp.Length).Concat(comp).Concat(BitConverter.GetBytes(ctrl.Length)).Concat(ctrl).ToArray();
        const string classId = "ABCDEF0123456789ABCDEF0123456789";
        var slots = new List<PluginSlot>
        {
            new() { Name = "Test EQ", Vendor = "Acme", Path = @"C:\Plugins\Test EQ.dll", Format = "VST2", State = Convert.ToBase64String(chunk) },
            new() { Name = "Comp", Vendor = "Acme", Path = @"C:\Plugins\comp.dll", Format = "VST2", Enabled = false, Wet = 25, State = Convert.ToBase64String(list), OutputDb = -3,
                MidiProcessors = { new PluginMidiProcessor { Type = "transpose" } } },
            new() { Name = "Synth", Vendor = "Acme Audio", Path = @"C:\Plugins\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument, State = Convert.ToBase64String(vst3), SidechainTrackId = "abc" },
            new() { Name = "Not loaded", Path = @"C:\Plugins\gone.dll", Format = "VST2", State = Convert.ToBase64String(chunk) },
            new() { Name = "Odd", Vendor = "Acme", Path = @"C:\Plugins\odd.vst3", Format = "VST3", State = Convert.ToBase64String(new byte[] { 9, 0, 0, 0, 1 }) },
        };
        var info = new LoadedPluginInfo?[]
        {
            new(LoadedPluginInfo.Vst2, 0x74736574, "", 2, 2, true, "Acme", "Test EQ"),
            new(LoadedPluginInfo.Vst2, 0x636F6D70, "", 4, 2, false, "Acme", "Comp"),
            new(LoadedPluginInfo.Vst3, 0, classId, -1, -1, true, "Acme Audio", "Synth"),
            null,
            new(LoadedPluginInfo.Vst3, 0, classId, -1, -1, true, "Acme", "Odd"),
        };
        // Containers: a nested tree comes back as the same tree, with name, bypass, mix, preset name and routing, and byte-equal states.
        var boxed = slots.Take(3).ToList();
        const string routing = "CONTAINER_CFG 4 2 2 0\n<IN_PINS\n>\n<OUT_PINS\n>\n";
        var boxes = new List<ChainContainer>
        {
            new() { Name = "Amp box", Enabled = false, Mix = 50, Preset = "P 1", Routing = routing, Start = 0, Count = 3, Depth = 0 },
            new() { Name = "Inner", Start = 1, Count = 1, Depth = 1, Routing = "CONTAINER_CFG 2 2 2 0\n<VST \"injected\"\n" },
            new() { Name = "Empty", Start = 3, Count = 0, Depth = 1 },
        };
        var nested = RfxChainExporter.Export(RfxChainExporter.FromTree(ChainTree.Tree(boxed, boxes), boxed, info));
        var nestedBack = ReaperChainImporter.Parse(nested.Text);
        Check("chain export: containers are written as blocks and come back as the same tree with their settings",
            nested.Written == 3 && nestedBack.Entries.Count == 3
            && nestedBack.Groups is [{ Name: "Amp box", Enabled: false, Mix: 50, Preset: "P 1", Start: 0, Count: 3, Depth: 0 }, { Name: "Inner", Enabled: true, Start: 1, Count: 1, Depth: 1 }, { Name: "Empty", Start: 3, Count: 0, Depth: 1 }]
            && nestedBack.Groups[0].Routing == routing && nestedBack.Groups[1].Routing.StartsWith("CONTAINER_CFG 2 2 2 0") && !nested.Text.Contains("injected")
            && nestedBack.Entries[0].State!.SequenceEqual(chunk) && nestedBack.Entries[1].State!.SequenceEqual(list) && !nestedBack.Entries[1].Enabled,
            $"{nested.Written} / {string.Join(" | ", nestedBack.Groups.Select(g => $"{g.Name} {g.Start}+{g.Count} d{g.Depth} {g.Enabled} {g.Mix} {g.Preset}"))}");

        var exported = RfxChainExporter.Export(RfxChainExporter.FromSlots(slots, info), builtInSound: true);
        var text = exported.Text;
        var back = ReaperChainImporter.Parse(text).Entries;
        Check("chain export: every plug-in with info is written, the one without is left out and named once",
            exported.Written == 4 && back.Count == 4 && exported.Notes.Count(n => n.StartsWith("Not exported") && n.Contains("Not loaded")) == 1 && exported.Notes[0] == "Exported 4 of 5 plug-ins.",
            $"{exported.Written} / {back.Count} / {string.Join(" | ", exported.Notes)}");
        if (back.Count != 4) return;
        Check("chain export: names, makers, file names and formats come back",
            back[0] is { Name: "Test EQ", Vendor: "Acme", FileName: "Test EQ.dll", Format: "VST2", IsInstrument: false }
            && back[1] is { Name: "Comp", FileName: "comp.dll", Format: "VST2" } && back[2] is { Name: "Synth", Vendor: "Acme Audio", FileName: "Synth.vst3", Format: "VST3", IsInstrument: true },
            string.Join(" | ", back.Select(b => $"{b.Name}/{b.Vendor}/{b.FileName}/{b.Format}")));
        Check("chain export: bypass and mix come back", back[0] is { Enabled: true, Wet: 100 } && back[1] is { Enabled: false, Wet: 25 } && back[2] is { Enabled: true, Wet: 100 });
        Check("chain export: VST2 chunk, VST2 parameter list and VST3 states come back byte for byte",
            back[0].State is { } s0 && s0.SequenceEqual(chunk) && back[1].State is { } s1 && s1.SequenceEqual(list) && back[2].State is { } s2 && s2.SequenceEqual(vst3),
            string.Join(" | ", back.Select(b => b.StateProblem ?? "ok")));
        // The id fields and the block header, as the importer's block reader sees them.
        var lines = text.Split("\r\n");
        ReaperChainImporter.Block? BlockOf(string file)
        {
            var at = Array.FindIndex(lines, l => l.StartsWith("<VST ") && l.Contains(file));
            return ReaperChainImporter.DecodeBlock(lines.Skip(at + 1).TakeWhile(l => l != ">").Select(l => l.Trim()).ToList(), out _);
        }
        Check("chain export: VST2 id field (number, then VST + four characters + file name) and VST3 class id",
            text.Contains("<VST \"VST: Test EQ (Acme)\" \"Test EQ.dll\" 0 \"\" 1953719668<56535474736574746573742065710000> \"\"")
            && text.Contains("<VST \"VST3i: Synth (Acme Audio)\" Synth.vst3 0 \"\" 0{" + classId + "} \"\""), lines.FirstOrDefault(l => l.StartsWith("<VST")) ?? "");
        Check("chain export: header (id, chunk or parameter-list mark, pin counts) and lines of at most 128 characters",
            BlockOf("Test EQ.dll") is { Id: 0x74736574, Magic: 0xFEED5EEE, Inputs: 2, Outputs: 2, IsChunk: true }
            && BlockOf("comp.dll") is { Id: 0x636F6D70, Magic: 0xFEED5EEF, Inputs: 4, Outputs: 2, IsChunk: false }
            && BlockOf("Synth.vst3") is { Magic: 0xFEED5EEE, Inputs: 2, Outputs: 2, IsChunk: true } && lines.All(l => l.Trim().Length <= 128 || l.StartsWith("<VST")));
        Check("chain export: a state with an unknown layout is not written; the plug-in is kept and named",
            back[3] is { Name: "Odd", State: null } && exported.Notes.Count(n => n.StartsWith("Exported with default settings: Odd")) == 1, string.Join(" | ", exported.Notes));
        Check("chain export: TabForge-only settings are named, one line each",
            exported.Notes.Count(n => n.Contains("MIDI processors (Comp)")) == 1 && exported.Notes.Count(n => n.Contains("sidechain and MIDI routes (Synth)")) == 1
            && exported.Notes.Count(n => n.Contains("plug-in output volume (Comp)")) == 1 && exported.Notes.Count(n => n.Contains("built-in sound")) == 1, string.Join(" | ", exported.Notes));
        Check("chain export: a chain that goes out whole says nothing; no info at all writes nothing",
            RfxChainExporter.Export(RfxChainExporter.FromSlots(slots.Take(1).ToList(), info)) is { Written: 1, Notes.Count: 0 }
            && RfxChainExporter.Export(RfxChainExporter.FromSlots(slots, Array.Empty<LoadedPluginInfo?>())) is { Written: 0, Text: "" });
        var odd = new List<PluginSlot> { new() { Name = "Q", Path = @"C:\Plugins\'q`x.dll", Format = "VST2" }, new() { Name = "Wrong", Path = @"C:\Plugins\w.dll", Format = "VST2" } };
        var oddOut = RfxChainExporter.Export(RfxChainExporter.FromSlots(odd, new LoadedPluginInfo?[] { info[0], info[2] }));
        Check("chain export: a file name with ' or ` is quoted and comes back; info of another format counts as no info",
            oddOut.Written == 1 && oddOut.Text.Contains(" \"'q`x.dll\" 0 ") && ReaperChainImporter.Parse(oddOut.Text).Entries is { Count: 1 } q && q[0].FileName == "'q`x.dll", oddOut.Text);
        Check("chain export: the same chain exports to the same text", RfxChainExporter.Export(RfxChainExporter.FromSlots(slots, info), true).Text == text);

        // The engine's answer: the record travels whole, and the client files it by request; an answer nobody asked for is dropped.
        BinaryReader Frame(int slot, int id, int index, int count, LoadedPluginInfo plugin)
        {
            var stream = new MemoryStream();
            Frames.Write(stream, (byte)EngineEvent.PluginInfo, w => { w.Write(slot); w.Write(id); w.Write(index); w.Write(count); plugin.Write(w); });
            stream.Position = 0;
            return Frames.Read(stream)!.Value.Reader;
        }
        var client = new AudioEngineClient();
        var requestId = client.BeginInfoRequest(7, out var request);
        client.OnPluginInfo(Frame(7, requestId + 1000, 0, 2, info[0]!));
        client.OnPluginInfo(Frame(7, requestId, 0, 2, info[0]!));
        var early = request.Done.Task.IsCompleted;
        client.OnPluginInfo(Frame(7, requestId, 1, 2, info[2]!));
        Check("plug-in info: both answers arrive whole, the request completes with the last one, a stray answer is dropped",
            !early && request.Done.Task.IsCompleted && request.Replies.Count == 2 && request.Replies[0] == info[0] && request.Replies[1] == info[2] && info[0]!.FourChars == "tset");
        var none = new AudioEngineClient().GetPluginInfoAsync(new TabForge.Models.TrackModel { Rig = { Plugins = { slots[0] } } }, timeoutMs: 50);
        Check("plug-in info: without a running engine every plug-in is 'not available', at once", none.IsCompleted && none.Result is { Length: 1 } r && r[0] is null);
    }
}
