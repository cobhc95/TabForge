using System.Globalization;
using System.IO;
using System.Text;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// Reads FX chain files from another host (.RfxChain, text): <c>&lt;VST "VST: Name (Vendor)" file.dll 0 "" id&lt;..&gt; ""</c>
/// followed by base64 lines, each line encoded on its own. Pure parsing (no file or engine access except <see cref="Import"/>).
/// The decoded bytes of a block are, in order:
///   header: plug-in id (4), magic 0xFEED5EEE or 0xFEED5EEF (4), input count (4) + 8 bytes of pin mask per input,
///           output count (4) + 8 bytes per output, state length (4), 1 = the plug-in's own chunk / 0 = parameter list (4), 4 more bytes;
///   state:  that many bytes. VST2 chunk: what effSetChunk takes. VST2 parameter list: DEADBEEF, DEADF00D, then one float per
///           parameter (the engine's VST2 state for a plug-in without chunks). VST3: component length, 1, component state,
///           controller length, a flag, controller state; converted here to the engine's length-prefixed pair;
///   footer: two zero-terminated names (program, preset) and 4 bytes.
/// Script effects and other formats are counted and skipped. A container block keeps its name, bypass, mix, preset name and
/// nesting (<see cref="Group"/>: a run of entries); its own internal routing is kept as text and its plug-ins run in series.
/// Bounds: file 16 MiB, <see cref="MaxEntries"/> blocks, <see cref="MaxPins"/> pins a side, state up to the plug-in state limit;
/// a block that breaks a bound or is not valid base64 keeps its plug-in and drops only its state, with the reason.
/// </summary>
public static class ReaperChainImporter
{
    public const int MaxEntries = 512, MaxPins = 1024;
    private const int MaxStateBytes = TabForge.Audio.Contracts.PluginStateLimits.MaxRawBytes;

    /// <summary><paramref name="State"/> is ready for the engine's SetState; <paramref name="StateProblem"/> says why there is none.</summary>
    public sealed record Entry(string Name, string Vendor, string FileName, string Format, bool IsInstrument, bool Enabled, byte[]? State, string? StateProblem, int Wet = 100, string Preset = "");
    /// <summary>A container block: the run of <see cref="Count"/> entries from <see cref="Start"/>; outer containers come first.</summary>
    public sealed class Group
    {
        public string Name = "", Preset = "", Comment = "", Routing = "";
        public bool Enabled = true, OwnRouting, Closed, Named, RoutingDropped;
        public int Mix = 100, Start, Count, Depth;
    }
    private static readonly Group Beyond = new();   // stands for every container past the bounds
    public sealed record Parsed(List<Entry> Entries, List<string> Notes) { public List<Group> Groups { get; init; } = new(); }
    /// <summary>One decoded state block. <paramref name="State"/> is the bytes as stored in the file.</summary>
    public sealed record Block(uint Id, uint Magic, int Inputs, int Outputs, bool IsChunk, byte[] State, string PresetName);
    public sealed record Imported(List<PluginSlot> Chain, string Report, int Missing);

