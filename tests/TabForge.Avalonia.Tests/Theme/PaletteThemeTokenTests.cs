using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the full-preset rule: every palette token has its own dark and light value (a token the same in both is listed in
//   NeutralPaletteTokens.json, which may only shrink), and the light master tokens read on the light surfaces.
// Does not own: the token values' use in drawing (DrawingArchitectureTests, ThemeArchitectureTests) or the preset write (ThemePresetTests).
// Tests: this file.
public sealed class PaletteThemeTokenTests
{
    private static HashSet<string> Neutral()
    {
        var path = Path.Combine(ArchitectureScan.RepoRoot(), "Source", "tests", "TabForge.Avalonia.Tests", "NeutralPaletteTokens.json");
        return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))!.ToHashSet(StringComparer.Ordinal);
    }

    private static UiColor Light(string name) => Palette.Get(name, light: true);
    private static UiColor Dark(string name) => Palette.Get(name, light: false);

    [Fact]
    public void EveryTokenHasADistinctLightValueOrIsListedAsThemeNeutral()
    {
        var neutral = Neutral();
        var same = Palette.Names.Where(n => Dark(n) == Light(n)).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(same.Except(neutral).OrderBy(n => n));
        Assert.Empty(neutral.Except(same).OrderBy(n => n));   // a token given its own light value must leave the list: it only shrinks
    }

    [Theory]
    [InlineData("Accent", "PanelBackground", 4.5)]
    [InlineData("Accent", "WindowBackground", 4.5)]
    [InlineData("Accent", "CardBackground", 4.5)]
    [InlineData("Accent", "RaisedBackground", 4.5)]
    [InlineData("Accent", "ChromeStrip", 4.5)]
    [InlineData("OnAccent", "Accent", 4.5)]
    [InlineData("AccentHover", "PanelBackground", 4.5)]
    [InlineData("AccentPress", "PanelBackground", 4.5)]
    [InlineData("TextPrimary", "WindowBackground", 4.5)]
    [InlineData("TextMuted", "PanelBackground", 4.5)]
    [InlineData("TextLegible", "PanelBackground", 4.5)]
    [InlineData("FocusRing", "PanelBackground", 3.0)]
    [InlineData("IconDefault", "PanelBackground", 4.5)]
    [InlineData("IconActive", "PanelBackground", 4.5)]
    [InlineData("Danger", "PanelBackground", 4.5)]
    [InlineData("Success", "PanelBackground", 3.0)]
    [InlineData("ScoreInkDefault", "ScorePaper", 4.5)]
    public void LightTokensReadOnTheirSurface(string foreground, string background, double ratio)
    {
        var fg = foreground switch { "ScoreInkDefault" => Light("ScoreLightInkDefault"), _ => Light(foreground) };
        var bg = background switch { "ScorePaper" => Light("ScoreLightPaperDefault"), _ => Light(background) };
        Assert.True(ThemeColours.Contrast(fg, bg) >= ratio, $"{foreground} on {background}: {ThemeColours.Contrast(fg, bg):0.00} < {ratio}");
    }

    [Theory]
    [InlineData("ScoreLightStaffLineDefault")]
    [InlineData("Playhead")]
    [InlineData("ScoreCursor")]
    [InlineData("ScoreCursorTabBox")]
    [InlineData("ScoreErrorRed")]
    [InlineData("Accent")]
    [InlineData("Selection")]
    public void LightScoreMarksReadOnTheLightPaper(string token)
    {
        var paper = Light("ScoreLightPaperDefault");
        Assert.True(ThemeColours.Luminance(paper) > 0.85, "the light paper is near-white");
        Assert.True(ThemeColours.Contrast(Light(token), paper) >= 3.0, $"{token} on the light paper: {ThemeColours.Contrast(Light(token), paper):0.00}");
    }

    [Fact]
    public void ThePresetsAreBuiltFromTheMasterTokens()
    {
        var a = new AppearanceSettings();
        ThemeColours.ApplyPreset(a, light: true);
        Assert.Equal(Palette.Hex("Accent", true), a.Accent);
        Assert.Equal(Palette.Hex("Playhead", true), a.PlayheadColour);
        Assert.Equal(Palette.Hex("ScoreLightPaperDefault", true), a.LightScorePaperColour);
        ThemeColours.ApplyPreset(a, light: false);
        Assert.Equal(Palette.Hex("Accent", false), a.Accent);
        Assert.Equal(new AppearanceSettings().Accent, a.Accent);
    }
}
