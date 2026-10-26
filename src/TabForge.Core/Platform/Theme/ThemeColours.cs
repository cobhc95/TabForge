using TabForge.Services;

namespace TabForge.Platform.Theme;

// Owns: the brush-name to colour table and the Dark / Light presets (mirrors the summary below).
// Does not own: writing brushes into a toolkit or reading the system theme.
// Tests: ThemeTests.

/// <summary>
/// The theme brushes worked out from the appearance settings: which brush name gets which colour in the dark and the light theme,
/// with the accent and panel mixing. Owns: the brush-name to colour table and the Dark / Light presets.
/// Does not own: writing brushes into a toolkit (AvaloniaPalette) or reading the system theme.
/// Tests: TabForge.Avalonia.Tests.ThemeTests.
/// </summary>
public static class ThemeColours
{
    /// <summary>The colour a Settings colour row shows when its default is unknown.</summary>
    public const string FallbackColourHex = "#FFFFFF";

    /// <summary>Label ink for accent fills: the light or the dark one, whichever reads better.</summary>
    public static readonly UiColor InkLight = new(0xFF, 0xF4, 0xF6, 0xF8);
    public static readonly UiColor InkDark = new(0xFF, 0x11, 0x14, 0x18);

    /// <summary>Brush name to colour, in paint order. <paramref name="parse"/> is the toolkit's colour-text parser (it accepts
    /// named colours); a brush whose source colour does not parse is left out. Shaded and blended colours are opaque.</summary>
    public static IReadOnlyList<KeyValuePair<string, UiColor>> Brushes(AppearanceSettings a, bool light, Func<string?, UiColor?> parse)
    {
        var list = new List<KeyValuePair<string, UiColor>>();
        void Set(string key, UiColor? colour) { if (colour is { } c) list.Add(new(key, c)); }
        UiColor? Shade(string? text, double factor) => parse(text) is { } c ? ThemeColours.Shade(c, factor) : null;
        UiColor? Blend(string? text, string? other, double amount) =>
            parse(text) is not { } c ? null : parse(other) is { } o ? ThemeColours.Blend(c, o, amount) : c;
        var panel = a.Panel;
        var text = a.Text;
        var muted = a.Muted;
        Set("WindowBrush", parse(a.Background));
        Set("PanelBrush", parse(panel));
        // Cards: the dark card is about 1.25x the panel, the CardBackground palette shade the app paints, so cards stand off the page.
        Set("Panel2Brush", Shade(panel, light ? 1.0 : 1.25));
        Set("Panel3Brush", Shade(panel, light ? 0.92 : 1.22));
        Set("ChromeStripBrush", parse(a.TitleBarColour));
        Set("TabActiveBrush", parse(a.ActiveTabColour));
        Set("TabHoverBrush", parse(a.TabHoverColour));
        // Unselected tabs read as three levels above the title bar: rest, hover, then the selected tab (TabActiveBrush).
        Set("TabRestSurfaceBrush", Shade(a.TitleBarColour, light ? 0.97 : 1.5));
        Set("TabHoverSurfaceBrush", Shade(a.TitleBarColour, light ? 0.92 : 1.9));
        Set("BorderBrush", Shade(panel, light ? 0.78 : 1.55));
        // Checkbox outline: a stronger grey in light themes so the empty box shows on grey panels; dark keeps BorderBrush.
        Set("CheckBoxBorderBrush", Shade(panel, light ? 0.55 : 1.55));
        Set("BorderSoftBrush", Shade(panel, light ? 0.88 : 1.28));
        Set("TextBrush", parse(text));
        Set("TextStrongBrush", Palette.Get("TextStrong", light));
        Set("MutedBrush", parse(muted));
        Set("LegibleBrush", Blend(muted, text, 0.2));   // lifted muted text for tiny labels
        Set("SecondaryTextBrush", Blend(muted, text, 0.45));
        // The applied accent: the picked colour, lifted or lowered until it reads on the background (the stored setting keeps the pick).
        var accent = parse(a.Accent) is { } picked && parse(a.Background) is { } background ? AccentShades(picked, background, light).Base : parse(a.Accent);
        Set("AccentBrush", accent);
        Set("AccentSoftBrush", accent is { } soft ? ThemeColours.Blend(soft, parse(a.Background) ?? soft, 0.55) : null);
        // Label ink on accent fills (the AccentButton style): whichever ink reads better on this accent.
        if (accent is { } fill) Set("OnAccentBrush", InkOn(fill));
        Set("SelectionBrush", a.IsOverridden(nameof(a.SelectionColour)) ? parse(a.SelectionColour) : accent);   // the selection follows the accent until the user picks its own
        Set("HoverAccentBrush", parse(a.HoverColour));
        Set("TimelineScrollTrackBrush", Shade(panel, light ? 0.88 : 0.82));
        Set("TimelineScrollThumbBrush", parse(a.TimelineScrollBarThumbColour));
        Set("TimelineScrollThumbHoverBrush", Blend(a.TimelineScrollBarThumbColour, "#FFFFFF", 0.20));
        Set("TimelineScrollThumbDragBrush", Blend(a.TimelineScrollBarThumbColour, "#FFFFFF", 0.34));
        Set("CursorBrush", parse(a.CursorColour));
        Set("PlayBrush", parse(a.PlayheadColour));
        // Level meters (input and track): the usual green -> amber -> red, tuned for the background.
        Set("MeterBrush", Palette.Get("Meter", light));
        Set("MeterHotBrush", Palette.Get("MeterHot", light));
        Set("WorkspaceBrush", Palette.Get("Workspace", light));
        Set("InstrumentHostBrush", Palette.Get("InstrumentHost", light));
        return list;
    }

