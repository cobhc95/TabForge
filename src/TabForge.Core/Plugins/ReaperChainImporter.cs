using System.Globalization;
using System.IO;
using System.Text;
using TabForge.Audio.Contracts;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: .RfxChain pure parsing (VST2/VST3 blocks, containers, bounds; state dropped with reason on violation).
// Does not own: file/engine access or the plug-in loader.
// Tests: chain import tests.

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
    public sealed record Entry(string Name, string Vendor, string FileName, string Format, bool IsInstrument, bool Enabled, byte[]? State, string? StateProblem, int Wet = 100, string Preset = "", string ClassId = "");
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
    /// <param name="Total">Plug-ins the file named.</param>
    /// <param name="Searched">The folders the catalog was built from.</param>
    public sealed record Imported(List<PluginSlot> Chain, string Report, int Missing, int Total = 0, IReadOnlyList<string>? Searched = null)
    {
        /// <summary>The status sentence when none of the file's plug-ins was found (with the folders searched); null otherwise.</summary>
        public string? NoneFoundNotice(string chainName)
        {
            if (Chain.Count > 0 || Total == 0 || Missing != Total) return null;
            var searched = Searched is { Count: > 0 } ? " Searched: " + string.Join("; ", Searched) + "." : "";
            return (Total == 1 ? $"The plug-in in {chainName} was not found." : $"None of the {Total} plug-ins in {chainName} were found.") + searched;
        }
    }

    /// <summary>What a parse accumulates while it reads the lines of a chain file.</summary>
    private sealed class ParseState
    {
        public readonly List<Entry> Entries = new();
        public readonly List<string> Notes = new();
        public string[] Lines = Array.Empty<string>();
        public readonly Stack<Group> Open = new();   // one per open CONTAINER
        public readonly List<Group> Groups = new();
        public Group? Last;
        public int LastDepth = -1;   // the container just closed: its preset, comment and mix lines follow it
        public readonly Stack<string> DepthKinds = new();      // every open block
        public bool PendingBypass;
        public readonly SortedDictionary<string, int> Skipped = new();
        public int EntryDepth = -1;
    }

    public static Parsed Parse(string text)
    {
        var st = new ParseState();
        st.Lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < st.Lines.Length; i++)
        {
            var line = st.Lines[i].Trim();
            if (line.Length == 0) continue;
            if (line == ">") { CloseBlock(st); continue; }
            if (line.StartsWith('<')) { i = OpenBlock(st, line, i); continue; }
            ReadSettingLine(st, line);
        }
        FinishParse(st);
        return new Parsed(st.Entries, st.Notes) { Groups = st.Groups };
    }

    private static void CloseBlock(ParseState st)
    {
        st.EntryDepth = -1;
        if (st.DepthKinds.Count > 0 && st.DepthKinds.Pop() == "CONTAINER" && st.Open.Count > 0) { st.Last = st.Open.Pop(); st.Last.Count = st.Entries.Count - st.Last.Start; st.Last.Closed = true; st.LastDepth = st.DepthKinds.Count; if (ReferenceEquals(st.Last, Beyond)) st.Last = null; }
    }

    /// <summary>A block opens: a container starts a group, the pin and comment blocks are read for it, a plug-in block becomes an entry. Returns the line to continue from.</summary>
    private static int OpenBlock(ParseState st, string line, int i)
    {
        var tokens = Tokenize(line);
        var kind = tokens.Count > 0 ? tokens[0][1..] : "";
        if (kind == "CONTAINER")
        {
            // Past the depth or count bound no container is made: its plug-ins are read into what holds it.
            var group = Beyond;
            if (st.Groups.Count < ChainTree.MaxContainers && st.Open.Count < ChainTree.MaxDepth && !st.Open.Contains(Beyond))
                st.Groups.Add(group = new Group { Name = tokens.Count > 2 ? tokens[2] : "", Enabled = !st.PendingBypass, Start = st.Entries.Count, Depth = st.Open.Count });
            st.Open.Push(group); st.DepthKinds.Push("CONTAINER"); st.PendingBypass = false; st.EntryDepth = -1; st.Last = null; return i;
        }
        // Gather the block's own lines up to its closing '>' (nested blocks are skipped whole).
        var body = new List<string>(); var j = i + 1; var depth = 1;
        for (; j < st.Lines.Length; j++)
        {
            var l = st.Lines[j].Trim();
            if (l.StartsWith('<')) depth++;
            else if (l == ">") { if (--depth == 0) break; }
            else if (l.Length > 0 && depth == 1) body.Add(l);
        }
        if (kind is "IN_PINS" or "OUT_PINS" && st.Open.Count > 0)
        {
            // The container's own routing: kept as written, never interpreted.
            var box = st.Open.Peek();
            if (body.Count > 0) box.OwnRouting = true;
            if (ReferenceEquals(box, Beyond)) { return j; }
            if (!box.RoutingDropped && box.Routing.Length + body.Sum(b => b.Length + 1) + 16 < ChainTree.MaxRoutingChars) box.Routing += $"<{kind}\n{string.Concat(body.Select(b => b + "\n"))}>\n";
            else { box.Routing = ""; box.RoutingDropped = true; }
            return j;
        }
        if (kind == "COMMENT")
        {
            if (st.Last is not null && st.LastDepth == st.DepthKinds.Count && body.Sum(b => b.Length) <= 4096)
                try { st.Last.Comment = Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(body))).Trim(); } catch (FormatException) { } // swallow: a comment that is not base64 is no name
            return j;
        }
        st.Last = null;
        if (kind == "VST" && tokens.Count >= 3)
        {
            st.EntryDepth = -1;
            if (st.Entries.Count < MaxEntries) { st.Entries.Add(MakeVst(tokens, body, st.PendingBypass)); st.EntryDepth = st.DepthKinds.Count; }
        }
        else if (kind is "JS" or "CLAP" or "AU" or "DX" or "VIDEO_EFFECT" or "LV2") { st.Skipped[kind] = st.Skipped.GetValueOrDefault(kind) + 1; st.EntryDepth = -1; }
        st.PendingBypass = false;
        return j;
    }

    /// <summary>The setting lines between blocks: bypass, container routing, preset name and mix.</summary>
    private static void ReadSettingLine(ParseState st, string line)
    {
        if (line.StartsWith("BYPASS ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            st.PendingBypass = parts.Length > 1 && parts[1] == "1";
        }
        else if (line.StartsWith("CONTAINER_CFG ", StringComparison.Ordinal) && st.Open.Count > 0 && st.Open.Peek().Routing.Length == 0 && !ReferenceEquals(st.Open.Peek(), Beyond))
        {
            if (line.Length >= 256) { st.Open.Peek().RoutingDropped = true; return; }
            st.Open.Peek().Routing = line + "\n";   // only pin blocks that route (OwnRouting) earn the note, never a wider channel count alone
        }
        else if (line.StartsWith("PRESETNAME ", StringComparison.Ordinal) && Tokenize(line) is { Count: > 1 } preset && preset[1].Length <= 200)
        {
            if (st.Last is not null && st.LastDepth == st.DepthKinds.Count) st.Last.Preset = preset[1].Trim();
            else if (st.EntryDepth == st.DepthKinds.Count && st.Entries.Count > 0) st.Entries[^1] = st.Entries[^1] with { Preset = preset[1].Trim() };
        }
        else if (line.StartsWith("WET ", StringComparison.Ordinal) && st.Last is not null && st.LastDepth == st.DepthKinds.Count)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mix) && double.IsFinite(mix)) st.Last.Mix = (int)Math.Round(Math.Clamp(mix, 0, 1) * 100);
        }
        else if (line.StartsWith("WET ", StringComparison.Ordinal) && st.EntryDepth == st.DepthKinds.Count && st.Entries.Count > 0)
        {
            // The mix line follows the block it belongs to: 0..1.
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var wet) && double.IsFinite(wet))
                st.Entries[^1] = st.Entries[^1] with { Wet = (int)Math.Round(Math.Clamp(wet, 0, 1) * 100) };
        }
    }

    private static void FinishParse(ParseState st)
    {
        foreach (var (kind, count) in st.Skipped)
            st.Notes.Add(kind == "JS" ? $"{count} script effect{(count == 1 ? "" : "s")} (JS) cannot be imported." : $"{count} {kind} effect{(count == 1 ? "" : "s")} cannot be imported.");
        foreach (var g in st.Groups)
        {
            if (!g.Closed) g.Count = Math.Max(0, st.Entries.Count - g.Start);   // a container the file never closed holds the rest
            g.Named = g.Name.Length > 0 || g.Comment.Length > 0 || g.Preset.Length > 0;
            if (g.Name.Length == 0 || g.Name.Length > 200) g.Name = g.Comment.Length is > 0 and <= 200 ? g.Comment : g.Preset.Length > 0 ? g.Preset : "Container";
        }
        if (st.Groups.Count(g => g.RoutingDropped) is var dropped and > 0) st.Notes.Add($"The internal routing of {dropped} container{(dropped == 1 ? "" : "s")} is too large to keep.");
        if (st.Groups.Count(g => g.OwnRouting) is var own and > 0) st.Notes.Add($"{own} container{(own == 1 ? " uses its" : "s use their")} own internal routing; their plug-ins run in series here.");
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
        return new Entry(rest, vendor, file, vst3 ? "VST3" : "VST2", isInst, !bypass, state, problem, ClassId: vst3 ? ClassIdIn(t) : "");
    }

    /// <summary>The VST3 class id a block line carries after the file name ("...{5653...}": 32 hex digits, dashes allowed), upper case; "" when there is none.</summary>
    private static string ClassIdIn(List<string> tokens)
    {
        foreach (var token in tokens.Skip(3))
        {
            var open = token.IndexOf('{'); var close = token.IndexOf('}');
            if (open < 0 || close < open) continue;
            if (Vst3ClassId.Normalize(token[(open + 1)..close]) is { Length: > 0 } id) return id;
        }
        return "";
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

    private static byte[]? Vst2State(Block b, out string? problem)
    {
        problem = null;
        if (b.State.Length == 0) return null;
        // A parameter list is the mark and whole floats; anything else stored as "not a chunk" is unknown.
        if (!b.IsChunk && !ParameterListState.IsList(b.State))
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
    /// The known plug-in a chain entry names: the same file name (any case), else the same VST3 class id, else the same name and format
    /// (also ignoring an "x64" suffix, as in Bulldozer_x64.dll, when the vendors are the same), else the separately installed build of a plug-in that ships inside a host ("name-standalone"),
    /// else the other format (VST3 for VST2 and the reverse) of the same name and vendor.
    /// </summary>
    public static VstPluginInfo? Match(Entry e, IReadOnlyList<VstPluginInfo> catalog)
    {
        var stem = Path.GetFileNameWithoutExtension(e.FileName);
        bool Stem(VstPluginInfo c, string s) => c.Format == e.Format && string.Equals(Path.GetFileNameWithoutExtension(c.Path), s, StringComparison.OrdinalIgnoreCase);
        return catalog.FirstOrDefault(c => string.Equals(Path.GetFileName(c.Path), e.FileName, StringComparison.OrdinalIgnoreCase))
            ?? (e.ClassId.Length == 0 ? null : catalog.FirstOrDefault(c => c.Format == e.Format && Vst3ClassId.Normalize(c.ClassId) == e.ClassId))
            ?? catalog.FirstOrDefault(c => Stem(c, stem))
            ?? catalog.FirstOrDefault(c => c.Format == e.Format && VendorsAgree(e.Vendor, c.Vendor) && Bare(Path.GetFileNameWithoutExtension(c.Path)) == Bare(stem))
            ?? catalog.FirstOrDefault(c => Stem(c, stem + "-standalone") || Stem(c, stem + "_standalone"))
            ?? catalog.FirstOrDefault(c => c.Format != e.Format && SameVendor(e.Vendor, c.Vendor)   // Reaper's own fallback: the other format of the same plug-in
                && (Bare(Path.GetFileNameWithoutExtension(c.Path)) == Bare(stem) || Bare(c.Name) == Bare(e.Name)));
    }

    /// <summary>A file stem without a trailing "(x64)" or "x64" (and the separator before it), in lower case; a bare "64" stays ("M64" is not "M").</summary>
    private static string Bare(string stem)
    {
        var s = stem.Trim().ToLowerInvariant();
        foreach (var suffix in new[] { "(x64)", "x64" })
            if (s.Length > suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal)) { s = s[..^suffix.Length]; break; }
        return s.TrimEnd(' ', '_', '-');
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

    /// <summary>Two vendor names for the same maker: both known and equal ignoring case, spaces and punctuation.</summary>
    public static bool SameVendor(string a, string b) => VendorKey(a) is { Length: > 0 } x && x == VendorKey(b);

    // As SameVendor, but a vendor the file or the plug-in does not name does not tell the makers apart (same-format file-name matches only).
    private static bool VendorsAgree(string a, string b) => VendorKey(a).Length == 0 || VendorKey(b).Length == 0 || SameVendor(a, b);

    private static string VendorKey(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>True when the saved settings of <paramref name="e"/> are those of <paramref name="known"/>: the same VST3 class id, or exactly the same file name stem and a known, equal vendor.</summary>
    private static bool IsSamePlugin(Entry e, VstPluginInfo known) =>
        e.ClassId.Length > 0 && Vst3ClassId.Normalize(known.ClassId) == e.ClassId
        || string.Equals(Path.GetFileNameWithoutExtension(e.FileName), Path.GetFileNameWithoutExtension(known.Path), StringComparison.OrdinalIgnoreCase) && SameVendor(e.Vendor, known.Vendor);

    /// <summary>The chain and one grouped summary (empty when every plug-in came in whole). <paramref name="searched"/> are the folders the catalog was built from (named when a plug-in is missing).</summary>
    public static Imported Build(Parsed parsed, IReadOnlyList<VstPluginInfo> catalog, IReadOnlyList<string>? searched = null)
    {
        var chain = new List<PluginSlot>(); var missing = new List<string>(); var noState = new List<string>(); var otherFormat = new List<string>(); var over = 0;
        var placed = new List<int>();   // for each entry, its place in the chain (-1: left out)
        foreach (var e in parsed.Entries)
        {
            placed.Add(-1);
            if (chain.Count >= InputLimits.MaxPluginsPerTrack) { over++; continue; }
            var known = Match(e, catalog);
            if (known is null) { if (!missing.Contains(e.Name)) missing.Add(e.Name); continue; }
            placed[^1] = chain.Count;
            var swapped = known.Format != e.Format;
            if (swapped) otherFormat.Add($"Using the {known.Format} version of {e.Name}.");
            var slot = new PluginSlot
            {
                Name = known.Name, Path = known.Path, Format = known.Format, Vendor = known.Vendor.Length > 0 ? known.Vendor : e.Vendor,
                Type = e.IsInstrument ? PluginSlotType.Instrument : PluginSlotType.Effect, Enabled = e.Enabled, Wet = e.Wet,
                PresetName = e.Preset.Length is > 0 and <= 200 ? e.Preset : null,
            };
            var problem = e.StateProblem;
            // Settings saved by the other format are not another format's layout; the match was by name only or the maker differs or is unknown: perhaps another plug-in's. Neither is applied.
            if (e.State is { Length: > 0 } && swapped) problem = $"its saved settings are for the {e.Format} version";
            else if (e.State is { Length: > 0 } && !IsSamePlugin(e, known)) problem = "its saved settings belong to a different plug-in";
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
        if (missing.Count > 0)
            notes.Add(string.Join("\n", missing.Select(m => $"{m} isn't in TabForge's plug-in folders. Add its folder in Preferences › Plug-ins, or rescan."))
                + (searched is { Count: > 0 } ? "\nFolders searched: " + string.Join("; ", searched) + "." : ""));
        if (otherFormat.Count > 0) notes.Add(string.Join("\n", otherFormat));
        // TabForge has no wet/dry stage around a container (a plug-in's own Wet is applied): the container's mix travels with the chain only.
        if (containers.Where(c => c.Mix < 100).Select(c => $"{c.Name} ({c.Mix}%)").ToList() is { Count: > 0 } mixed)
            notes.Add($"Container mix is kept with the chain and written back on export, but not applied to the sound: {string.Join(", ", mixed)}.");
        if (noState.Count > 0) notes.Add($"Loaded with default settings: {string.Join(", ", noState)}.");
        if (over > 0) notes.Add($"A chain holds at most {InputLimits.MaxPluginsPerTrack} plug-ins: the last {over} were left out.");
        notes.AddRange(parsed.Notes);
        return new Imported(chain, string.Join("\n\n", notes), missing.Count, total, searched);
    }
}
