using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Services;

// Owns: the fretboard look's name lists (woods, bindings, inlay shapes and materials, capo and string looks, vibration levels), the five
//   presets, independent neck/note brightness ranges and the file names of the shipped textures. A look is a set of names; the presets are named sets, "Custom" is any other set.
// Does not own: the drawing (Avalonia FretboardArtist), the geometry (FretboardInlayRules, FretboardGeometryRules), loading and validating
//   settings (SettingsValidator) or the Preferences rows (SettingsCatalog.Fretboard).
// Tests: TabForge.Avalonia.Tests.FretboardStyleTests.
public static class FretboardStyles
{
    public const string Custom = "Custom";
    public const string None = "None";
    // Brightness is independent of material presets: choosing a wood must not undo a user's contrast adjustment.
    public const int MinBrightness = 40, MaxBrightness = 160, BrightnessStep = 5;
    /// <summary>The board brightness defaults: the dark theme's (also the settings' own default), the light theme's, and the one files saved before
    /// the defaults followed the theme (a stored value equal to it was never chosen).</summary>
    public const int DefaultBoardBrightness = 75, LightBoardBrightness = 90, LegacyBoardBrightness = 65, DefaultNoteBrightness = 100;

    /// <summary>The default preset of each theme: Rosewood in the dark theme, Maple in the light one.</summary>
    public const string DarkPreset = "Rosewood", LightPreset = "Maple";

    /// <summary>What a fretboard default follows (EditingSettings.FretboardDefaultsTheme for the look, FretboardBrightnessTheme for the board
    /// brightness): the theme it was last set for, or Own once the person chose something else (it never follows the theme again).</summary>
    public const string DarkTheme = "Dark", LightTheme = "Light", Own = "Own";

    /// <summary>The fretboard look and board brightness a theme starts with.</summary>
    public static (Look Look, int Brightness) ThemeDefaults(bool light) =>
        (OfPreset(light ? LightPreset : DarkPreset)!, light ? LightBoardBrightness : DefaultBoardBrightness);

    /// <summary>
    /// Moves the fretboard's look and board brightness, each on its own, to the defaults of the theme shown now while it still holds the
    /// defaults of the theme it was last set for (a file from before has none: the Rosewood look, at 65% or 75%). One that differs was chosen
    /// by the person: it is marked Own and kept from then on, whatever the theme. True when anything changed.
    /// </summary>
    public static bool FollowTheme(EditingSettings e, bool light)
    {
        var theme = light ? LightTheme : DarkTheme;
        var (toLook, toBrightness) = ThemeDefaults(light);
        var changed = false;
        if (e.FretboardDefaultsTheme != Own && e.FretboardDefaultsTheme != theme)
        {
            var fromLook = e.FretboardDefaultsTheme switch { LightTheme => ThemeDefaults(true).Look, _ => ThemeDefaults(false).Look };
            if (LookOf(e) == fromLook) { Apply(e, toLook); e.FretboardDefaultsTheme = theme; }
            else e.FretboardDefaultsTheme = Own;
            changed = true;
        }
        if (e.FretboardBrightnessTheme != Own && e.FretboardBrightnessTheme != theme)
        {
            var isDefault = e.FretboardBrightnessTheme switch
            {
                LightTheme => e.FretboardBrightness == LightBoardBrightness,
                DarkTheme => e.FretboardBrightness == DefaultBoardBrightness,
                _ => e.FretboardBrightness is LegacyBoardBrightness or DefaultBoardBrightness,
            };
            if (isDefault) { e.FretboardBrightness = toBrightness; e.FretboardBrightnessTheme = theme; }
            else e.FretboardBrightnessTheme = Own;
            changed = true;
        }
        return changed;
    }

    /// <summary>Writes the shown theme's look and brightness defaults and lets both follow the theme again (Reset section on the Look page).</summary>
    public static void ResetToTheme(EditingSettings e, bool light)
    {
        var (look, brightness) = ThemeDefaults(light);
        Apply(e, look);
        e.FretboardBrightness = brightness;
        e.FretboardDefaultsTheme = e.FretboardBrightnessTheme = light ? LightTheme : DarkTheme;
    }
    public static int ClampBrightness(int value) => Math.Clamp(value, MinBrightness, MaxBrightness);

    /// <summary>A neutral black/white veil: below 100 darkens, above 100 brightens; zero at the original look.</summary>
    public static double BrightnessVeil(int percent) => Math.Abs(ClampBrightness(percent) - 100) / 100.0;

    public static readonly IReadOnlyList<string> Woods = new[]
    {
        "Dark rosewood", "Rosewood", "Pau ferro", "Ebony", "Walnut", "Birdseye maple", "Flame maple", "Roasted maple", "Black composite",
    };

    public static readonly IReadOnlyList<string> Bindings = new[]
    {
        None, "Cream", "Ivory", "White", "Parchment", "Gold", "Black", "Black and white", "Pearloid", "Abalone", "Tortoise",
    };

    public static readonly IReadOnlyList<string> InlayShapes = new[] { "Dots", "Small dots", "Blocks", "Trapezoids", "Shark fins", None };

    public static readonly IReadOnlyList<string> InlayMaterials = new[]
    {
        "Pearl white", "Pearl cream", "Ivory", "Gold pearl", "Black pearl", "Pink pearl", "Abalone blue", "Abalone green", "Paua", "Black pearloid",
    };

