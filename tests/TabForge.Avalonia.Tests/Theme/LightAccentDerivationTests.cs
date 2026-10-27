using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the light-theme accent derived from the master accent (lighten when too dark, keep when light, dark theme untouched).
// Does not own: the contrast floor or the other shades (ThemeReadabilityFixTests).
// Tests: this file.
public class LightAccentDerivationTests
{
    private static readonly UiColor Navy = new(0xFF, 0x0F, 0x44, 0x89), LightPick = new(0xFF, 0x4C, 0x9A, 0xFF), Surface = new(0xFF, 0xF5, 0xF5, 0xF5), DarkSurface = new(0xFF, 0x1E, 0x1E, 0x1E);

    private static AppearanceSettings Dark(string accent) { var a = new AppearanceSettings { Accent = accent }; a.Accent = a.Own("Accent", accent); return a; }

    [Fact]
    public void SwitchingToLightLightensDarkNavy()
    {
        var a = Dark("#0F4489");
        ThemeColours.SwitchTheme(a, "Light");
        Assert.NotEqual("#0F4489", a.Accent);
        Assert.True(ColourHex.TryParse(a.Accent, out var c));
        Assert.True(ThemeColours.Luminance(new UiColor(0xFF, c.R, c.G, c.B)) >= ThemeColours.LightAccentMinimumLuminance);
    }

    [Fact]
    public void SwitchingToLightKeepsALightPick()
    {
        var a = Dark("#4C9AFF");
        ThemeColours.SwitchTheme(a, "Light");
        Assert.Equal("#4C9AFF", a.Accent);
    }

    [Fact]
    public void ManualDarkPickInLightStaysAndBackToDarkRestoresTheOriginal()
    {
        var a = Dark("#0F4489");
        ThemeColours.SwitchTheme(a, "Light");
        a.Accent = a.Own("Accent", "#102030");   // the user picks a dark accent while in Light
        ThemeColours.SwitchTheme(a, "Dark");
        Assert.Equal("#102030", a.Accent);
        var b = Dark("#0F4489");
        ThemeColours.SwitchTheme(b, "Light");
        ThemeColours.SwitchTheme(b, "Dark");
        Assert.Equal("#0F4489", b.Accent);
    }
}