    /// <summary>Writes the Dark or Light palette (interface, accent, score page and paper) into the settings; <paramref name="light"/> picks which.
    /// Every colour is the theme's master token (Palette), so a preset is one table per theme and nothing is typed twice. A colour the user set
    /// explicitly (<see cref="AppearanceSettings.IsOverridden"/>) is left as it is: the preset is the base, the user's choices layer on top.</summary>
    public static void ApplyPreset(AppearanceSettings a, bool light)
    {
        string Pick(string field, string token, string current) => a.IsOverridden(field) ? current : Palette.Hex(token, light);
        a.Background = Pick(nameof(a.Background), "WindowBackground", a.Background); a.Panel = Pick(nameof(a.Panel), "PanelBackground", a.Panel);
        a.TitleBarColour = Pick(nameof(a.TitleBarColour), "ChromeStrip", a.TitleBarColour);
        a.ActiveTabColour = Pick(nameof(a.ActiveTabColour), "TabActive", a.ActiveTabColour); a.TabHoverColour = Pick(nameof(a.TabHoverColour), "TabHover", a.TabHoverColour);
        a.Text = Pick(nameof(a.Text), "TextPrimary", a.Text); a.Muted = Pick(nameof(a.Muted), "TextMuted", a.Muted);
        a.Accent = Pick(nameof(a.Accent), "Accent", a.Accent); a.SelectionColour = Pick(nameof(a.SelectionColour), "Selection", a.SelectionColour);
        a.HoverColour = Pick(nameof(a.HoverColour), "HoverOutline", a.HoverColour);
        a.PlayheadColour = Pick(nameof(a.PlayheadColour), "Playhead", a.PlayheadColour); a.CursorColour = Pick(nameof(a.CursorColour), "ScoreCursor", a.CursorColour);
        // Symmetric: each preset writes its own paper, ink and staff-line colours (the other paper's colours stay as the user set them).
        if (light)
        {
            if (!a.IsOverridden(nameof(a.ScorePaper))) a.ScorePaper = "Light";
            a.LightScorePaperColour = Pick(nameof(a.LightScorePaperColour), "ScoreLightPaperDefault", a.LightScorePaperColour);
            a.LightScoreInkColour = Pick(nameof(a.LightScoreInkColour), "ScoreLightInkDefault", a.LightScoreInkColour);
            a.LightScoreLinesColour = Pick(nameof(a.LightScoreLinesColour), "ScoreLightStaffLineDefault", a.LightScoreLinesColour);
        }
        else
        {
            if (!a.IsOverridden(nameof(a.ScorePaper))) a.ScorePaper = "Dark";
            a.DarkScorePaperColour = Pick(nameof(a.DarkScorePaperColour), "ScoreDarkPaperDefault", a.DarkScorePaperColour);
            a.DarkScoreInkColour = Pick(nameof(a.DarkScoreInkColour), "ScoreDarkInkDefault", a.DarkScoreInkColour);
            a.DarkScoreLinesColour = Pick(nameof(a.DarkScoreLinesColour), "ScoreDarkStaffLineDefault", a.DarkScoreLinesColour);
        }
    }

