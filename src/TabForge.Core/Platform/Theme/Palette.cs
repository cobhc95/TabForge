using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabForge.Platform.Theme;

/// <summary>One named colour: one value for every theme, or a dark and a light value. Values are "#RRGGBB" or "#AARRGGBB".</summary>
public sealed record PaletteEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("dark")] string? Dark = null,
    [property: JsonPropertyName("light")] string? Light = null);

/// <summary>
/// The theme table's named colours, read from the JSON files in Platform/Theme/Palette (embedded in the app). Views name a colour instead of
/// writing its value; a colour that differs by theme has a dark and a light value. The table is toolkit-neutral (<see cref="UiColor"/>); the Avalonia
/// adapter (AvaloniaPalette) hands out one brush per colour.
/// Owns: reading and finding named colours. Does not own: the user-editable appearance colours (AvaloniaPalette paints those into resources).
/// Tests: PaletteToolsTests.
/// </summary>
public static class Palette
{
    private static readonly Lazy<IReadOnlyDictionary<string, (UiColor Dark, UiColor Light)>> All = new(Load);

    /// <summary>The colour <paramref name="name"/> for a theme-independent entry (or its dark value); throws for an unknown name.</summary>
    public static UiColor Get(string name) => Find(name).Dark;

    /// <summary>The colour <paramref name="name"/> in the light or dark theme.</summary>
    public static UiColor Get(string name, bool light) => light ? Find(name).Light : Find(name).Dark;

    /// <summary>The colour <paramref name="name"/> as "#RRGGBB" (or "#AARRGGBB" when it is not opaque), the text form the appearance settings store.</summary>
    public static string Hex(string name, bool light)
    {
        var c = Get(name, light);
        return TabForge.Models.ColourHex.FormatAuto(new TabForge.Models.Rgba(c.R, c.G, c.B, c.A));
    }

    /// <summary>Every name in the table.</summary>
    public static IEnumerable<string> Names => All.Value.Keys;

    private static (UiColor Dark, UiColor Light) Find(string name) =>
        All.Value.TryGetValue(name, out var c) ? c : throw new ArgumentException($"No colour named '{name}' in the palette.");

    /// <summary>"#RRGGBB" or "#AARRGGBB" as a colour.</summary>
    public static UiColor Parse(string hex)
    {
        var h = hex.TrimStart('#');
        var v = uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (h.Length == 6) v |= 0xFF000000;
        else if (h.Length != 8) throw new FormatException($"Colour '{hex}' is not #RRGGBB or #AARRGGBB.");
        return new UiColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private static IReadOnlyDictionary<string, (UiColor, UiColor)> Load()
    {
        var asm = typeof(Palette).Assembly;
        var colours = new Dictionary<string, (UiColor, UiColor)>(StringComparer.Ordinal);
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.Contains(".Theme.Palette.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            foreach (var e in JsonSerializer.Deserialize<List<PaletteEntry>>(stream) ?? new List<PaletteEntry>())
            {
                var dark = Parse(e.Value ?? e.Dark ?? throw new FormatException($"Colour '{e.Name}' has no value."));
                var light = e.Value is not null ? dark : Parse(e.Light ?? throw new FormatException($"Colour '{e.Name}' has a dark value but no light one."));
                colours.Add(e.Name, (dark, light));
            }
        }
        return colours;
    }
}