    /// <summary>The palette token of an inlay material's own colour ("Inlay" plus each word of the name capitalised, e.g. "Pearl cream" is
    /// InlayPearlCream; palette names are case-sensitive): the tint over its grain and its flat colour when the texture is missing. An unknown
    /// name takes the first material's.</summary>
    public static string InlayToken(string? material) =>
        "Inlay" + string.Concat(Pick(InlayMaterials, material, InlayMaterials[0]).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>How strongly a material's own colour tints its grain (the grain stays visible under it).</summary>
    public const double InlayTintOpacity = 0.45;

    public static readonly IReadOnlyList<string> CapoLooks = new[] { "Black", "Bronze", "Steel" };

    public static readonly IReadOnlyList<string> StringLooks = new[] { "Steel", "Nickel", "Gold", "Black", "Bronze" };

    /// <summary>How far a sounding string swings: Off draws it still; Mild (the default) is a hair's breadth; Strong is plain to see.</summary>
    public static readonly IReadOnlyList<string> VibrationLevels = new[] { "Off", "Mild", "Medium", "Strong" };

    public static readonly IReadOnlyList<string> VibrationSpeeds = new[] { "Slow", "Normal", "Fast" };

    /// <summary>One look: every choice that decides how the board is drawn.</summary>
    public sealed record Look(string Wood, string Binding, string InlayShape, string InlayMaterial, string CapoLook, string StringLook);

    /// <summary>The five presets, in menu order; the first is the default look.</summary>
    public static readonly IReadOnlyList<(string Name, Look Look)> Presets = new (string, Look)[]
    {
        ("Rosewood", new Look("Dark rosewood", None, "Dots", "Pearl cream", "Black", "Steel")),
        ("Pearl fins", new Look("Rosewood", "White", "Shark fins", "Pearl white", "Black", "Steel")),
        ("Ebony blocks", new Look("Ebony", "Cream", "Blocks", "Ivory", "Black", "Steel")),
        ("Maple", new Look("Birdseye maple", None, "Dots", "Black pearl", "Steel", "Nickel")),
        ("Flame maple", new Look("Flame maple", "Black", "Dots", "Abalone green", "Bronze", "Nickel")),
    };

    public static IReadOnlyList<string> PresetNames { get; } = Presets.Select(p => p.Name).Append(Custom).ToArray();

    public static Look DefaultLook => Presets[0].Look;

    /// <summary>The preset whose look equals <paramref name="look"/>, else <see cref="Custom"/>.</summary>
    public static string PresetOf(Look look) => Presets.FirstOrDefault(p => p.Look == look).Name ?? Custom;

    /// <summary>The look of a preset name; null for <see cref="Custom"/> or an unknown name.</summary>
    public static Look? OfPreset(string? name) => Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Look;

    /// <summary>The list's own spelling of <paramref name="value"/> (any case), else <paramref name="fallback"/>.</summary>
    public static string Pick(IReadOnlyList<string> list, string? value, string fallback) =>
        list.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    /// <summary>The look the settings hold.</summary>
    public static Look LookOf(EditingSettings e) => new(e.FretboardWood, e.FretboardBinding, e.FretboardInlayShape, e.FretboardInlayMaterial, e.FretboardCapoLook, e.FretboardStringLook);

    /// <summary>Writes <paramref name="look"/> into the settings; null (Custom or an unknown preset) leaves them as they are.</summary>
    public static void Apply(EditingSettings e, Look? look)
    {
        if (look is null) return;
        (e.FretboardWood, e.FretboardBinding, e.FretboardInlayShape, e.FretboardInlayMaterial, e.FretboardCapoLook, e.FretboardStringLook) =
            (look.Wood, look.Binding, look.InlayShape, look.InlayMaterial, look.CapoLook, look.StringLook);
    }

    /// <summary>The look with every name made valid: an unknown name falls back to the default look's.</summary>
    public static Look Normalize(Look look)
    {
        var d = DefaultLook;
        return new Look(Pick(Woods, look.Wood, d.Wood), Pick(Bindings, look.Binding, d.Binding), Pick(InlayShapes, look.InlayShape, d.InlayShape),
            Pick(InlayMaterials, look.InlayMaterial, d.InlayMaterial), Pick(CapoLooks, look.CapoLook, d.CapoLook), Pick(StringLooks, look.StringLook, d.StringLook));
    }

    /// <summary>The peak sideways swing of a sounding string in DIPs for a level: 0 off, 1 mild (the long-standing look), 1.8 medium, 3 strong.</summary>
    public static double VibrationPeak(string? level) => Pick(VibrationLevels, level, "Mild") switch
    {
        "Off" => 0,
        "Medium" => 1.8,
        "Strong" => 3.0,
        _ => 1.0,
    };

    /// <summary>How fast the swing runs relative to the standard curve: 0.7 slow, 1 normal, 1.45 fast.</summary>
    public static double VibrationRate(string? speed) => Pick(VibrationSpeeds, speed, "Normal") switch
    {
        "Slow" => 0.7,
        "Fast" => 1.45,
        _ => 1.0,
    };

    // Texture file names (all under Assets/Fretboards, decoded once): a name's lower-case words joined by hyphens.
    private static string Slug(string name) => name.ToLowerInvariant().Replace(' ', '-');
    public static string WoodFile(string wood) => $"fb_wood_{Slug(wood)}.jpg";
    public static string? BindingFile(string binding) => binding == None ? null : $"fb_binding_{Slug(binding)}.jpg";
    public static string InlayFile(string material) => $"fb_inlay_{Slug(material)}.jpg";
    public static string CapoFile(string look, bool partial, bool shadow) => $"fb_capo_{Slug(look)}_{(partial ? "partial" : "full")}{(shadow ? "_shadow" : "")}.png";
}