    /// <summary>Drops every colour the user set explicitly and writes the preset again: the "reset to preset" action.</summary>
    public static void ResetToPreset(AppearanceSettings a, bool light)
    {
        a.OverriddenColours = "";
        ApplyPreset(a, light);
    }

    /// <summary>True when the palette token <paramref name="name"/> is the accent in another role (focus ring, active icon, slider fill, primary button...): the theme paints it
    /// from the Accent setting (see <see cref="FromAccent"/>), not from the palette's fixed blue.</summary>
    public static bool DerivesFromAccent(string name) => name is
        "SelectedTint" or "SelectedOutline" or "AccentHover" or "AccentPress" or "FocusRing" or "IconActive" or "SliderTrackFill" or "SliderThumbGlow" or "TransportButtonGlow" or "AddTrackBorder" or "AddTrackSurface"
        or "PrefsAccent" or "PrefsPrimary" or "PrefsPrimaryHover" or "PrefsPrimaryPress" or "PrefsPrimaryInk" or "PrefsSelection" or "SettingsNavIconSelected"
        or "DockGhostAccentFallback" or "DockGhostGlow" or "DockDragGhostFallback" or "DockDragGlow" or "FxSplitAccentFallback" or "ToolPaletteAccentFallback" or "ControllerDockGlow";

    /// <summary>The accent-derived token <paramref name="name"/> for the applied accent (<paramref name="accent"/>, already lifted to read on <paramref name="surface"/>) and its soft fill.
    /// The hover and press steps are fixed lightness steps of the accent, the ink on a primary fill is the light or dark ink that reads better,
    /// and the icon and text shades (active icon, selected nav icon) are the accent moved by the measured icon offset.</summary>
    public static UiColor FromAccent(string name, UiColor accent, UiColor accentSoft, UiColor surface, bool light) => name switch
    {
        // Light: the accent mixed 85% toward the surface (a pale tint, dark glyph on it); dark keeps the soft fill. The outline is the accent in both.
        "SelectedTint" => light ? Blend(accent, surface, SelectedTintLightAmount) : accentSoft,
        "SelectedOutline" => accent,
        "AccentHover" or "FocusRing" or "PrefsPrimaryHover" => Step(accent, 0.15, 0.2),
        "AccentPress" or "PrefsPrimaryPress" => Step(accent, 0.3, 0.1),
        "PrefsPrimaryInk" => InkOn(accent),
        "PrefsSelection" => accentSoft,
        "AddTrackSurface" => Shade(accent, 0.6),
        "DockDragGhostFallback" => WithOpacity(accent, 0.22),
        "IconActive" or "PrefsAccent" or "SettingsNavIconSelected" => AccentShades(accent, surface, light).Icon,
        _ => accent,
    };

    // A hover or press step moves the accent away from its label ink (darker under light ink, lighter under dark ink), so the ink keeps its contrast in every state.
    private static UiColor Step(UiColor accent, double darken, double lighten) =>
        InkOn(accent) == InkLight ? Shade(accent, 1 - darken) : Brighten(accent, lighten);

