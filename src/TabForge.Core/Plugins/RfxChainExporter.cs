using System.Globalization;
using System.IO;
using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.Plugins;

// Owns: writing a chain as the text of an FX chain file for other hosts (.RfxChain; VST2, VST3 and CLAP blocks): the inverse of ReaperChainImporter
//       (the block layout is in that class's header comment). Pure: items in, text and notes out.
// Does not own: reading plug-in states or plug-in info (AudioEngineClient), the file dialog and the write (RfxChainExportFlow),
//       the container model (ChainTree gives the tree; an item with Children is written as a container block).
// Tests: RfxClapExportTests (the CLAP block text); export, then import: names, files, bypass, mix and states of VST blocks come back byte for byte.
public static class RfxChainExporter
{
    private const int MaxStateBytes = PluginStateLimits.MaxRawBytes;
    private const int LineBytes = 96;   // 128 base64 characters a line

    /// <summary>
    /// One plug-in, or (with <paramref name="Children"/>) a group of them. <paramref name="State"/> is the engine's state layout;
    /// <paramref name="Info"/> is what the loaded plug-in reports (null: no answer; <see cref="LoadedPluginInfo.Unavailable"/>: not loaded, <paramref name="Unloaded"/> says why; either way the plug-in is left out and named);
    /// <paramref name="NotInFile"/> names TabForge-only settings of this plug-in that the file cannot hold (for the notes).
    /// </summary>
    public sealed record Item(string Name, string Vendor, string FileName, string Format, bool IsInstrument, bool Enabled, int Wet, byte[]? State,
        LoadedPluginInfo? Info, IReadOnlyList<string>? NotInFile = null, IReadOnlyList<Item>? Children = null, string PresetName = "", string? Routing = null, RfxExportGap Unloaded = RfxExportGap.Unsupported);

    /// <summary><paramref name="Written"/> plug-ins are in <paramref name="Text"/>; <paramref name="Notes"/> is one line per kind of thing left out.</summary>
    public sealed record Result(string Text, List<string> Notes, int Written);

