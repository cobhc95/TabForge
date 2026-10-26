using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The Avalonia hover and press surfaces: a stronger shade than the Core theme's derived HoverBrush, which only guards 1.3:1 against the Panel2 cards,
/// so a hovered button on the window chrome changed to a fill indistinguishable from the surface under it. Avalonia steps the shade away from the panel
/// until it reaches <see cref="MinimumHoverContrast"/> against the window, the panel and the cards while the primary text stays above
/// <see cref="MinimumHoverTextContrast"/>; the pressed shade is one further step. The Core theme maths and the app's output are untouched.
/// Owns: the minimum ratios and the derived hover and press colours. Does not own: the published brushes (AvaloniaPalette) or which control paints the shade.
/// Tests: TabForge.Avalonia.Tests.HoverSurfaceContrastTests.
/// </summary>
public static class HoverSurfaces
{
    /// <summary>
    /// The minimum contrast of a hovered surface against the window background, the panel and the Panel2 cards it sits on: 1.6:1. The Core 1.3:1 step
    /// is below the point a flat fill change reads as a state; WCAG's 3:1 for non-text is an essential-boundary ratio and would turn a button into a
    /// slab rather than a hover. 1.6 is the smallest step that clears the flat chrome while the primary text keeps WCAG AA.
    /// </summary>
    public const double MinimumHoverContrast = 1.6;

    /// <summary>The stronger hover of the toolbar and transport icon buttons (IconHoverSurface): 1.9:1 against the same surfaces, one clear step past the rest.</summary>
    public const double MinimumIconHoverContrast = 1.9;

    /// <summary>The minimum contrast of the pressed surface against the hover surface, so a press reads as one further step: 1.2:1.</summary>
    public const double MinimumPressedContrast = 1.2;

    /// <summary>The WCAG AA body-text contrast the primary text keeps on the hover surface: 4.5:1.</summary>
    public const double MinimumHoverTextContrast = 4.5;

    // The Core theme's own shade factors, where the search starts (light darkens the panel, dark lightens it), and the step it moves by.
    private const double LightStartFactor = 0.82, DarkStartFactor = 1.95, ShadeStep = 0.01;
    // Bounds that stop a search for a shade which the theme's greys cannot reach.
    private const double LightLimitFactor = 0.4, DarkLimitFactor = 3.0;

    // How far past the hover limit the pressed shade may step (in ShadeSteps) before giving up.
    private const int PressExtraSteps = 300;

    /// <summary>The palette keys of the surfaces this class derives: they are not Core palette entries, so this class is their one source.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "HoverSurface", "PressSurface", "IconHoverSurface" };

    /// <summary>True when <paramref name="paletteName"/> is one of the derived <see cref="Names"/>.</summary>
    public static bool IsOverridden(string paletteName) => Names.Contains(paletteName);

    /// <summary>The derived surface colour <paramref name="paletteName"/> paints in the dark or the light theme.</summary>
    public static UiColor Value(string paletteName, bool light) => paletteName switch
    {
        "HoverSurface" => Colours(light).Hover,
        "PressSurface" => Colours(light).Press,
        "IconHoverSurface" => (light ? IconLight : IconDark).Value.Hover,
        _ => throw new ArgumentException($"{paletteName} is not a derived hover surface.", nameof(paletteName)),
    };

    // The four tables derive both themes up front (the flag picks which, nothing is read for the current theme), so the literals are the point.
    private static Lazy<(UiColor Hover, UiColor Press)> Dark = new(() => Derive(light: false, MinimumHoverContrast));
    private static Lazy<(UiColor Hover, UiColor Press)> Light = new(() => Derive(light: true, MinimumHoverContrast));
    private static Lazy<(UiColor Hover, UiColor Press)> IconDark = new(() => Derive(light: false, MinimumIconHoverContrast));
    private static Lazy<(UiColor Hover, UiColor Press)> IconLight = new(() => Derive(light: true, MinimumIconHoverContrast));

    /// <summary>Drops the derived shades so the next read derives them from the palette just published (a custom panel colour changes them).</summary>
    internal static void Invalidate()
    {
        Dark = new(() => Derive(light: false, MinimumHoverContrast));
        Light = new(() => Derive(light: true, MinimumHoverContrast));
        IconDark = new(() => Derive(light: false, MinimumIconHoverContrast));
        IconLight = new(() => Derive(light: true, MinimumIconHoverContrast));
    }

    private static (UiColor Hover, UiColor Press) Colours(bool light) => light ? Light.Value : Dark.Value;

    // Steps the Core shade away from the panel until it clears the window, the panel and the cards and keeps the primary text legible, then one
    // more step for the pressed shade. The first shade that clears is the smallest visible step, so a hover stays a shade rather than a highlight.
    private static (UiColor Hover, UiColor Press) Derive(bool light, double minimumContrast)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        var surfaces = new[]
        {
            AvaloniaPalette.Get("WindowBackground", light),
            panel,
            AvaloniaPalette.ThemeBrush("Panel2Brush", light),
        };
        var ink = AvaloniaPalette.Get("TextPrimary", light);
        var step = light ? -ShadeStep : ShadeStep;
        var factor = light ? LightStartFactor : DarkStartFactor;
        var limit = light ? LightLimitFactor : DarkLimitFactor;

        // A user's custom greys may never clear the minimum: the shade then stops at the limit (the strongest step) instead of failing the
        // whole palette. The shipped presets are held to the minimum by PaletteThemeTokenTests, not here.
        while (!Clears(factor) && !(light ? factor <= limit : factor >= limit))
            factor += step;
        var hover = ThemeColours.Shade(panel, factor);
        var hoverFactor = factor;
        // The pressed shade may step past the hover limit (it only has to differ from the hover); the cap just bounds the loop.
        var pressLimit = light ? limit - PressExtraSteps * ShadeStep : limit + PressExtraSteps * ShadeStep;
        do
            factor += step;
        while (ThemeColours.Contrast(ThemeColours.Shade(panel, factor), hover) < MinimumPressedContrast
               && !(light ? factor <= pressLimit : factor >= pressLimit));
        // The pressed shade keeps the primary text legible too: it never steps past the point where the text falls under the minimum.
        while (ThemeColours.Contrast(ThemeColours.Shade(panel, factor), ink) < MinimumHoverTextContrast && (light ? factor < hoverFactor : factor > hoverFactor))
            factor -= step;
        return (hover, ThemeColours.Shade(panel, factor));

        bool Clears(double shadeFactor)
        {
            var candidate = ThemeColours.Shade(panel, shadeFactor);
            if (ThemeColours.Contrast(candidate, ink) < MinimumHoverTextContrast) return false;
            foreach (var surface in surfaces)
                if (ThemeColours.Contrast(candidate, surface) < minimumContrast) return false;
            return true;
        }
    }
}
