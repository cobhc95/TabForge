using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: accent readability by surface luminance, the repaint on a variant change, and the derived-colour cache per publish.
// Does not own: the default shades (AccentDerivationTests).
// Tests: this file.
public class ThemeReadabilityFixTests
{
    [Theory]
    [InlineData("#F5F5F5", false)]
    [InlineData("#101014", true)]
    public void PaleOrDarkAccentReadsOnTheOppositeSurfaceOfItsVariant(string surfaceHex, bool light)
    {
        var surface = Palette.Parse(surfaceHex);
        var pick = light ? Palette.Parse("#101010") : Palette.Parse("#F0F0F0");
        var (bar, icon) = ThemeColours.AccentShades(pick, surface, light);
        Assert.True(ThemeColours.Contrast(bar, surface) >= ThemeColours.MinimumAccentContrast);
        Assert.True(ThemeColours.Contrast(icon, surface) >= ThemeColours.MinimumAccentContrast);
    }

    [AvaloniaFact]
    public void VariantChangeWithEqualTablesStillQueuesARepaint()
    {
        var window = new Window();
        var root = new LayoutTransformControl();
        try
        {
            ThemeSettingsBinding.ApplyWindow(window, root, new AppearanceSettings { ThemeMode = "System" });
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(AvaloniaPalette.InvalidateQueued);
            ThemeSettingsBinding.ApplyWindow(window, root, new AppearanceSettings { ThemeMode = "Dark" });
            Assert.True(AvaloniaPalette.InvalidateQueued);
            Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        }
        finally { AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [AvaloniaFact]
    public void DerivedColoursAreCachedBetweenPublishes()
    {
        AvaloniaPalette.Publish(new AppearanceSettings { ThemeMode = "Custom", Accent = "#FF0080" });
        var first = AvaloniaPalette.Get("IconActive", light: false);
        var count = AvaloniaPalette.DerivedCacheCount;
        Assert.True(count > 0);
        Assert.Equal(first, AvaloniaPalette.Get("IconActive", light: false));
        Assert.Equal(count, AvaloniaPalette.DerivedCacheCount);
        AvaloniaPalette.Publish(new AppearanceSettings { ThemeMode = "Custom", Accent = "#00FF00" });
        Assert.NotEqual(first, AvaloniaPalette.Get("IconActive", light: false));
        AvaloniaPalette.Publish(new AppearanceSettings());
    }
}
