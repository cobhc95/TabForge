using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabForge.Plugins;

// Owns: drum map presets and source-to-target note conversion (notes plus Remap table).
// Does not own: the shipped Resources/DrumMaps files or the engine-side mapping.
// Tests: drum map tests.

/// <summary>
/// One drum map preset: which note plays which piece. Shipped maps live in Resources/DrumMaps, the user's own under the app data folder.
/// <paramref name="Remap"/> is the map's optional note-to-note table (incoming note, note sent; -1: not sent), used when the map is the target.
/// </summary>
public sealed record DrumMap(string Name, bool Verified, bool IsUser, IReadOnlyDictionary<int, string> Notes, IReadOnlyDictionary<int, int>? Remap = null);

/// <summary>
/// Drum map presets (JSON: <c>{ "Name": "...", "Verified": true, "Notes": { "36": "Bass Drum 1" }, "Remap": { "40": 38 } }</c>) and the
/// conversion between two of them: the note of a piece in the source map becomes the note of the same-named piece in the target map,
/// then the target map's optional "Remap" (incoming note to note sent, whatever the pieces are called) is applied.
/// </summary>
public static class DrumMapLibrary
{
    public const string None = "";
    private static List<DrumMap>? _cache;
    private static readonly object Gate = new();

    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "Resources", "DrumMaps");
    public static string UserFolder => Path.Combine(PluginLibrary.Root, "DrumMaps");

    /// <summary>Every map, shipped first (General MIDI at the top), then the user's; cached until <see cref="Reload"/>.</summary>
    public static IReadOnlyList<DrumMap> All()
    {
        lock (Gate) return _cache ??= Load();
    }

    /// <summary>The names of the maps the app ships with (the maps that have no user file).</summary>
    public static IReadOnlyList<string> BuiltInNames() => All().Where(m => !m.IsUser).Select(m => m.Name).ToList();

    public static void Reload() { lock (Gate) _cache = null; }

    public static DrumMap? Find(string name) => string.IsNullOrEmpty(name) ? null : All().FirstOrDefault(m => string.Equals(m.Name, Current(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>The current name of the shipped map, for chains saved under one of its earlier names.</summary>
    private static string Current(string name) =>
        string.Equals(name, "Superior Drummer 3 (unverified)", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Acoustic kit map (unverified)", StringComparison.OrdinalIgnoreCase) ? "Tabs to SD3" : name;

    private static List<DrumMap> Load()
    {
        var list = new List<DrumMap>();
        AddFolder(list, ShippedFolder, user: false);
        AddFolder(list, UserFolder, user: true);
        return list.OrderBy(m => m.IsUser).ThenByDescending(m => m.Verified).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddFolder(List<DrumMap> into, string folder, bool user)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(200))
                if (Parse(file, user) is { } map && !into.Any(m => m.Name == map.Name)) into.Add(map);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Engine, "drum maps: read folder: " + ex.Message); }
    }

    /// <summary>Reads one map file; null when it is no readable map.</summary>
    internal static DrumMap? Parse(string file, bool user)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > 256 * 1024) return null;
            using var doc = JsonDocument.Parse(TabForge.Services.InputLimits.ReadBoundedBytes(file, 256 * 1024, "drum map"));
            var root = doc.RootElement;
            var name = root.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : Path.GetFileNameWithoutExtension(file);
            if (name.Length is 0 or > 64) return null;
            var verified = root.TryGetProperty("Verified", out var v) && v.ValueKind == JsonValueKind.True;
            var notes = new Dictionary<int, string>();
            if (root.TryGetProperty("Notes", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
                foreach (var p in nodes.EnumerateObject())
                    if (int.TryParse(p.Name, out var note) && note is >= 0 and <= MidiLimits.Max && p.Value.ValueKind == JsonValueKind.String) notes[note] = p.Value.GetString() ?? "";
            Dictionary<int, int>? remap = null;
            if (root.TryGetProperty("Remap", out var pairs) && pairs.ValueKind == JsonValueKind.Object)
                foreach (var p in pairs.EnumerateObject())
                    if (int.TryParse(p.Name, out var note) && note is >= 0 and <= MidiLimits.Max && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var sent) && sent is >= -1 and <= MidiLimits.Max)
                        (remap ??= new())[note] = sent;
            return new DrumMap(name, verified, user, notes, remap);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "drum map: read: " + ex.Message); return null; }
    }

    /// <summary>The 128-entry note table that converts from one map to another (unnamed or unmatched notes stay as they are), then applies "36>38, 40>37" style overrides.</summary>
    public static int[] BuildTable(string from, string to, string overrides)
    {
        var table = new int[128];
        for (var i = 0; i < 128; i++) table[i] = i;
        var target = Find(to);
        if (Find(from) is { } source && target is not null && !ReferenceEquals(source, target))
        {
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (note, piece) in target.Notes) byName.TryAdd(piece, note);
            foreach (var (note, piece) in source.Notes) if (byName.TryGetValue(piece, out var mapped)) table[note] = mapped;
        }
        if (target?.Remap is { } remap) foreach (var (note, sent) in remap) table[note] = sent;   // the target map's own note table
        foreach (Match m in Regex.Matches(overrides ?? "", @"(\d{1,3})\s*>\s*(\d{1,3}|-|x)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
        {
            if (!int.TryParse(m.Groups[1].Value, out var a) || a > MidiLimits.Max) continue;
            table[a] = int.TryParse(m.Groups[2].Value, out var b) ? Math.Min(b, MidiLimits.Max) : -1;   // "-" or "x": drop that note
        }
        return table;
    }

    /// <summary>What a note map does, in words: the notes it changes (at most <paramref name="max"/>, then "+ N more"), one per line.</summary>
    public static string Describe(string from, string to, string overrides, int max = 4)
    {
        var table = BuildTable(from, to, overrides);
        var names = (Find(from) ?? Find("General MIDI"))?.Notes;
        var changes = Enumerable.Range(0, 128).Where(n => table[n] != n)
            .Select(n => $"{n}{(names is not null && names.TryGetValue(n, out var piece) ? " " + piece : "")} → {(table[n] < 0 ? "not sent" : table[n].ToString())}").ToList();
        if (changes.Count == 0) return "Every note is sent unchanged.";
        return "Notes are sent unchanged except:\n" + string.Join("\n", changes.Take(max)) + (changes.Count > max ? $"\n+ {changes.Count - max} more" : "");
    }
}
