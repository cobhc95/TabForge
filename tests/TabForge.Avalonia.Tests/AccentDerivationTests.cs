using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Accent setting driving every accent-role token (primary button, selected tool, slider fill, focus ring, checkbox fill, selected row),
//   the text-on-accent contrast for every swatch preset, and the guard that no palette token keeps the default accent blue unless it is listed here.
// Does not own: the styles that read the tokens (AvaloniaFormControlStyles, AvaloniaLooks) or the preset write (ThemePresetTests).
// Tests: this file.
public class AccentDerivationTests
{
    private const string Crimson = "#DC2626";
    private const double MinimumTextContrast = 4.5;

    // Tokens that hold the default accent blue on purpose: the accent itself, the selection and default settings that carry their own colour, and the "upcoming" note colour, the tags that mark a custom hotkey or a moved area.
    private static readonly HashSet<string> OwnBlue = new(StringComparer.Ordinal)
    {
        "Accent", "InstrumentAccent", "Selection", "DefaultAccent", "DefaultSelection", "HotkeyCustomTag", "AreaMoveFill", "AreaMoveStroke", "AreaMoveDropCaret",
    };

    private static UiColor Colour(string hex) => Palette.Parse(hex);

    private static UiColor Window(bool light) => Palette.Get("WindowBackground", light);

    [AvaloniaTheory]
    [InlineData(false, "Dark")]
    [InlineData(true, "Light")]
    public void CrimsonDrivesEveryAccentRoleToken(bool light, string mode)
    {
        try
        {
            var appearance = new AppearanceSettings { ThemeMode = mode };
            ThemeColours.ApplyPreset(appearance, light);
            appearance.Accent = Crimson;
            AvaloniaPalette.Publish(appearance);
            var (bar, icon) = ThemeColours.AccentShades(Colour(Crimson), Window(light), light);
            Assert.Equal(bar, AvaloniaPalette.Get("PrefsPrimary", light));          // the primary (OK) button fill
            Assert.Equal(bar, AvaloniaPalette.Get("SliderTrackFill", light));       // slider and mini slider fill
            Assert.Equal(bar, AvaloniaPalette.ThemeBrush("AccentBrush", light));    // checkbox fill, selected tab underline
            Assert.Equal(bar, AvaloniaPalette.ThemeBrush("SelectionBrush", light));
            Assert.Equal(icon, AvaloniaPalette.Get("IconActive", light));           // the selected tool glyph
            Assert.Equal(icon, AvaloniaPalette.Get("SettingsNavIconSelected", light));
            Assert.True(ThemeColours.Contrast(icon, Window(light)) >= ThemeColours.MinimumAccentContrast);
            Assert.Equal(AvaloniaPalette.Get("AccentSoft", light), AvaloniaPalette.ThemeBrush("AccentSoftBrush", light));   // selected tool and selected row fill
            Assert.Equal(AvaloniaPalette.Get("AccentHover", light), AvaloniaPalette.Get("FocusRing", light));
            foreach (var colour in new[] { bar, icon, AvaloniaPalette.Get("AccentSoft", light), AvaloniaPalette.Get("FocusRing", light) })
                Assert.True(colour.R > colour.B, $"{colour} is not red-based");
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [Theory]
    [InlineData(false, "#4C9AFF", "#38A9F5")]
    [InlineData(true, "#0F4489", "#105EB0")]
    public void DefaultBlueReproducesTodaysTwoShades(bool light, string bar, string icon)
    {
        var (b, i) = ThemeColours.AccentShades(Colour(bar), Window(light), light);
        Assert.Equal(Colour(bar), b);
        var (hue, _, lightness) = ThemeColours.ToHsl(i);
        var (todayHue, _, todayLightness) = ThemeColours.ToHsl(Colour(icon));
        Assert.InRange(Math.Abs(hue - todayHue), 0, 12);
        Assert.InRange(Math.Abs(lightness - todayLightness), 0, 0.02);
    }

    [Theory]
    [InlineData(false, "#000000")]
    [InlineData(true, "#000000")]
    [InlineData(false, "#FFFFFF")]
    [InlineData(true, "#FFFFFF")]
    public void ExtremePicksAreLiftedOrLoweredToTheContrastFloor(bool light, string pick)
    {
        var (bar, icon) = ThemeColours.AccentShades(Colour(pick), Window(light), light);
        Assert.True(ThemeColours.Contrast(bar, Window(light)) >= ThemeColours.MinimumAccentContrast);
        Assert.True(ThemeColours.Contrast(icon, Window(light)) >= ThemeColours.MinimumAccentContrast);
        Assert.True(ThemeColours.Contrast(ThemeColours.InkOn(bar), bar) >= ThemeColours.MinimumLabelContrast);
    }

    [Fact]
    public void TextOnEveryPresetAccentReadsInEveryState()
    {
        foreach (var (name, hex) in NamedColourSwatches.Presets)
            foreach (var light in new[] { false, true })
            {
                var accent = ThemeColours.AccentShades(Colour(hex), Window(light), light).Base;
                var soft = ThemeColours.Blend(accent, Window(light), 0.55);
                var ink = ThemeColours.FromAccent("PrefsPrimaryInk", accent, soft, Window(light), light);
                foreach (var fill in new[] { accent, ThemeColours.FromAccent("AccentHover", accent, soft, Window(light), light), ThemeColours.FromAccent("AccentPress", accent, soft, Window(light), light) })
                    Assert.True(ThemeColours.Contrast(ink, fill) >= MinimumTextContrast, $"{name} light={light} {fill}");
            }
    }

    [Fact]
    public void NoPaletteTokenKeepsTheDefaultAccentBlueUnlessListed()
    {
        var accentDark = Palette.Get("Accent", false);
        var accentLight = Palette.Get("Accent", true);
        var stale = Palette.Names.Where(n => !OwnBlue.Contains(n) && !ThemeColours.DerivesFromAccent(n)
            && (Palette.Get(n, false) == accentDark || Palette.Get(n, true) == accentLight || Palette.Get(n, false) == Palette.Get("AccentHover", false))).ToList();
        Assert.Empty(stale);
    }
}
