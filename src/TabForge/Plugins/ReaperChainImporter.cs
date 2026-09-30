using System.IO;
using System.Text;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// Reads REAPER FX chain files (.RfxChain, text): <c>&lt;VST "VST: Name (Vendor)" file.dll 0 "" id&lt;..&gt; ""</c> followed by base64 lines.
/// Pure parsing (no file or engine access except <see cref="Import"/>). VST2: the first base64 line is REAPER's header
/// (uid, magic 0xFEED5EEE, pin data, then chunk length, 1, 0, 0x100000 as its last 16 bytes); the next lines hold the
/// plug-in's own chunk (that many bytes) plus a small footer, so the chunk is what our engine's SetState wants.
/// VST3 state and REAPER JS effects are not imported (JS skipped, VST3 loaded without state).
/// </summary>
public static class ReaperChainImporter
{
    public sealed record Entry(string Name, string Vendor, string FileName, string Format, bool IsInstrument, bool Enabled, byte[]? Chunk);
    public sealed record Parsed(List<Entry> Entries, List<string> Notes);

    public static Parsed Parse(string text)
    {
        var entries = new List<Entry>(); var notes = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var containerBypass = new Stack<bool>();   // one per open CONTAINER
        var depthKinds = new Stack<string>();      // every open '<' block
        var pendingBypass = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line == ">") { if (depthKinds.Count > 0 && depthKinds.Pop() == "CONTAINER" && containerBypass.Count > 0) containerBypass.Pop(); continue; }
            if (line.StartsWith('<'))
            {
                var tokens = Tokenize(line);
                var kind = tokens.Count > 0 ? tokens[0][1..] : "";
                if (kind == "CONTAINER") { containerBypass.Push(pendingBypass || containerBypass.Any(b => b)); depthKinds.Push("CONTAINER"); pendingBypass = false; continue; }
                if (kind == "VST" && tokens.Count >= 3)
                {
                    // Gather base64 lines up to the block's closing '>' at this level.
                    var b64 = new List<string>(); var j = i + 1;
                    for (; j < lines.Length; j++)
                    {
                        var l = lines[j].Trim();
                        if (l == ">") break;
                        if (l.Length > 0) b64.Add(l);
                    }
                    entries.Add(MakeVst(tokens, b64, pendingBypass || containerBypass.Any(b => b), notes));
                    pendingBypass = false;
                    i = j;   // skip the block
                    continue;
                }
                if (kind == "JS")
                {
                    notes.Add($"REAPER JS effect skipped: {(tokens.Count > 1 ? tokens[1] : "?")}");
                    var j = i + 1; var depth = 1;
                    for (; j < lines.Length && depth > 0; j++) { var l = lines[j].Trim(); if (l.StartsWith('<')) depth++; else if (l == ">") depth--; }
                    i = j - 1; pendingBypass = false;
                    continue;
                }
                // Unknown block (AU, CLAP, IN_PINS...): skip it whole.
                {
                    var j = i + 1; var depth = 1;
                    for (; j < lines.Length && depth > 0; j++) { var l = lines[j].Trim(); if (l.StartsWith('<')) depth++; else if (l == ">") depth--; }
                    if (kind is "CLAP" or "AU" or "DX" or "VIDEO_EFFECT") notes.Add($"{kind} effect skipped: {(tokens.Count > 1 ? tokens[1] : "?")}");
                    i = j - 1; pendingBypass = false;
                }
                continue;
            }
            if (line.StartsWith("BYPASS ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                pendingBypass = parts.Length > 1 && parts[1] == "1";
            }
        }
        return new Parsed(entries, notes);
    }

    private static Entry MakeVst(List<string> t, List<string> b64, bool bypass, List<string> notes)
    {
        var label = t[1];
        var isInst = label.StartsWith("VSTi:", StringComparison.OrdinalIgnoreCase) || label.StartsWith("VST3i:", StringComparison.OrdinalIgnoreCase);
        var vst3 = label.StartsWith("VST3", StringComparison.OrdinalIgnoreCase);
        var rest = label[(label.IndexOf(':') + 1)..].Trim();
        var vendor = "";
        if (rest.EndsWith(')') && rest.LastIndexOf('(') is var p and > 0) { vendor = rest[(p + 1)..^1].Trim(); rest = rest[..p].Trim(); }
        var file = t[2];
        if (file.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) vst3 = true;
        byte[]? chunk = null;
        if (!vst3)
        {
            chunk = ExtractVst2Chunk(b64);
            if (chunk is null && b64.Count > 0) notes.Add($"{rest}: state not imported");
        }
        else notes.Add($"{rest}: state not imported (VST3)");
        return new Entry(rest, vendor, file, vst3 ? "VST3" : "VST2", isInst, !bypass, chunk);
    }

    /// <summary>Skips REAPER's header (first base64 line), returns the plug-in chunk; null when the layout is not as expected.</summary>
    public static byte[]? ExtractVst2Chunk(IReadOnlyList<string> b64Lines)
    {
        if (b64Lines.Count < 2) return null;
        try
        {
            var header = Convert.FromBase64String(b64Lines[0]);
            if (header.Length < 24 || BitConverter.ToUInt32(header, 4) != 0xFEED5EEE) return null;
            var len = BitConverter.ToInt32(header, header.Length - 16);
            var tail = BitConverter.ToInt32(header, header.Length - 4);
            if (len <= 0 || len > InputLimits.MaxPluginStateChars / 2 * 3 || tail != 0x100000) return null;
            var body = new List<byte>();
            for (var i = 1; i < b64Lines.Count; i++) body.AddRange(Convert.FromBase64String(b64Lines[i]));
            if (body.Count < len) return null;
            return body.Take(len).ToArray();
        }
        catch (FormatException) { return null; }
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

    /// <summary>Reads a file and maps its plug-ins onto <paramref name="catalog"/> by file name. Returns null when unreadable.</summary>
    public static (List<PluginSlot> Chain, string Report)? Import(string path, IEnumerable<VstPluginInfo> catalog)
    {
        string text;
        try { text = Encoding.UTF8.GetString(InputLimits.ReadBoundedBytes(path, 16L * 1024 * 1024, "REAPER FX chain")); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
        var parsed = Parse(text);
        var cat = catalog.ToList();
        var chain = new List<PluginSlot>(); var notes = new List<string>(parsed.Notes);
        foreach (var e in parsed.Entries)
        {
            if (chain.Count >= InputLimits.MaxPluginsPerTrack) { notes.Add("Chain limit reached; the rest was skipped."); break; }
            var byFile = cat.FirstOrDefault(c => string.Equals(Path.GetFileName(c.Path), e.FileName, StringComparison.OrdinalIgnoreCase))
                ?? cat.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c.Path), Path.GetFileNameWithoutExtension(e.FileName), StringComparison.OrdinalIgnoreCase) && c.Format == e.Format);
            if (byFile is null) { notes.Add($"Not found in your plug-in list, skipped: {e.Name} ({e.FileName})"); continue; }
            var slot = new PluginSlot
            {
                Name = byFile.Name, Path = byFile.Path, Format = byFile.Format, Vendor = byFile.Vendor.Length > 0 ? byFile.Vendor : e.Vendor,
                Type = e.IsInstrument ? PluginSlotType.Instrument : PluginSlotType.Effect, Enabled = e.Enabled,
            };
            if (e.Chunk is { Length: > 0 })
            {
                var s = Convert.ToBase64String(e.Chunk);
                if (s.Length <= InputLimits.MaxPluginStateChars) slot.State = s; else notes.Add($"{e.Name}: state too large, not imported");
            }
            chain.Add(slot);
        }
        return (chain, string.Join("\n", notes));
    }

    /// <summary>Self-test on a synthetic chain; null when it passes, else what failed.</summary>
    public static string? SelfTestFailure()
    {
        var chunk = new byte[] { 1, 2, 3, 4, 5 };
        var header = new List<byte>();
        header.AddRange(BitConverter.GetBytes(0x74736574)); header.AddRange(BitConverter.GetBytes(0xFEED5EEE));
        header.AddRange(new byte[8]); header.AddRange(BitConverter.GetBytes(chunk.Length)); header.AddRange(BitConverter.GetBytes(1)); header.AddRange(BitConverter.GetBytes(0)); header.AddRange(BitConverter.GetBytes(0x100000));
        var body = chunk.Concat(new byte[] { 0, 0x10, 0, 0 }).ToArray();
        var text = "BYPASS 0 0\n<VST \"VST: Test EQ (Acme)\" test.dll 0 \"\" 1<00> \"\"\n  " + Convert.ToBase64String(header.ToArray()) + "\n  " + Convert.ToBase64String(body) + "\n>\n"
            + "BYPASS 1 0\n<VST \"VST3: Synth (Acme)\" Synth.vst3 0 \"\" 2<00> \"\"\n>\n<JS midi/foo \"\"\n  0 1\n>\n";
        var p = Parse(text);
        if (p.Entries.Count != 2) return $"expected 2 entries, got {p.Entries.Count}";
        var a = p.Entries[0]; var b = p.Entries[1];
        if (a.Name != "Test EQ" || a.Vendor != "Acme" || a.Format != "VST2" || !a.Enabled) return "first entry fields";
        if (a.Chunk is null || !a.Chunk.SequenceEqual(chunk)) return "VST2 chunk not extracted";
        if (b.Format != "VST3" || b.Enabled || b.Chunk is not null) return "second entry fields";
        if (!p.Notes.Any(n => n.Contains("JS"))) return "JS note missing";
        return null;
    }
}
