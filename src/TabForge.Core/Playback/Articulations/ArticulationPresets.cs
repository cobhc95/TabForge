using System.IO;
using System.Text.Json;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the articulation presets: the two built in, the JSON file form (read with limits, written atomically), and the folder of the user presets.
// Does not own: the map itself (ArticulationMap), the editor (Views/Articulations) or the file dialogs.
// Tests: TestArticulationKeyswitches.

/// <summary>A preset as it is stored in a .json file: a name, a description, the lead and the rules as technique id to encoded rule.</summary>
public sealed class ArticulationPreset
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int LeadMs { get; set; } = ArticulationMap.DefaultLeadMs;
    public Dictionary<string, string> Rules { get; set; } = new();

    public ArticulationMap ToMap()
    {
        var map = ArticulationMap.FromBindings(Rules);
        map.LeadMs = Math.Clamp(LeadMs, 0, ArticulationMap.MaxLeadMs);
        return map;
    }

    public static ArticulationPreset From(string name, string description, ArticulationMap map) =>
        new() { Name = name, Description = description, LeadMs = map.LeadMs, Rules = map.Rules.ToDictionary(p => p.Key, p => p.Value.Encode()) };
}

public static class ArticulationPresets
{
    public const string GenericName = "Generic (none)";
    public const string AmpleName = "Ample Sound guitar";
    public const string FileExtension = ".json";
    private const long MaxFileBytes = 64 * 1024;
    private const int MaxNameChars = 64, MaxDescriptionChars = 512;

    /// <summary>The user presets, one .json file each.</summary>
    public static PresetFolderStore UserStore() => new(Path.Combine(PluginLibrary.Root, "Articulations"), FileExtension, bytes => Parse(bytes) is not null);

    /// <summary>The presets that come with the app. Ample Sound guitars switch articulation with keyswitch notes at the bottom of the keyboard.</summary>
    public static IReadOnlyList<ArticulationPreset> BuiltIn { get; } = new[]
    {
        new ArticulationPreset { Name = GenericName, Description = "No articulation messages: notes play as written." },
        Ample(),
    };

    private static ArticulationPreset Ample()
    {
        // Assumption to verify against the instrument: the Ample Sound manuals number octaves so that C0 = MIDI 24 (middle C is C3 = 60),
        // so the keyswitches C0..F0 are the notes 24..29. A library that numbers C-1 = 0 reads the same notes one octave higher.
        var map = new ArticulationMap();
        void Key(string id, int note) => map.Rules[id] = new ArticulationRule(ArticulationAction.Keyswitch, note, 0, ArticulationMode.Latched);
        Key(ArticulationTechniques.Normal, 24);
        Key(Models.TechniqueNames.Harmonic, 25);
        Key(Models.TechniqueNames.PalmMute, 26);
        Key("Slide", 27);
        Key(Models.TechniqueNames.LegatoSlide, 28);
        Key("HammerPull", 29);
        return ArticulationPreset.From(AmpleName,
            "Keyswitches (octave numbering with C0 = MIDI 24, to be verified against the instrument): C0 normal / sustain, C#0 natural harmonic, D0 palm mute, D#0 slide in / out, E0 legato slide, F0 hammer-on / pull-off.", map);
    }

    public static ArticulationPreset? FindBuiltIn(string name) => BuiltIn.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a preset file bytes; null for anything that is not a preset (sizes are capped, and every rule is checked when the map is built).</summary>
    public static ArticulationPreset? Parse(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes) return null;
        try
        {
            var preset = JsonSerializer.Deserialize<ArticulationPreset>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });
            if (preset is null || preset.Rules is null || preset.Rules.Count > ArticulationMap.MaxRules + 1) return null;
            preset.Name = Clip(preset.Name, MaxNameChars);
            preset.Description = Clip(preset.Description, MaxDescriptionChars);
            return preset;
        }
        catch (JsonException) { return null; }
    }

    public static byte[] ToBytes(ArticulationPreset preset) => JsonSerializer.SerializeToUtf8Bytes(preset, new JsonSerializerOptions { WriteIndented = true });

    public static ArticulationPreset? ReadFile(string path)
    {
        try { return Parse(InputLimits.ReadBoundedBytes(path, MaxFileBytes, "articulation preset")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Trace.Error(Trace.Ui, "articulations: read: " + ex.Message); return null; }
    }

    public static void WriteFile(string path, ArticulationPreset preset) =>
        FilePathPolicy.WriteAtomically(path, s => s.Write(ToBytes(preset)), createDirectory: true);

    /// <summary>A built-in preset, else the user preset of that name; null when neither exists.</summary>
    public static ArticulationPreset? Find(string name)
    {
        if (FindBuiltIn(name) is { } builtIn) return builtIn;
        try { return ReadFile(UserStore().PathOf(name)); }
        catch (InvalidDataException) { return null; }
    }

    private static string Clip(string? text, int max) { var t = (text ?? "").Trim(); return t.Length > max ? t[..max] : t; }
}
