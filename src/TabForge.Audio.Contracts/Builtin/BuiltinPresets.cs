using System.Text.Json;

namespace TabForge.Audio.Contracts.Builtin;

// Owns: the factory presets of the built-in effects (today the compressor: Bass, Vocal, Guitar) as built-in state bytes, so the FX chain preset list shows them
//   beside the user presets and applies them the same way (a partial state: the keys it names change).
// Does not own: the user presets (PluginLibrary), the effects or their parameter keys (the engine classes; BuiltinEffectSetTests checks every key exists).
// Tests: BuiltinEffectSetTests.
public static class BuiltinPresets
{
    private sealed record Preset(string Name, IReadOnlyList<(string Key, double Value)> Values);

    private static IReadOnlyList<Preset> Compressor { get; } =
    [
        new("Bass", [("bypass", 0), ("threshold", -22), ("ratio", 4), ("knee", 6), ("attack", 25), ("release", 120), ("makeup", 3), ("autoMakeup", 0)]),
        new("Vocal", [("bypass", 0), ("threshold", -20), ("ratio", 3), ("knee", 9), ("attack", 6), ("release", 90), ("makeup", 0), ("autoMakeup", 1)]),
        new("Guitar", [("bypass", 0), ("threshold", -24), ("ratio", 3), ("knee", 6), ("attack", 15), ("release", 140), ("makeup", 2), ("autoMakeup", 0)]),
    ];

    private static IReadOnlyList<Preset> Of(string? id) =>
        string.Equals(id, BuiltinEffects.CompressorId, StringComparison.OrdinalIgnoreCase) ? Compressor : [];

    /// <summary>The names of the factory presets of the built-in effect <paramref name="id"/> (none for most effects and for any other path).</summary>
    public static IReadOnlyList<string> Names(string? id) => Of(id).Select(p => p.Name).ToList();

    /// <summary>The built-in state bytes of a factory preset, or null when the effect has no preset of that name.</summary>
    public static byte[]? State(string? id, string name)
    {
        if (Of(id).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } preset) return null;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteStartObject("params");
            foreach (var (key, value) in preset.Values) w.WriteNumber(key, value);
            w.WriteEndObject();
            w.WriteStartObject("text");
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>The parameter keys a factory preset sets (for the check that they all exist).</summary>
    public static IReadOnlyList<string> Keys(string? id, string name) =>
        Of(id).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Values.Select(v => v.Key).ToList() ?? [];
}
