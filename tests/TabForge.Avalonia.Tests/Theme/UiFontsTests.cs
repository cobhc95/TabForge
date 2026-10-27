// Owns: the shared font entry point: the interface typeface and icon size from the appearance, the score style from
//   ScoreText, and the fixed mono family.
// Does not own: the appearance values (AppearanceSettings) or the configured score style (ScoreText).
// Tests: UiFontsTests.
using TabForge.Platform.Theme;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class UiFontsTests
{
    [Fact]
    public void UiFollowsTheAppearance()
    {
        Assert.Equal(new UiFont("Segoe UI", 12), UiFonts.Ui(new AppearanceSettings()));
        var custom = new AppearanceSettings { FontFamily = "Arial", FontSize = 15 };
        Assert.Equal(new UiFont("Arial", 15), UiFonts.Ui(custom));
    }

    [Fact]
    public void IconSizeFollowsTheAppearance()
    {
        Assert.Equal(14, UiFonts.IconSize(new AppearanceSettings()));
        Assert.Equal(18, UiFonts.IconSize(new AppearanceSettings { IconSize = 18 }));
    }

    [Fact]
    public void MonoFamilyIsPinned()
    {
        var fonts = new FontProvider();
        fonts.Configure(FontProvider.WindowsSource);
        Assert.Equal("Consolas", UiFonts.MonoFamily(fonts));
    }

    [Fact]
    public void ScoreMirrorsTheConfiguredStyle()
    {
        try
        {
            ScoreText.ConfigureStyle("Arial", 16, bold: true, italic: false);
            Assert.Equal(new UiFont("Arial", 16, 700, false), UiFonts.Score());
            ScoreText.ConfigureStyle(null, 13.5, bold: false, italic: true);
            Assert.Equal(new UiFont("Segoe UI", 13.5, 400, true), UiFonts.Score());
        }
        finally
        {
            ScoreText.ConfigureStyle("Segoe UI", 13.5, bold: false, italic: false);
        }
    }
}
