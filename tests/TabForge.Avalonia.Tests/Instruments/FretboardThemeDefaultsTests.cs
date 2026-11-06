using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard defaults that follow the theme: Rosewood at 75% in the dark theme and Maple at 90% in the light one, an older file's
///   untouched defaults (Rosewood at 65%) moving to the theme's, and a chosen look or brightness surviving a theme switch.
/// Does not own: when the theme is applied (ThemeSettingsBinding) or the drawing.
/// Tests: FretboardStyles.ThemeDefaults and FollowTheme.
/// </summary>
public class FretboardThemeDefaultsTests
{
    private static string Preset(EditingSettings e) => FretboardStyles.PresetOf(FretboardStyles.LookOf(e));

    [Fact]
    public void TheDarkThemeStartsOnRosewoodAt75AndTheLightOneOnMapleAt90()
    {
        var dark = new EditingSettings();
        FretboardStyles.FollowTheme(dark, light: false);
        Assert.Equal(("Rosewood", 75), (Preset(dark), dark.FretboardBrightness));
        var light = new EditingSettings();
        FretboardStyles.FollowTheme(light, light: true);
        Assert.Equal(("Maple", 90), (Preset(light), light.FretboardBrightness));
    }

    [Fact]
    public void AnOlderFilesUntouchedDefaultsMoveToTheThemesDefaults()
    {
        var old = new EditingSettings { FretboardBrightness = FretboardStyles.LegacyBoardBrightness };   // no defaults theme stored yet
        Assert.True(FretboardStyles.FollowTheme(old, light: true));
        Assert.Equal(("Maple", 90, FretboardStyles.LightTheme), (Preset(old), old.FretboardBrightness, old.FretboardDefaultsTheme));
    }

    [Fact]
    public void ThemeDefaultsSwitchBackAndForthWhileUntouched()
    {
        var e = new EditingSettings();
        FretboardStyles.FollowTheme(e, light: true);
        Assert.False(FretboardStyles.FollowTheme(e, light: true));   // no theme change: nothing to do
        FretboardStyles.FollowTheme(e, light: false);
        Assert.Equal(("Rosewood", 75), (Preset(e), e.FretboardBrightness));
    }

    [Fact]
    public void AChosenLookAndBrightnessSurviveAThemeSwitch()
    {
        var e = new EditingSettings();
        FretboardStyles.FollowTheme(e, light: false);
        FretboardStyles.Apply(e, FretboardStyles.OfPreset("Ebony blocks"));
        e.FretboardBrightness = 110;
        FretboardStyles.FollowTheme(e, light: true);
        Assert.Equal(("Ebony blocks", 110), (Preset(e), e.FretboardBrightness));

        var lightPick = new EditingSettings();
        FretboardStyles.FollowTheme(lightPick, light: true);
        FretboardStyles.Apply(lightPick, FretboardStyles.OfPreset("Rosewood"));   // Rosewood chosen in the light theme
        FretboardStyles.FollowTheme(lightPick, light: false);
        FretboardStyles.FollowTheme(lightPick, light: true);
        Assert.Equal("Rosewood", Preset(lightPick));   // only the brightness, still the default, followed
        Assert.Equal(90, lightPick.FretboardBrightness);
    }
}