    /// <summary>How far the light selected tint is mixed from the accent toward the surface.</summary>
    public const double SelectedTintLightAmount = 0.85;

    /// <summary>Lowest contrast an accent bar or icon keeps against its surface.</summary>
    public const double MinimumAccentContrast = 3.0;

    // Today's two blues, measured in HSL: the icon blue sits 7 points of lightness below the base in the dark theme (#4C9AFF to #38A9F5) and 8 above it in the light theme (#0F4489 to #105EB0).
    private const double IconLightnessOffsetDark = -0.07, IconLightnessOffsetLight = 0.08, LightnessStep = 0.01;

    /// <summary>The base shade (bar, primary fill, selected fill) and the icon shade (accent icons and text) of a picked accent on <paramref name="surface"/>:
    /// a pick that is too dark (dark theme) or too light (light theme) is lifted or lowered in lightness until each shade has <see cref="MinimumAccentContrast"/> against the surface.</summary>
    public static (UiColor Base, UiColor Icon) AccentShades(UiColor picked, UiColor surface, bool light)
    {
        var (hue, saturation, lightness) = ToHsl(picked);
        var baseColour = Reads(picked, surface, label: true) ? picked : ReadOn(hue, saturation, lightness, surface, label: true);
        var (_, baseSaturation, baseLightness) = ToHsl(baseColour);
        var icon = ReadOn(hue, baseSaturation, Math.Clamp(baseLightness + (light ? IconLightnessOffsetLight : IconLightnessOffsetDark), 0, 1), surface, label: false);
        return (baseColour, icon);
    }

    /// <summary>Relative luminance a light-theme accent is lifted to when the master accent is darker (about #2563C4).</summary>
    public const double LightAccentMinimumLuminance = 0.12;

    /// <summary>The light-theme accent derived from the master accent: darker than <see cref="LightAccentMinimumLuminance"/> is lightened (hue and saturation kept) until it reaches it, lighter is unchanged.
    /// Lightening stops where the label ink would drop under <see cref="MinimumLabelContrast"/>.</summary>
    public static UiColor LightenForLightTheme(UiColor master)
    {
        if (Luminance(master) >= LightAccentMinimumLuminance) return master;
        var (hue, saturation, lightness) = ToHsl(master);
        var colour = master;
        for (var level = lightness + LightnessStep; level <= 1; level += LightnessStep)
        {
            var next = FromHsl(hue, saturation, level);
            if (Contrast(InkOn(next), next) < MinimumLabelContrast) break;
            colour = next;
            if (Luminance(colour) >= LightAccentMinimumLuminance) break;
        }
        return colour;
    }