    public static Parsed Parse(string text)
    {
        var entries = new List<Entry>(); var notes = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var open = new Stack<Group>();   // one per open CONTAINER
        var groups = new List<Group>(); Group? last = null; var lastDepth = -1;   // the container just closed: its preset, comment and mix lines follow it
        var depthKinds = new Stack<string>();      // every open '<' block
        var pendingBypass = false; var skipped = new SortedDictionary<string, int>(); var entryDepth = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line == ">")
            {
                entryDepth = -1;
                if (depthKinds.Count > 0 && depthKinds.Pop() == "CONTAINER" && open.Count > 0) { last = open.Pop(); last.Count = entries.Count - last.Start; last.Closed = true; lastDepth = depthKinds.Count; if (ReferenceEquals(last, Beyond)) last = null; }
                continue;
            }
            if (line.StartsWith('<'))
            {
                var tokens = Tokenize(line);
                var kind = tokens.Count > 0 ? tokens[0][1..] : "";
                if (kind == "CONTAINER")
                {
                    // Past the depth or count bound no container is made: its plug-ins are read into what holds it.
                    var group = Beyond;
                    if (groups.Count < ChainTree.MaxContainers && open.Count < ChainTree.MaxDepth && !open.Contains(Beyond))
                        groups.Add(group = new Group { Name = tokens.Count > 2 ? tokens[2] : "", Enabled = !pendingBypass, Start = entries.Count, Depth = open.Count });
                    open.Push(group); depthKinds.Push("CONTAINER"); pendingBypass = false; entryDepth = -1; last = null; continue;
                }
                // Gather the block's own lines up to its closing '>' (nested blocks are skipped whole).
                var body = new List<string>(); var j = i + 1; var depth = 1;
                for (; j < lines.Length; j++)
                {
                    var l = lines[j].Trim();
                    if (l.StartsWith('<')) depth++;
                    else if (l == ">") { if (--depth == 0) break; }
                    else if (l.Length > 0 && depth == 1) body.Add(l);
                }
                if (kind is "IN_PINS" or "OUT_PINS" && open.Count > 0)
                {
                    // The container's own routing: kept as written, never interpreted.
                    var box = open.Peek();
                    if (body.Count > 0) box.OwnRouting = true;
                    if (ReferenceEquals(box, Beyond)) { i = j; continue; }
                    if (!box.RoutingDropped && box.Routing.Length + body.Sum(b => b.Length + 1) + 16 < ChainTree.MaxRoutingChars) box.Routing += $"<{kind}\n{string.Concat(body.Select(b => b + "\n"))}>\n";
                    else { box.Routing = ""; box.RoutingDropped = true; }
                    i = j; continue;
                }
                if (kind == "COMMENT")
                {
                    if (last is not null && lastDepth == depthKinds.Count && body.Sum(b => b.Length) <= 4096)
                        try { last.Comment = Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(body))).Trim(); } catch (FormatException) { } // Not logged: a comment that is not base64 is no name
                    i = j; continue;
                }
                last = null;
                if (kind == "VST" && tokens.Count >= 3)
                {
                    entryDepth = -1;
                    if (entries.Count < MaxEntries) { entries.Add(MakeVst(tokens, body, pendingBypass)); entryDepth = depthKinds.Count; }
                }
                else if (kind is "JS" or "CLAP" or "AU" or "DX" or "VIDEO_EFFECT" or "LV2") { skipped[kind] = skipped.GetValueOrDefault(kind) + 1; entryDepth = -1; }
                pendingBypass = false;
                i = j;
                continue;
            }
            if (line.StartsWith("BYPASS ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                pendingBypass = parts.Length > 1 && parts[1] == "1";
            }
            else if (line.StartsWith("CONTAINER_CFG ", StringComparison.Ordinal) && open.Count > 0 && open.Peek().Routing.Length == 0 && !ReferenceEquals(open.Peek(), Beyond))
            {
                if (line.Length >= 256) { open.Peek().RoutingDropped = true; continue; }
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                open.Peek().Routing = line + "\n";
                if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channels) && channels > 2) open.Peek().OwnRouting = true;
            }
            else if (line.StartsWith("PRESETNAME ", StringComparison.Ordinal) && Tokenize(line) is { Count: > 1 } preset && preset[1].Length <= 200)
            {
                if (last is not null && lastDepth == depthKinds.Count) last.Preset = preset[1].Trim();
                else if (entryDepth == depthKinds.Count && entries.Count > 0) entries[^1] = entries[^1] with { Preset = preset[1].Trim() };
            }
            else if (line.StartsWith("WET ", StringComparison.Ordinal) && last is not null && lastDepth == depthKinds.Count)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mix) && double.IsFinite(mix)) last.Mix = (int)Math.Round(Math.Clamp(mix, 0, 1) * 100);
            }
            else if (line.StartsWith("WET ", StringComparison.Ordinal) && entryDepth == depthKinds.Count && entries.Count > 0)
            {
                // The mix line follows the block it belongs to: 0..1.
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var wet) && double.IsFinite(wet))
                    entries[^1] = entries[^1] with { Wet = (int)Math.Round(Math.Clamp(wet, 0, 1) * 100) };
            }
        }
        foreach (var (kind, count) in skipped)
            notes.Add(kind == "JS" ? $"{count} script effect{(count == 1 ? "" : "s")} (JS) cannot be imported." : $"{count} {kind} effect{(count == 1 ? "" : "s")} cannot be imported.");
        foreach (var g in groups)
        {
            if (!g.Closed) g.Count = Math.Max(0, entries.Count - g.Start);   // a container the file never closed holds the rest
            g.Named = g.Name.Length > 0 || g.Comment.Length > 0 || g.Preset.Length > 0;
            if (g.Name.Length == 0 || g.Name.Length > 200) g.Name = g.Comment.Length is > 0 and <= 200 ? g.Comment : g.Preset.Length > 0 ? g.Preset : "Container";
        }
        if (groups.Count(g => g.RoutingDropped) is var dropped and > 0) notes.Add($"The internal routing of {dropped} container{(dropped == 1 ? "" : "s")} is too large to keep.");
        if (groups.Count(g => g.OwnRouting) is var own and > 0) notes.Add($"{own} container{(own == 1 ? " uses its" : "s use their")} own internal routing; their plug-ins run in series here.");
        return new Parsed(entries, notes) { Groups = groups };
    }

    private static Entry MakeVst(List<string> t, List<string> b64, bool bypass)
    {
        var label = t[1];
        var isInst = label.StartsWith("VSTi:", StringComparison.OrdinalIgnoreCase) || label.StartsWith("VST3i:", StringComparison.OrdinalIgnoreCase);
        var vst3 = label.StartsWith("VST3", StringComparison.OrdinalIgnoreCase);
        var rest = label[(label.IndexOf(':') + 1)..].Trim();
        var vendor = "";
        if (rest.EndsWith(')') && rest.LastIndexOf('(') is var p and > 0) { vendor = rest[(p + 1)..^1].Trim(); rest = rest[..p].Trim(); }
        var file = t[2];
        if (file.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) vst3 = true;
        byte[]? state = null; string? problem = null;
        if (b64.Count > 0)
        {
            var block = DecodeBlock(b64, out problem);
            if (block is not null) state = vst3 ? Vst3State(block.State, out problem) : Vst2State(block, out problem);
        }
        return new Entry(rest, vendor, file, vst3 ? "VST3" : "VST2", isInst, !bypass, state, problem);
    }

    /// <summary>Decodes one block's base64 lines into header fields, the stored state and the preset name; null with the reason when it is malformed.</summary>
    public static Block? DecodeBlock(IReadOnlyList<string> b64Lines, out string? problem)
    {
        problem = null;
        long chars = 0;
        foreach (var l in b64Lines) chars += l.Length;
        if (chars > InputLimits.MaxPluginStateChars + 65536L) { problem = "its saved settings are too large"; return null; }
        byte[] d;
        try
        {
            using var all = new MemoryStream((int)(chars / 4 * 3));
            foreach (var l in b64Lines) all.Write(Convert.FromBase64String(l));
            d = all.ToArray();
        }
        catch (FormatException) { problem = "its saved settings are not valid base64"; return null; } // Not logged: reported to the user in the import summary
        problem = "its saved settings have an unknown layout";
        if (d.Length < 28) return null;
        var magic = BitConverter.ToUInt32(d, 4);
        if (magic is not (0xFEED5EEE or 0xFEED5EEF)) return null;
        long o = 8;
        var counts = new int[2];
        for (var side = 0; side < 2; side++)
        {
            if (o + 4 > d.Length) return null;
            var n = BitConverter.ToInt32(d, (int)o);
            if (n < 0 || n > MaxPins) return null;
            counts[side] = n;
            o += 4 + 8L * n;
        }
        if (o + 12 > d.Length) return null;
        var len = BitConverter.ToInt32(d, (int)o);
        var kind = BitConverter.ToInt32(d, (int)o + 4);
        o += 12;
        if (len < 0 || o + len > d.Length) return null;
        if (len > MaxStateBytes) { problem = "its saved settings are too large"; return null; }
        var state = d.AsSpan((int)o, len).ToArray();
        // Footer: program name, preset name, each zero-terminated.
        var foot = d.AsSpan((int)o + len);
        var first = foot.IndexOf((byte)0);
        var name = "";
        if (first >= 0 && foot[(first + 1)..].IndexOf((byte)0) is var second and >= 0) name = Encoding.UTF8.GetString(foot.Slice(first + 1, Math.Min(second, 256)));
        problem = null;
        return new Block(BitConverter.ToUInt32(d, 0), magic, counts[0], counts[1], kind != 0, state, name);
    }

    private static ReadOnlySpan<byte> ParameterListMark => new byte[] { 0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE };

    private static byte[]? Vst2State(Block b, out string? problem)
    {
        problem = null;
        if (b.State.Length == 0) return null;
        // A parameter list is the mark and whole floats; anything else stored as "not a chunk" is unknown.
        if (!b.IsChunk && !(b.State.Length >= 12 && (b.State.Length - 8) % 4 == 0 && b.State.AsSpan(0, 8).SequenceEqual(ParameterListMark)))
        { problem = "its saved settings have an unknown layout"; return null; }
        return b.State;
    }

    /// <summary>Stored VST3 state (length, 1, component, length, flag, controller) to the engine's (length, component, length, controller).</summary>
    public static byte[]? Vst3State(byte[] s, out string? problem)
    {
        problem = "its saved settings have an unknown layout";
        if (s.Length == 0) { problem = null; return null; }
        if (s.Length < 16) return null;
        long cs = BitConverter.ToUInt32(s, 0);
        if (cs + 16 > s.Length) return null;
        long ks = BitConverter.ToUInt32(s, (int)cs + 8);
        if (cs + ks + 16 > s.Length) return null;
        var result = new byte[cs + ks + 8];
        BitConverter.TryWriteBytes(result.AsSpan(0, 4), (uint)cs);
        s.AsSpan(8, (int)cs).CopyTo(result.AsSpan(4));
        BitConverter.TryWriteBytes(result.AsSpan((int)cs + 4, 4), (uint)ks);
        s.AsSpan((int)cs + 16, (int)ks).CopyTo(result.AsSpan((int)cs + 8));
        problem = null;
        return result;
    }

    private static List<string> Tokenize(string line)
    {
        var list = new List<string>(); var sb = new StringBuilder(); var quoted = false; var has = false;
        foreach (var c in line)
        {
            if (c == '"') { quoted = !quoted; has = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted) { if (has) { list.Add(sb.ToString()); sb.Clear(); has = false; } continue; }
            sb.Append(c); has = true;
        }
        if (has) list.Add(sb.ToString());
        return list;
    }

    /// <summary>
    /// The known plug-in a chain entry names: the same file name (any case), else the same name and format, else the separately
    /// installed build of a plug-in that ships inside a host ("name-standalone").
    /// </summary>
    public static VstPluginInfo? Match(Entry e, IReadOnlyList<VstPluginInfo> catalog)
    {
        var stem = Path.GetFileNameWithoutExtension(e.FileName);
        bool Stem(VstPluginInfo c, string s) => c.Format == e.Format && string.Equals(Path.GetFileNameWithoutExtension(c.Path), s, StringComparison.OrdinalIgnoreCase);
        return catalog.FirstOrDefault(c => string.Equals(Path.GetFileName(c.Path), e.FileName, StringComparison.OrdinalIgnoreCase))
            ?? catalog.FirstOrDefault(c => Stem(c, stem))
            ?? catalog.FirstOrDefault(c => Stem(c, stem + "-standalone") || Stem(c, stem + "_standalone"));
    }

    /// <summary>Reads a file and maps its plug-ins onto <paramref name="catalog"/> (the plug-ins TabForge knows). Returns null when unreadable.</summary>
    public static Imported? Import(string path, IEnumerable<VstPluginInfo> catalog) => Read(path) is { } parsed ? Build(parsed, catalog.ToList()) : null;

    /// <summary>Reads and parses a file. Null when it is unreadable or holds nothing a chain file holds (no plug-in block, nothing skipped).</summary>
    public static Parsed? Read(string path)
    {
        string text;
        try { text = Encoding.UTF8.GetString(InputLimits.ReadBoundedBytes(path, 16L * 1024 * 1024, "FX chain file")); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Import, "FX chain import: read: " + ex.Message); return null; }
        return Recognised(Parse(text));
    }

    /// <summary>Null for a text that is no chain file: no plug-in block and nothing skipped.</summary>
    public static Parsed? Recognised(Parsed parsed) => parsed.Entries.Count == 0 && parsed.Notes.Count == 0 ? null : parsed;

    /// <summary>Two vendor names for the same maker: equal, or one holds the other, ignoring case, spaces and punctuation ("Acme" / "Acme Audio Ltd.").</summary>
    public static bool SameVendor(string a, string b)
    {
        static string Key(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var (x, y) = (Key(a), Key(b));
        return x.Length == 0 || y.Length == 0 || x.Contains(y) || y.Contains(x);
    }

    /// <summary>The chain and one grouped summary (empty when every plug-in came in whole).</summary>
    public static Imported Build(Parsed parsed, IReadOnlyList<VstPluginInfo> catalog)
    {
        var chain = new List<PluginSlot>(); var missing = new List<string>(); var noState = new List<string>(); var over = 0;
        var placed = new List<int>();   // for each entry, its place in the chain (-1: left out)
        foreach (var e in parsed.Entries)
        {
            placed.Add(-1);
            if (chain.Count >= InputLimits.MaxPluginsPerTrack) { over++; continue; }
            var known = Match(e, catalog);
            if (known is null) { if (!missing.Contains(e.Name)) missing.Add(e.Name); continue; }
            placed[^1] = chain.Count;
            var slot = new PluginSlot
            {
                Name = known.Name, Path = known.Path, Format = known.Format, Vendor = known.Vendor.Length > 0 ? known.Vendor : e.Vendor,
                Type = e.IsInstrument ? PluginSlotType.Instrument : PluginSlotType.Effect, Enabled = e.Enabled, Wet = e.Wet,
                PresetName = e.Preset.Length is > 0 and <= 200 ? e.Preset : null,
            };
            var problem = e.StateProblem;
            // The file name matched, the maker did not: the saved settings are another plug-in's and are not applied.
            if (e.State is { Length: > 0 } && !SameVendor(e.Vendor, known.Vendor)) problem = "its saved settings belong to a different plug-in";
            else if (e.State is { Length: > 0 })
            {
                var s = Convert.ToBase64String(e.State);
                if (s.Length <= InputLimits.MaxPluginStateChars) slot.State = s; else problem = "its saved settings are too large";
            }
            if (problem is not null) noState.Add($"{e.Name} ({problem})");
            chain.Add(slot);
        }
        // A container keeps the plug-ins of its run that came in.
        var containers = new List<ChainContainer>();
        foreach (var g in parsed.Groups)
        {
            var start = placed.Take(Math.Min(g.Start, placed.Count)).Count(i => i >= 0);
            var count = placed.Skip(g.Start).Take(g.Count).Count(i => i >= 0);
            // An empty container stays when the file named it; an unnamed empty one goes (what was inside it in the file is not here).
            if (count > 0 || g.Named) containers.Add(new ChainContainer { Name = g.Name, Enabled = g.Enabled, Mix = g.Mix, Preset = g.Preset.Length > 0 ? g.Preset : null, Routing = g.Routing.Length > 0 ? g.Routing : null, Start = start, Count = count, Depth = g.Depth });
        }
        if (chain.Count > 0) ChainTree.Pack(chain, containers);
        var notes = new List<string>();
        var total = parsed.Entries.Count;
        if (missing.Count > 0 || noState.Count > 0 || over > 0 || parsed.Notes.Count > 0) notes.Add($"Imported {chain.Count} of {total} plug-in{(total == 1 ? "" : "s")}.");
        if (missing.Count > 0) notes.Add($"Not in your plug-in list: {string.Join(", ", missing)}. Open Add plug-in, add or scan the folder that holds them, then load the chain again.");
        if (noState.Count > 0) notes.Add($"Loaded with default settings: {string.Join(", ", noState)}.");
        if (over > 0) notes.Add($"A chain holds at most {InputLimits.MaxPluginsPerTrack} plug-ins: the last {over} were left out.");
        notes.AddRange(parsed.Notes);
        return new Imported(chain, string.Join("\n\n", notes), missing.Count);
    }
}
