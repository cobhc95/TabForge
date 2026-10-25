using System;
using System.Linq;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the theme preset rules: only explicit Dark/Light write the preset colours, both are symmetric (paper, ink and
//   staff lines), and System/Custom never write a colour.
// Does not own: applying a preset to a window (ThemeLiveSwitchTests) or the row layout (PreferencesPagesTests).
// Tests: this file.
public class ThemePresetTests
{
    private static SettingDescriptor ThemeRow(AppSettings settings) =>
        PreferencesCatalog.Build(settings).Single(row => row.Key == "appearance.thememode");

    [Fact]
    public void DarkAndLightPresetsWriteTheirOwnPaperAndInk()
    {
        var defaults = new AppearanceSettings();
        var settings = new AppSettings();
        var appearance = settings.Appearance;
        appearance.DarkScorePaperColour = "#000001";
        appearance.DarkScoreInkColour = "#000002";
        appearance.DarkScoreLinesColour = "#000003";
        appearance.LightScorePaperColour = "#000004";
        appearance.LightScoreInkColour = "#000005";
        appearance.LightScoreLinesColour = "#000006";

        ThemeRow(settings).Set("Dark");
        Assert.Equal("Dark", appearance.ThemeMode);
        Assert.Equal("Dark", appearance.ScorePaper);
        Assert.Equal(defaults.DarkScorePaperColour, appearance.DarkScorePaperColour);
        Assert.Equal(defaults.DarkScoreInkColour, appearance.DarkScoreInkColour);
        Assert.Equal(defaults.DarkScoreLinesColour, appearance.DarkScoreLinesColour);

        ThemeRow(settings).Set("Light");
        Assert.Equal("Light", appearance.ThemeMode);
        Assert.Equal("Light", appearance.ScorePaper);
        Assert.Equal(defaults.LightScorePaperColour, appearance.LightScorePaperColour);
        Assert.Equal(defaults.LightScoreInkColour, appearance.LightScoreInkColour);
        Assert.Equal(defaults.LightScoreLinesColour, appearance.LightScoreLinesColour);
    }

    [Theory]
    [InlineData("Custom")]
    [InlineData("System")]
    public void CustomAndSystemWriteNoColour(string mode)
    {
        var settings = new AppSettings();
        var appearance = settings.Appearance;
        appearance.Background = "#111111";
        appearance.Accent = "#222222";
        appearance.DarkScorePaperColour = "#333333";
        appearance.LightScorePaperColour = "#444444";
        appearance.ScorePaper = "Light";

        ThemeRow(settings).Set(mode);

        Assert.Equal(mode, appearance.ThemeMode);
        Assert.Equal("#111111", appearance.Background);
        Assert.Equal("#222222", appearance.Accent);
        Assert.Equal("#333333", appearance.DarkScorePaperColour);
        Assert.Equal("#444444", appearance.LightScorePaperColour);
        Assert.Equal("Light", appearance.ScorePaper);
    }

    [Fact]
    public void TheThemeRowStatesThePresetRule()
    {
        var text = ThemeRow(new AppSettings()).Description;
        Assert.Contains("System follows", text);
        // PREFSPLAIN reworded the row to plain text: "Custom keeps all your colours as they are." The rule itself is unchanged.
        Assert.Contains("Custom keeps all your colours", text);
    }
}
