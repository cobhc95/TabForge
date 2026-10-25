// Owns: the shared colour and opacity entry points: colour by name in the dark or the light theme, the named opacity
//   table, and the contrast helpers. Each instance is a snapshot built on settings publish, never per frame.
// Does not own: the colour data (Palette), the brush table and maths (ThemeColours), or publishing into a toolkit.
// Tests: UiTokensTests.
using System;
using System.Collections.Generic;
using TabForge.Services;

namespace TabForge.Platform.Theme;

/// <summary>
/// The shared colour and opacity entry points. Colour resolves the appearance brush table first, then the palette, the same
/// precedence the Avalonia publisher paints with. An instance is a snapshot: build one per settings publish and hold it;
/// building one per frame would rebuild the brush table every frame. Wraps Palette and ThemeColours; no value is copied.
/// </summary>
public sealed class UiTokens
{
    private static readonly IReadOnlyDictionary<string, double> Opacities = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["glow.halo"] = 0.35,
        ["glow.active"] = 0.70,
        ["glow.ring"] = 0.12,
        ["glow.hover"] = 0.28,
        ["glow.press"] = 0.85,
        ["clip.outline"] = 0.35,
        ["barMarker.stroke"] = 0.8,
        ["playhead.shadow"] = 0.72,
        ["transport.shadow"] = 0.52,
        ["fretboard.halo"] = 0.20,
        ["fretboard.releasedRing"] = 0.7,
        ["fretboard.nextFill"] = 0.30,
        ["fretboard.tagFill"] = 0.92,
        ["fretboard.movement"] = 0.75,
    };

    private readonly IReadOnlyDictionary<string, UiColor> _brushes;

    /// <summary>Builds the snapshot for the appearance in the dark or the light theme. The parse reads toolkit colour text.</summary>
    public UiTokens(AppearanceSettings appearance, bool light, Func<string?, UiColor?> parse)
    {
        if (appearance is null) throw new ArgumentNullException(nameof(appearance));
        if (parse is null) throw new ArgumentNullException(nameof(parse));
        var brushes = new Dictionary<string, UiColor>(StringComparer.Ordinal);
        foreach (var entry in ThemeColours.Brushes(appearance, light, parse))
            brushes[entry.Key] = entry.Value;
        _brushes = brushes;
        Light = light;
    }

    /// <summary>True when this snapshot resolves the light theme.</summary>
    public bool Light { get; }

    /// <summary>Builds a snapshot for the appearance in the dark or the light theme.</summary>
    public static UiTokens For(AppearanceSettings appearance, bool light, Func<string?, UiColor?> parse) =>
        new UiTokens(appearance, light, parse);

    /// <summary>The colour for the token: the appearance brush table first, then the palette. An unknown name throws.</summary>
    public UiColor Ink(string token) =>
        _brushes.TryGetValue(token, out var colour) ? colour : Palette.Get(token, Light);

    /// <summary>The palette colour for the name in the light or the dark theme. An unknown name throws.</summary>
    public static UiColor Colour(string name, bool light) => Palette.Get(name, light);

    /// <summary>The named opacity. An unknown name throws.</summary>
    public double Opacity(string token) =>
        Opacities.TryGetValue(token, out var value) ? value : throw new KeyNotFoundException($"No opacity token named '{token}'.");

    /// <summary>The colour with its alpha scaled by the named opacity token.</summary>
    public UiColor With(UiColor colour, string opacityToken)
    {
        var amount = Opacity(opacityToken);
        return new UiColor((byte)Math.Round(colour.A * amount), colour.R, colour.G, colour.B);
    }

    /// <summary>Blends one colour towards another, the ThemeColours blend.</summary>
    public static UiColor Blend(UiColor colour, UiColor other, double amount) => ThemeColours.Blend(colour, other, amount);

    /// <summary>Shades one colour towards white or black, the ThemeColours shade.</summary>
    public static UiColor Shade(UiColor colour, double factor) => ThemeColours.Shade(colour, factor);

    /// <summary>The readable label ink on the surface, the ThemeColours ink.</summary>
    public static UiColor InkOn(UiColor surface) => ThemeColours.InkOn(surface);

    /// <summary>The WCAG contrast ratio of two colours, the ThemeColours ratio.</summary>
    public static double Contrast(UiColor first, UiColor second) => ThemeColours.Contrast(first, second);

    /// <summary>The opaque white ink text is measured with: measuring never paints, so the ink is fixed and shared.</summary>
    public static UiColor MeasureInk { get; } = Palette.Get("White");
}