    private static string Hex(UiColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>The theme selector moved to <paramref name="mode"/>: Dark and Light write their preset; switching to Light once lightens a too-dark accent (the original is kept),
    /// switching back to Dark restores it if the accent is still the lightened one. A pick made afterwards is honoured as it is.</summary>
    public static void SwitchTheme(AppearanceSettings a, string mode)
    {
        var wasLight = string.Equals(a.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);
        var before = a.Accent;
        a.ThemeMode = mode;
        if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreset(a, light: true);
            if (wasLight || !TabForge.Models.ColourHex.TryParse(a.Accent, out var rgba)) return;
            var accent = new UiColor(0xFF, rgba.R, rgba.G, rgba.B);
            var lighter = LightenForLightTheme(accent);
            if (lighter == accent) { a.AccentBeforeLight = ""; return; }
            a.AccentBeforeLight = a.Accent;
            a.Accent = Hex(lighter);
        }
        else if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreset(a, light: false);
            if (wasLight && a.AccentBeforeLight.Length > 0 && TabForge.Models.ColourHex.TryParse(a.AccentBeforeLight, out var orig)
                && string.Equals(before, Hex(LightenForLightTheme(new UiColor(0xFF, orig.R, orig.G, orig.B))), StringComparison.OrdinalIgnoreCase)) a.Accent = a.AccentBeforeLight;
            a.AccentBeforeLight = "";
        }
    }

    /// <summary>Lowest contrast of the ink on an accent fill.</summary>
    public const double MinimumLabelContrast = 4.5;

    // A shade reads when it has the minimum contrast against the surface and, for a fill that carries a label, its ink has the label contrast on it.
    private static bool Reads(UiColor colour, UiColor surface, bool label) =>
        Contrast(colour, surface) >= MinimumAccentContrast && (!label || Contrast(InkOn(colour), colour) >= MinimumLabelContrast);

    // Moves the lightness away from the surface (down on a light surface, up on a dark one, judged by its luminance, not by the theme variant) until the colour reads (see Reads);
    // when that direction hits the end without reading, the other direction is tried, and the first colour that reads wins.
    private static UiColor ReadOn(double hue, double saturation, double lightness, UiColor surface, bool label)
    {
        var sign = Luminance(surface) > 0.18 ? -1 : 1;
        var last = FromHsl(hue, saturation, lightness);
        foreach (var direction in new[] { sign, -sign })
        {
            var level = lightness;
            var colour = FromHsl(hue, saturation, level);
            for (var step = 0; step <= 1 / LightnessStep && !Reads(colour, surface, label); step++)
            {
                level = Math.Clamp(level + direction * LightnessStep, 0, 1);
                colour = FromHsl(hue, saturation, level);
            }
            if (Reads(colour, surface, label)) return colour;
            if (direction == sign) last = colour;
        }
        return last;
    }

    /// <summary>Hue in degrees, saturation and lightness (0-1) of an opaque colour.</summary>
    public static (double Hue, double Saturation, double Lightness) ToHsl(UiColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d == 0) return (0, 0, l);
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static UiColor FromHsl(double hue, double saturation, double lightness)
    {
        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        byte Channel(double t)
        {
            t = (t % 1 + 1) % 1;
            var v = t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            return (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        }
        return new UiColor(0xFF, Channel(hue / 360 + 1.0 / 3), Channel(hue / 360), Channel(hue / 360 - 1.0 / 3));
    }

    /// <summary>Multiplies the colour towards white (factor &gt; 1) or black (factor &lt; 1); the result is opaque.</summary>
    public static UiColor Shade(UiColor c, double factor)
    {
        byte Channel(byte v) => (byte)Math.Clamp(Math.Round(v * factor), 0, 255);
        return new UiColor(0xFF, Channel(c.R), Channel(c.G), Channel(c.B));
    }

    /// <summary>Blends <paramref name="c"/> towards <paramref name="other"/> by <paramref name="amount"/> (0-1); the result is opaque.</summary>
    public static UiColor Blend(UiColor c, UiColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * amount), 0, 255);
        return new UiColor(0xFF, Mix(c.R, other.R), Mix(c.G, other.G), Mix(c.B, other.B));
    }

    /// <summary>The colour mixed toward white by <paramref name="amount"/> (0 keeps it, 1 is white); alpha is kept.</summary>
    public static UiColor Brighten(UiColor c, double amount)
    {
        byte Lift(byte v) => (byte)Math.Clamp(Math.Round(v + (255 - v) * Math.Clamp(amount, 0, 1)), 0, 255);
        return c with { R = Lift(c.R), G = Lift(c.G), B = Lift(c.B) };
    }

    /// <summary>The colour with its alpha scaled by <paramref name="opacity"/> (0 to 1).</summary>
    public static UiColor WithOpacity(UiColor c, double opacity) =>
        c with { A = (byte)Math.Clamp(Math.Round(c.A * Math.Clamp(opacity, 0, 1)), 0, 255) };

    public static UiColor InkOn(UiColor surface) => Contrast(InkLight, surface) >= Contrast(InkDark, surface) ? InkLight : InkDark;

    /// <summary>WCAG contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(UiColor a, UiColor b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double Luminance(UiColor c)
    {
        static double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

}
