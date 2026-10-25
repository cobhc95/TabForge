using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Colour values shared by the Avalonia windows and drawn views.
/// Owns: the one opacity composite and the one strict hex read. Does not own: palette lookup (AvaloniaPalette) or the Core colour rules.
/// Tests: TabForge.Avalonia.Tests.ColourValuesTests.
/// </summary>
internal static class ColourValues
{
    /// <summary>The colour at <paramref name="opacity"/> of its own alpha (the Core colour maths, as an extension for the drawing code).</summary>
    public static UiColor WithOpacity(this UiColor colour, double opacity) => ThemeColours.WithOpacity(colour, opacity);

    /// <summary>The strict "#RRGGBB" / "#AARRGGBB" read (the leading '#' optional), or <paramref name="fallback"/> for anything else, names included.</summary>
    public static Rgba? FromHex(string? text, Rgba? fallback = null) => ColourHex.TryParseStrict(text, out var colour) ? colour : fallback;
}
