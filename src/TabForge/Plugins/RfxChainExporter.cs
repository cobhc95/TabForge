using System.Globalization;
using System.IO;
using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.Plugins;

// Owns: writing a chain as the text of an FX chain file for other hosts (.RfxChain): the inverse of ReaperChainImporter
//       (the block layout is in that class's header comment). Pure: items in, text and notes out.
// Does not own: reading plug-in states or plug-in info (AudioEngineClient), the file dialog and the write (RfxChainExportFlow),
//       the container model (ChainTree gives the tree; an item with Children is written as a container block).
// Tests: TestRfxChainExport (export, then import: names, files, bypass, mix and states come back byte for byte).
public static class RfxChainExporter
{
    private const int MaxStateBytes = PluginStateLimits.MaxRawBytes;
    private const int LineBytes = 96;   // 128 base64 characters a line

    /// <summary>
    /// One plug-in, or (with <paramref name="Children"/>) a group of them. <paramref name="State"/> is the engine's state layout;
    /// <paramref name="Info"/> is what the loaded plug-in reports (null: not available, the plug-in is left out and named);
    /// <paramref name="NotInFile"/> names TabForge-only settings of this plug-in that the file cannot hold (for the notes).
    /// </summary>
    public sealed record Item(string Name, string Vendor, string FileName, string Format, bool IsInstrument, bool Enabled, int Wet, byte[]? State,
        LoadedPluginInfo? Info, IReadOnlyList<string>? NotInFile = null, IReadOnlyList<Item>? Children = null, string PresetName = "", string? Routing = null);

    /// <summary><paramref name="Written"/> plug-ins are in <paramref name="Text"/>; <paramref name="Notes"/> is one line per kind of thing left out.</summary>
    public sealed record Result(string Text, List<string> Notes, int Written);

