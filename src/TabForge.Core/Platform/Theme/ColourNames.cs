using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Platform.Theme;

// Owns: the one name a colour value shows in a colour button: "Default" (or "Default <preset>" when the default is a named preset), the preset's name, or "Custom".
// Does not own: the preset list (NamedColourSwatches) or the button that shows the name (ColourButton).
// Tests: TabForge.Avalonia.Tests.ColourButtonTests.
public static class ColourNames
{
    /// <summary>The name shown for a colour that is neither the default nor a preset.</summary>
    public const string Custom = "Custom";

    /// <summary>The name of <paramref name="hex"/>: the default's name when it equals <paramref name="defaultHex"/>, else its preset name, else <see cref="Custom"/>.</summary>
    public static string NameOf(string? hex, string defaultHex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Custom;
        if (Same(hex, defaultHex)) return PresetName(defaultHex) is { } preset ? $"Default {preset.ToLowerInvariant()}" : "Default";
        return PresetName(hex) ?? Custom;
    }

    // The presets parsed once: a name lookup compares colours instead of parsing every preset each time.
    private static readonly IReadOnlyList<(string Name, Rgba Colour)> ParsedPresets = ParsePresets();

    private static (string Name, Rgba Colour)[] ParsePresets() =>
        NamedColourSwatches.Presets.Select(p => (p.Name, Parsed: TryColour(p.Hex, out var colour), colour)).Where(p => p.Parsed).Select(p => (p.Name, p.colour)).ToArray();

    private static string? PresetName(string hex) =>
        TryColour(hex, out var colour) ? ParsedPresets.Where(p => p.Colour == colour).Select(p => p.Name).FirstOrDefault() : null;

    // With or without the leading '#': the bare form is taken only as 6 or 8 digits, so a short word like "bad" stays text.
    private static bool TryColour(string text, out Rgba colour) => ColourHex.TryParse(text, out colour) || ColourHex.TryParseStrict(text, out colour);

    // Parsed colours compare equal across forms (#FF4C9AFF == #4C9AFF, #RGB == #RRGGBB); text that is not a hex form compares as text.
    private static bool Same(string a, string b) =>
        TryColour(a, out var left) && TryColour(b, out var right) ? left == right : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