    /// <summary>A chain's slots as items; <paramref name="info"/> is per slot, in order (missing or null: no answer). <paramref name="whyNotLoaded"/> names why a slot the engine did not load is missing (default: unsupported).</summary>
    public static List<Item> FromSlots(IReadOnlyList<PluginSlot> slots, IReadOnlyList<LoadedPluginInfo?> info, Func<PluginSlot, RfxExportGap>? whyNotLoaded = null)
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
            var clap = s.Format == PluginFormats.Clap || s.Path.EndsWith(".clap", StringComparison.OrdinalIgnoreCase);
            items.Add(new Item(s.Name, s.Vendor, Path.GetFileName(s.Path.TrimEnd('\\', '/')), clap ? PluginFormats.Clap : vst3 ? "VST3" : "VST2", s.Type == PluginSlotType.Instrument, s.Enabled, s.Wet,
                state, i < info.Count ? info[i] : null, extra, PresetName: s.PresetName ?? "", Unloaded: whyNotLoaded?.Invoke(s) ?? RfxExportGap.Unsupported));
        }
        return items;
    }

    /// <summary>
    /// A chain's tree as items: a container becomes an item with Children that carries its name, bypass (Enabled), mix (Wet),
    /// preset name and routing text. <paramref name="slots"/>, <paramref name="info"/> and <paramref name="whyNotLoaded"/> are as for <see cref="FromSlots"/>.
    /// </summary>
    public static List<Item> FromTree(IReadOnlyList<ChainItem> tree, IReadOnlyList<PluginSlot> slots, IReadOnlyList<LoadedPluginInfo?> info, Func<PluginSlot, RfxExportGap>? whyNotLoaded = null)
    {
        var flat = FromSlots(slots, info, whyNotLoaded);
        var position = new Dictionary<PluginSlot, int>(ReferenceEqualityComparer.Instance);
        for (var n = 0; n < slots.Count; n++) position[slots[n]] = n;
        List<Item> Map(IReadOnlyList<ChainItem> items) => items.Select(i => i.Plugin is { } p
            ? (position.TryGetValue(p, out var at) ? flat[at] : null)
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
        var gaps = new List<string>(); var notInFile = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        int written = 0, total = 0;
        void AddGap(Item item, RfxExportGap gap)
        {
            var line = RfxExportGaps.Line(Label(item), gap);
            if (!gaps.Contains(line)) gaps.Add(line);
        }
        // Like Reaper's own files: what is inside a container is indented two spaces a level and its BYPASS lines carry a third field.
        void Walk(IReadOnlyList<Item> list, int depth = 0)
        {
            var pad = new string(' ', 2 * depth);
            foreach (var item in list)
            {
                if (item.Children is { } children)
                {
                    // A container block: its lines, its items, then the lines that follow a block (mix, preset name).
                    text.Append(pad).Append(Bypass(item.Enabled, depth)).Append(pad).Append("<CONTAINER Container ").Append('"').Append(Clean(item.Name)).Append("\"\r\n");
                    foreach (var line in RoutingText(item.Routing).Split('\n', StringSplitOptions.RemoveEmptyEntries)) text.Append(pad).Append("  ").Append(line).Append("\r\n");
                    text.Append(pad).Append("  SHOW 0\r\n").Append(pad).Append("  LASTSEL 0\r\n").Append(pad).Append("  DOCKED 0\r\n");
                    Walk(children, depth + 1);
                    text.Append(pad).Append(">\r\n");
                    if (item.Wet != 100) text.Append(pad).Append("WET ").Append((Math.Clamp(item.Wet, 0, 100) / 100.0).ToString("0.######", CultureInfo.InvariantCulture)).Append(" 0\r\n");
                    if (Clean(item.PresetName) is { Length: > 0 } boxPreset) text.Append(pad).Append("PRESETNAME ").Append(Quote(boxPreset)).Append("\r\n");
                    text.Append(pad).Append("WAK 0 0\r\n");
                    continue;
                }
                total++;
                if (item.FileName.Length == 0 || item.Info is not { Format: LoadedPluginInfo.Vst2 or LoadedPluginInfo.Vst3 or LoadedPluginInfo.Clap } info || info.Format != ExpectedFormat(item) || (info.Format == LoadedPluginInfo.Vst3 && !IsClassId(info.ClassId)) || (info.Format == LoadedPluginInfo.Clap && info.ClassId.Length == 0))
                {
                    var gap = item.Info is null ? RfxExportGap.NoAnswer : item.Info.Format == LoadedPluginInfo.None ? item.Unloaded : RfxExportGap.Unsupported;
                    AddGap(item, gap);
                    continue;
                }
                if (WriteItem(text, item, info, written, depth) is not null) AddGap(item, RfxExportGap.StateUnreadable);
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
        if (gaps.Count > 0) notes.Add($"Exported {written} of {total} plug-in{(total == 1 ? "" : "s")}.\n{string.Join('\n', gaps)}");
        foreach (var (thing, names) in notInFile) notes.Add($"Not part of the file: {thing} ({string.Join(", ", names)}).");
        if (builtInSound) notes.Add("TabForge's built-in sound is not part of the file.");
        return new Result(text.ToString(), notes, written);
    }

    /// <summary>A BYPASS line as Reaper writes it: bypass and offline, plus a third field for a block inside a container.</summary>
    private static string Bypass(bool enabled, int depth) => $"BYPASS {(enabled ? '0' : '1')} 0{(depth > 0 ? " 0" : "")}\r\n";

    private static string Label(Item item) => item.Name.Length > 0 ? item.Name : item.FileName.Length > 0 ? Path.GetFileNameWithoutExtension(item.FileName) : "Built-in sound";
    private static byte ExpectedFormat(Item item) => item.Format == PluginFormats.Clap ? LoadedPluginInfo.Clap : item.Format == "VST3" ? LoadedPluginInfo.Vst3 : LoadedPluginInfo.Vst2;
    private static bool IsClassId(string id) => id.Length == 32 && id.All(Uri.IsHexDigit);
    // Double quotes always: Clean leaves none in the text, and they keep a ' or ` in it from opening a quoted token.
    private static string Quote(string s) => s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || c is '\'' or '`') ? "\"" + s + "\"" : s;
    private static string Clean(string s) => new string(s.Select(c => c == '"' ? '\'' : char.IsControl(c) ? ' ' : c).ToArray());

    /// <summary>Appends one plug-in with its lines; returns why its settings were left out, or null.</summary>
    private static string? WriteItem(StringBuilder text, Item item, LoadedPluginInfo info, int ordinal, int depth)
    {
        if (info.Format == LoadedPluginInfo.Clap) return WriteClap(text, item, info, ordinal, depth);
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
                var list = ParameterListState.IsList(own);
                if (!info.UsesChunks && !list) problem = "its saved settings have an unknown layout";
                else { state = own; isChunk = !(list && !info.UsesChunks); }
            }
        }
        var name = Clean(info.Name.Length > 0 ? info.Name : item.Name).Trim();
        var vendor = Clean(info.Vendor.Length > 0 ? info.Vendor : item.Vendor).Trim();
        var kind = (vst3 ? "VST3" : "VST") + (item.IsInstrument ? "i" : "");
        var id = vst3 ? $"{info.UniqueId}{{{info.ClassId.ToUpperInvariant()}}}" : $"{info.UniqueId}<{Vst2IdHex(info.UniqueId, item.FileName)}>";
        var pad = new string(' ', 2 * depth);
        text.Append(pad).Append(Bypass(item.Enabled, depth));
        text.Append(pad).Append("<VST \"").Append(kind).Append(": ").Append(name).Append(vendor.Length > 0 ? $" ({vendor})" : "").Append("\" ")
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
                    text.Append(pad).Append("  ").Append(Convert.ToBase64String(part, o, Math.Min(LineBytes, part.Length - o))).Append("\r\n");
        }
        text.Append(pad).Append(">\r\n");
        if (item.Wet != 100) text.Append(pad).Append("WET ").Append((Math.Clamp(item.Wet, 0, 100) / 100.0).ToString("0.######", CultureInfo.InvariantCulture)).Append(" 0\r\n");
        if (Clean(item.PresetName) is { Length: > 0 } preset) text.Append(pad).Append("PRESETNAME ").Append(Quote(preset)).Append("\r\n");
        // A stable id per place in the chain, so the same chain exports to the same text.
        var fxId = new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes($"{ordinal}|{item.FileName}|{name}")));
        text.Append(pad).Append("FLOATPOS 0 0 0 0\r\n").Append(pad).Append("FXID ").Append(fxId.ToString("B").ToUpperInvariant()).Append("\r\n").Append(pad).Append("WAK 0 0\r\n");
        return problem;
    }

    /// <summary>
    /// Appends one CLAP plug-in as a CLAP block: <c>&lt;CLAP "CLAP: Name (Vendor)" plugin.id ""</c> (CLAPi for an instrument), then the plug-in's own state as base64 lines in a
    /// STATE block, then the lines that follow every plug-in. Returns why its settings were left out, or null.
    /// </summary>
    private static string? WriteClap(StringBuilder text, Item item, LoadedPluginInfo info, int ordinal, int depth)
    {
        string? problem = null;
        byte[]? state = null;
        if (item.State is { } own)
        {
            if (own.Length == 0) problem = "its saved settings could not be read";
            else if (own.Length > MaxStateBytes) problem = "its saved settings are too large";
            else state = own;
        }
        var name = Clean(info.Name.Length > 0 ? info.Name : item.Name).Trim();
        var vendor = Clean(info.Vendor.Length > 0 ? info.Vendor : item.Vendor).Trim();
        var pad = new string(' ', 2 * depth);
        text.Append(pad).Append(Bypass(item.Enabled, depth));
        text.Append(pad).Append("<CLAP \"").Append(item.IsInstrument ? "CLAPi" : "CLAP").Append(": ").Append(name).Append(vendor.Length > 0 ? $" ({vendor})" : "").Append("\" ")
            .Append(Quote(Clean(info.ClassId))).Append(" \"\"\r\n");
        if (state is not null)
        {
            text.Append(pad).Append("  <STATE\r\n");
            for (var o = 0; o < state.Length; o += LineBytes)
                text.Append(pad).Append("    ").Append(Convert.ToBase64String(state, o, Math.Min(LineBytes, state.Length - o))).Append("\r\n");
            text.Append(pad).Append("  >\r\n");
        }
        text.Append(pad).Append(">\r\n");
        if (item.Wet != 100) text.Append(pad).Append("WET ").Append((Math.Clamp(item.Wet, 0, 100) / 100.0).ToString("0.######", CultureInfo.InvariantCulture)).Append(" 0\r\n");
        if (Clean(item.PresetName) is { Length: > 0 } preset) text.Append(pad).Append("PRESETNAME ").Append(Quote(preset)).Append("\r\n");
        var fxId = new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes($"{ordinal}|{item.FileName}|{name}")));
        text.Append(pad).Append("FLOATPOS 0 0 0 0\r\n").Append(pad).Append("FXID ").Append(fxId.ToString("B").ToUpperInvariant()).Append("\r\n").Append(pad).Append("WAK 0 0\r\n");
        return problem;
    }

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
