using System.Linq;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the user overrides layered over a theme preset: a colour set in Preferences survives a Dark/Light switch, an unset one follows
//   the preset, and "Reset colours" drops the overrides and writes the preset again.
// Does not own: the preset values (PaletteThemeTokenTests) or the mode choice rules (ThemePresetTests).
// Tests: this file.
public class ThemeOverrideTests
{
    private static SettingDescriptor Row(AppSettings settings, string key) => PreferencesCatalog.Build(settings).Single(row => row.Key == key);

    [Fact]
    // The accent is bright enough for the Light preset: a too-dark one is lightened on the switch to Light (ThemeColours.SwitchTheme), which is its own rule.
    public void AColourTheUserSetSurvivesAPresetSwitchAndTheRestFollowsThePreset()
    {
        var settings = new AppSettings();
        Row(settings, "appearance.accent").Set("#CC4400");
        Row(settings, "appearance.thememode").Set("Light");
        var appearance = settings.Appearance;
        Assert.Equal("#CC4400", appearance.Accent);
        Assert.Equal(Palette.Hex("PanelBackground", true), appearance.Panel);
        Row(settings, "appearance.thememode").Set("Dark");
        Assert.Equal("#CC4400", appearance.Accent);
        Assert.Equal(Palette.Hex("PanelBackground", false), appearance.Panel);
    }

    [Fact]
    public void ResetToPresetDropsTheOverrides()
    {
        var settings = new AppSettings();
        Row(settings, "appearance.accent").Set("#AA3300");
        Row(settings, "appearance.scorepaper.light").Set("#EEEEEE");
        Assert.True(settings.Appearance.IsOverridden("Accent"));
        ThemeColours.ResetToPreset(settings.Appearance, light: true);
        Assert.False(settings.Appearance.IsOverridden("Accent"));
        Assert.Equal(Palette.Hex("Accent", true), settings.Appearance.Accent);
        Assert.Equal(Palette.Hex("ScoreLightPaperDefault", true), settings.Appearance.LightScorePaperColour);
    }

    [Fact]
    public void AnUnsetColourIsNotAnOverride()
    {
        var appearance = new AppearanceSettings();
        Assert.False(appearance.IsOverridden("Accent"));
        ThemeColours.ApplyPreset(appearance, light: true);
        Assert.Equal("", appearance.OverriddenColours);
    }
}
