using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabForge.Plugins;

/// <summary>One drum map preset: which note plays which piece. Shipped maps live in Resources/DrumMaps, the user's own under the app data folder.</summary>
public sealed record DrumMap(string Name, bool Verified, bool IsUser, IReadOnlyDictionary<int, string> Notes);

/// <summary>
/// Drum map presets (JSON: <c>{ "Name": "...", "Verified": true, "Notes": { "36": "Bass Drum 1" } }</c>) and the conversion between two of them:
/// the note of a piece in the source map becomes the note of the same-named piece in the target map.
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

    public static void Reload() { lock (Gate) _cache = null; }

    public static DrumMap? Find(string name) => string.IsNullOrEmpty(name) ? null : All().FirstOrDefault(m => string.Equals(m.Name, Current(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>The current name of a shipped map saved in older chains under its earlier name.</summary>
    private static string Current(string name) =>
        string.Equals(name, "Superior Drummer 3 (unverified)", StringComparison.OrdinalIgnoreCase) ? "Acoustic kit map (unverified)" : name;

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

    private static DrumMap? Parse(string file, bool user)
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
                    if (int.TryParse(p.Name, out var note) && note is >= 0 and <= 127 && p.Value.ValueKind == JsonValueKind.String) notes[note] = p.Value.GetString() ?? "";
            return new DrumMap(name, verified, user, notes);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "drum map: read: " + ex.Message); return null; }
    }

    /// <summary>The 128-entry note table that converts from one map to another (unnamed or unmatched notes stay as they are), then applies "36>38, 40>37" style overrides.</summary>
    public static int[] BuildTable(string from, string to, string overrides)
    {
        var table = new int[128];
        for (var i = 0; i < 128; i++) table[i] = i;
        if (Find(from) is { } source && Find(to) is { } target && !ReferenceEquals(source, target))
        {
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (note, piece) in target.Notes) byName.TryAdd(piece, note);
            foreach (var (note, piece) in source.Notes) if (byName.TryGetValue(piece, out var mapped)) table[note] = mapped;
        }
        foreach (Match m in Regex.Matches(overrides ?? "", @"(\d{1,3})\s*>\s*(\d{1,3}|-|x)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
        {
            if (!int.TryParse(m.Groups[1].Value, out var a) || a > 127) continue;
            table[a] = int.TryParse(m.Groups[2].Value, out var b) ? Math.Min(b, 127) : -1;   // "-" or "x": drop that note
        }
        return table;
    }
}
