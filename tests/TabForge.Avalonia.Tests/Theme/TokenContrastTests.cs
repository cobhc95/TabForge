// Owns: the S6a palette contrast ratchet: every listed text pair clears 4.5:1 and every listed
//   line/icon pair clears 3:1 in both themes, computed from the theme tables (never hexes).
// Does not own: the palette values (Palette/*.json) or the contrast maths (ThemeColours).
// Tests: TokenContrastTests (this file).
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class TokenContrastTests
{
    private static string ThemeName(bool light) => light ? "light" : "dark";

    private static void AssertText(string foreground, string background, bool light)
    {
        var ratio = ThemeColours.Contrast(Palette.Get(foreground, light), Palette.Get(background, light));
        Assert.True(ratio >= 4.5, $"{foreground} on {background} ({ThemeName(light)}): {ratio:0.00}:1 < 4.5:1.");
    }

    private static void AssertLine(string foreground, string background, bool light)
    {
        var ratio = ThemeColours.Contrast(Palette.Get(foreground, light), Palette.Get(background, light));
        Assert.True(ratio >= 3.0, $"{foreground} on {background} ({ThemeName(light)}): {ratio:0.00}:1 < 3:1.");
    }

    [Fact]
    public void TextPairsMeetFourPointFiveInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            AssertText("KeyboardKeyLabel", "KeyboardWhiteKey", light);
            AssertText("InstrumentLegible", "InstrumentBoard", light);
            AssertText("InstrumentText", "InstrumentBoard", light);
        }
    }

    [Fact]
    public void LinePairsMeetThreeInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            AssertLine("InstrumentFretWire", "InstrumentBoard", light);
            AssertLine("ClipOutline", "InstrumentBackground", light);
            AssertLine("TimelinePlayhead", "InstrumentBackground", light);
            AssertLine("SectionDefault", "InstrumentBoard", light);
        }
    }

    [Fact]
    public void CursorHighlightMeetsThreeOnScorePaperInBothThemes()
    {
        var appearance = new AppearanceSettings();
        var darkPaper = Palette.Parse(appearance.DarkScorePaperColour);
        var lightPaper = Palette.Parse(appearance.LightScorePaperColour);
        var darkRatio = ThemeColours.Contrast(Palette.Get("ScoreCursorTabBox", light: false),
            new UiColor(darkPaper.A, darkPaper.R, darkPaper.G, darkPaper.B));
        Assert.True(darkRatio >= 3.0, $"ScoreCursorTabBox on score paper (dark): {darkRatio:0.00}:1 < 3:1.");
        var lightRatio = ThemeColours.Contrast(Palette.Get("ScoreCursorTabBox", light: true),
            new UiColor(lightPaper.A, lightPaper.R, lightPaper.G, lightPaper.B));
        Assert.True(lightRatio >= 3.0, $"ScoreCursorTabBox on score paper (light): {lightRatio:0.00}:1 < 3:1.");
    }
}