    /// <summary>A chain's slots as items; <paramref name="info"/> is per slot, in order (missing or null: not available).</summary>
    public static List<Item> FromSlots(IReadOnlyList<PluginSlot> slots, IReadOnlyList<LoadedPluginInfo?> info)
    {
        var items = new List<Item>();
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var extra = new List<string>();
            if (s.MidiProcessors.Count > 0) extra.Add("MIDI processors");
            if (s.SidechainTrackId.Length > 0 || s.MidiOutTrackId.Length > 0 || s.MidiIn.Source != PluginMidiIn.Own) extra.Add("sidechain and MIDI routes");
            if (s.OutputDb != 0) extra.Add("plug-in output volume");
            byte[]? state = null;
            if (s.State is { Length: > 0 })
            {
                try { state = Convert.FromBase64String(s.State); }
                catch (FormatException) { state = Array.Empty<byte>(); } // Not logged: reported in the export notes as "default settings"
            }
            var vst3 = s.Format == "VST3" || s.Path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);
            items.Add(new Item(s.Name, s.Vendor, Path.GetFileName(s.Path.TrimEnd('\\', '/')), vst3 ? "VST3" : "VST2", s.Type == PluginSlotType.Instrument, s.Enabled, s.Wet,
                state, i < info.Count ? info[i] : null, extra));
        }
        return items;
    }

    /// <summary>
    /// A chain's tree as items: a container becomes an item with Children that carries its name, bypass (Enabled), mix (Wet),
    /// preset name and routing text. <paramref name="slots"/> and <paramref name="info"/> are the flat chain, as for <see cref="FromSlots"/>.
    /// </summary>
    public static List<Item> FromTree(IReadOnlyList<ChainItem> tree, IReadOnlyList<PluginSlot> slots, IReadOnlyList<LoadedPluginInfo?> info)
    {
        var flat = FromSlots(slots, info);
        List<Item> Map(IReadOnlyList<ChainItem> items) => items.Select(i => i.Plugin is { } p
            ? (slots.ToList().IndexOf(p) is var at and >= 0 ? flat[at] : null)
            : new Item(i.Container!.Name, "", "", "", false, i.Container.Enabled, i.Container.Mix, null, null, null, Map(i.Children), i.Container.Preset ?? "", i.Container.Routing)).Where(i => i is not null).Select(i => i!).ToList();
        return Map(tree);
    }

    private const string StereoRouting = "CONTAINER_CFG 2 2 2 0\n<IN_PINS\n>\n<OUT_PINS\n>\n";

    /// <summary>The routing text as kept from an import when it is only a config line and pin blocks; anything else is replaced by a plain stereo path.</summary>
    private static string RoutingText(string? routing)
    {
        if (routing is not { Length: > 0 and <= ChainTree.MaxRoutingChars }) return StereoRouting;
        var lines = routing.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inBlock = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (i == 0 ? !l.StartsWith("CONTAINER_CFG ", StringComparison.Ordinal) : l.StartsWith("CONTAINER_CFG", StringComparison.Ordinal)) return StereoRouting;
            if (i == 0) continue;
            if (l is "<IN_PINS" or "<OUT_PINS") { if (inBlock) return StereoRouting; inBlock = true; }
            else if (l == ">") { if (!inBlock) return StereoRouting; inBlock = false; }
            else if (!inBlock || l.StartsWith('<') || l.Any(char.IsControl)) return StereoRouting;
        }
        return inBlock ? StereoRouting : string.Concat(lines.Select(l => l.Trim() + "\n"));
    }

    /// <param name="builtInSound">The track also sounds through TabForge's built-in General MIDI sound (it is named in the notes).</param>
    public static Result Export(IReadOnlyList<Item> items, bool builtInSound = false)
    {
        var text = new StringBuilder();
        var noInfo = new List<string>(); var noState = new List<string>(); var notInFile = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        int written = 0, total = 0;
        void Walk(IReadOnlyList<Item> list)
        {
            foreach (var item in list)
            {
                if (item.Children is { } children)
                {
                    // A container block: its lines, its items, then the lines that follow a block (mix, preset name).
                    text.Append("BYPASS ").Append(item.Enabled ? '0' : '1').Append(" 0\r\n").Append("<CONTAINER Container ").Append('"').Append(Clean(item.Name)).Append("\"\r\n");
                    foreach (var line in RoutingText(item.Routing).Split('\n', StringSplitOptions.RemoveEmptyEntries)) text.Append("  ").Append(line).Append("\r\n");
                    text.Append("  SHOW 0\r\n  LASTSEL 0\r\n  DOCKED 0\r\n");
                    Walk(children);
                    text.Append(">\r\n");
                    if (item.Wet != 100) text.Append("WET ").Append((Math.Clamp(item.Wet, 0, 100) / 100.0).ToString("0.######", CultureInfo.InvariantCulture)).Append(" 0\r\n");
                    if (Clean(item.PresetName) is { Length: > 0 } boxPreset) text.Append("PRESETNAME ").Append(Quote(boxPreset)).Append("\r\n");
                    text.Append("WAK 0 0\r\n");
                    continue;
                }
                total++;
                if (item.FileName.Length == 0 || item.Info is not { Format: LoadedPluginInfo.Vst2 or LoadedPluginInfo.Vst3 } info || (info.Format == LoadedPluginInfo.Vst3) != (item.Format == "VST3") || (info.Format == LoadedPluginInfo.Vst3 && !IsClassId(info.ClassId)))
                { noInfo.Add(Label(item)); continue; }
                var problem = WriteItem(text, item, info, written);
                if (problem is not null) noState.Add($"{Label(item)} ({problem})");
                foreach (var thing in item.NotInFile ?? Array.Empty<string>())
                {
                    if (!notInFile.TryGetValue(thing, out var names)) notInFile[thing] = names = new List<string>();
                    if (!names.Contains(Label(item))) names.Add(Label(item));
                }
                written++;
            }
        }
        Walk(items);
        var notes = new List<string>();
        if (noInfo.Count > 0 || noState.Count > 0) notes.Add($"Exported {written} of {total} plug-in{(total == 1 ? "" : "s")}.");
        if (noInfo.Count > 0) notes.Add($"Not exported, because the plug-in is not loaded in the audio engine (start the audio engine and check that it loads): {string.Join(", ", noInfo.Distinct())}.");
        if (noState.Count > 0) notes.Add($"Exported with default settings: {string.Join(", ", noState)}.");
        foreach (var (thing, names) in notInFile) notes.Add($"Not part of the file: {thing} ({string.Join(", ", names)}).");
        if (builtInSound) notes.Add("TabForge's built-in sound is not part of the file.");
        return new Result(text.ToString(), notes, written);
    }

    private static string Label(Item item) => item.Name.Length > 0 ? item.Name : item.FileName.Length > 0 ? Path.GetFileNameWithoutExtension(item.FileName) : "Built-in sound";
    private static bool IsClassId(string id) => id.Length == 32 && id.All(Uri.IsHexDigit);
    // Double quotes always: Clean leaves none in the text, and they keep a ' or ` in it from opening a quoted token.
    private static string Quote(string s) => s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || c is '\'' or '`') ? "\"" + s + "\"" : s;
    private static string Clean(string s) => new string(s.Select(c => c == '"' ? '\'' : char.IsControl(c) ? ' ' : c).ToArray());

    /// <summary>Appends one plug-in with its lines; returns why its settings were left out, or null.</summary>
    private static string? WriteItem(StringBuilder text, Item item, LoadedPluginInfo info, int ordinal)
    {
        var vst3 = info.Format == LoadedPluginInfo.Vst3;
        string? problem = null;
        byte[]? state = null; var isChunk = true;
        if (item.State is { } own)
        {
            if (own.Length == 0) problem = "its saved settings could not be read";
            else if (own.Length > MaxStateBytes) problem = "its saved settings are too large";
            else if (vst3) { state = Vst3FileState(own); if (state is null) problem = "its saved settings have an unknown layout"; }
            else
            {
                // A plug-in without chunks is saved as its parameter list: the same bytes in the file, marked as "not a chunk".
                var list = own.Length >= 12 && (own.Length - 8) % 4 == 0 && own.AsSpan(0, 8).SequenceEqual(ParameterListMark);
                if (!info.UsesChunks && !list) problem = "its saved settings have an unknown layout";
                else { state = own; isChunk = !(list && !info.UsesChunks); }
            }
        }
        var name = Clean(info.Name.Length > 0 ? info.Name : item.Name).Trim();
        var vendor = Clean(info.Vendor.Length > 0 ? info.Vendor : item.Vendor).Trim();
        var kind = (vst3 ? "VST3" : "VST") + (item.IsInstrument ? "i" : "");
        var id = vst3 ? $"{info.UniqueId}{{{info.ClassId.ToUpperInvariant()}}}" : $"{info.UniqueId}<{Vst2IdHex(info.UniqueId, item.FileName)}>";
        text.Append("BYPASS ").Append(item.Enabled ? '0' : '1').Append(" 0\r\n");
        text.Append("<VST \"").Append(kind).Append(": ").Append(name).Append(vendor.Length > 0 ? $" ({vendor})" : "").Append("\" ")
            .Append(Quote(Clean(item.FileName))).Append(" 0 \"\" ").Append(id).Append(" \"\"\r\n");
        if (state is not null)
        {
            // A plug-in whose pin counts are not known (VST3) is written as stereo in, stereo out.
            var ins = info.Inputs < 0 ? 2 : Math.Min(info.Inputs, ReaperChainImporter.MaxPins);
            var outs = info.Outputs < 0 ? 2 : Math.Min(info.Outputs, ReaperChainImporter.MaxPins);
            var header = new MemoryStream(); var w = new BinaryWriter(header);
            w.Write(info.UniqueId); w.Write(isChunk ? 0xFEED5EEEu : 0xFEED5EEFu);
            foreach (var pins in new[] { ins, outs })
            {
                w.Write(pins);
                for (var i = 0; i < pins; i++) w.Write(i < 64 ? 1UL << i : 0UL);   // pin i to channel i
            }
            w.Write(state.Length); w.Write(isChunk ? 1 : 0); w.Write(vst3 ? 0x0010FFFFu : 0x00100000u);
            var footer = new MemoryStream();
            footer.WriteByte(0); footer.Write(Encoding.UTF8.GetBytes(Clean(item.PresetName))); footer.WriteByte(0); footer.Write(BitConverter.GetBytes(0x10));
            // Header, state and footer each start a line of their own; every line is encoded on its own.
            foreach (var part in new[] { header.ToArray(), state, footer.ToArray() })
                for (var o = 0; o < part.Length; o += LineBytes)
                    text.Append("  ").Append(Convert.ToBase64String(part, o, Math.Min(LineBytes, part.Length - o))).Append("\r\n");
        }
        text.Append(">\r\n");
        if (item.Wet != 100) text.Append("WET ").Append((Math.Clamp(item.Wet, 0, 100) / 100.0).ToString("0.######", CultureInfo.InvariantCulture)).Append(" 0\r\n");
        if (Clean(item.PresetName) is { Length: > 0 } preset) text.Append("PRESETNAME ").Append(Quote(preset)).Append("\r\n");
        // A stable id per place in the chain, so the same chain exports to the same text.
        var fxId = new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes($"{ordinal}|{item.FileName}|{name}")));
        text.Append("FLOATPOS 0 0 0 0\r\n").Append("FXID ").Append(fxId.ToString("B").ToUpperInvariant()).Append("\r\n").Append("WAK 0 0\r\n");
        return problem;
    }

    private static ReadOnlySpan<byte> ParameterListMark => new byte[] { 0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE };

    /// <summary>"VST", the id's four bytes (most significant first), then the first nine bytes of the file name in lower case, zero filled.</summary>
    public static string Vst2IdHex(uint id, string fileName)
    {
        var b = new byte[16];
        b[0] = (byte)'V'; b[1] = (byte)'S'; b[2] = (byte)'T';
        for (var i = 0; i < 4; i++) b[3 + i] = (byte)(id >> (24 - 8 * i));
        var stem = Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant());
        stem.AsSpan(0, Math.Min(9, stem.Length)).CopyTo(b.AsSpan(7));
        return Convert.ToHexString(b);
    }

    /// <summary>
    /// The engine's VST3 state (length, component, length, controller) as the file stores it (length, 1, component, length, 0,
    /// controller): the inverse of <see cref="ReaperChainImporter.Vst3State"/>. Null when the lengths do not fit the bytes.
    /// </summary>
    public static byte[]? Vst3FileState(byte[] s)
    {
        if (s.Length < 8) return null;
        long cs = BitConverter.ToUInt32(s, 0);
        if (cs + 8 > s.Length) return null;
        long ks = BitConverter.ToUInt32(s, (int)cs + 4);
        if (cs + ks + 8 != s.Length) return null;
        var result = new byte[s.Length + 8];
        BitConverter.TryWriteBytes(result.AsSpan(0, 4), (uint)cs);
        BitConverter.TryWriteBytes(result.AsSpan(4, 4), 1);
        s.AsSpan(4, (int)cs).CopyTo(result.AsSpan(8));
        BitConverter.TryWriteBytes(result.AsSpan((int)cs + 8, 4), (uint)ks);
        s.AsSpan((int)cs + 8, (int)ks).CopyTo(result.AsSpan((int)cs + 16));
        return result;
    }
}
