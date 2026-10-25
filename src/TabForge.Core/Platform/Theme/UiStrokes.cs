// Owns: the shared stroke entry point: the named base widths and the density, UI-scale and score-spacing multiplier.
// Does not own: the outline shape (UiStroke), the density and scale (UiDensity) or the score spacing (AppearanceSettings).
// Tests: UiStrokesTests.
using System;
using System.Collections.Generic;
using TabForge.Services;

namespace TabForge.Platform.Theme;

/// <summary>A named stroke base: the width in DIPs at scale 1.0, with an optional dash in widths.</summary>
public readonly record struct UiStrokeSpec(double Width, double Dash = 0, double Gap = 0);

/// <summary>
/// The shared stroke entry point. The width is the base times density times UI scale, and times the score spacing for
/// score strokes. The table is seeded with the shared widths only; each area batch adds its names when it deletes its
/// literals, so no width is invented here. Wraps UiStroke, UiDensity and AppearanceSettings; nothing in the app calls it yet.
/// </summary>
public static class UiStrokes
{
    private static readonly Dictionary<string, UiStrokeSpec> Table = new(StringComparer.Ordinal)
    {
        ["hairline"] = new UiStrokeSpec(1.0),
        ["border"] = new UiStrokeSpec(1.0),
    };

    /// <summary>The named base widths. A batch adds its names here when it deletes their literals.</summary>
    internal static IReadOnlyDictionary<string, UiStrokeSpec> Tokens => Table;

    /// <summary>The width multiplier: density times clamped UI scale, times the score spacing for score strokes.</summary>
    public static double Scale(AppearanceSettings appearance, bool score = false)
    {
        if (appearance is null) throw new ArgumentNullException(nameof(appearance));
        var factors = UiDensity.Of(appearance);
        var scale = UiDensity.ClampScale(appearance.UiScale) * factors.Stroke;
        return score ? scale * appearance.ScoreSpacing : scale;
    }

    /// <summary>The stroke width for the name at the appearance scale. An unknown name throws.</summary>
    public static double Width(string token, AppearanceSettings appearance, bool score = false) =>
        TokenOf(token).Width * Scale(appearance, score);

    /// <summary>The outline for the name in the colour at the appearance scale. An unknown name throws.</summary>
    public static UiStroke Get(string token, UiColor colour, AppearanceSettings appearance, bool score = false, bool roundCaps = false, bool roundJoins = false)
    {
        var spec = TokenOf(token);
        return new UiStroke(colour, spec.Width * Scale(appearance, score), roundCaps, spec.Dash, spec.Gap, roundJoins);
    }

    private static UiStrokeSpec TokenOf(string token) =>
        Table.TryGetValue(token, out var spec) ? spec : throw new KeyNotFoundException($"No stroke token named '{token}'.");
}
