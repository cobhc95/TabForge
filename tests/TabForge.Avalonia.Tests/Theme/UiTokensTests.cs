// Owns: the shared colour and opacity entry points: every palette name resolving in both themes, the snapshot beating
//   the live settings, the pinned opacity table, and the ThemeColours forwarders.
// Does not own: the palette or brush values (Palette, ThemeColours) or any toolkit publishing.
// Tests: UiTokensTests.
using System;
using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class UiTokensTests
{
    private static UiColor? ParseColourText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return Palette.Parse(text.Trim());
        }
        catch (Exception)
        {
            return null;
        }
    }

    [Fact]
    public void EveryPaletteNameResolvesInBothThemes()
    {
        var names = new List<string>(Palette.Names);
        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            var dark = Palette.Get(name, light: false);
            var light = Palette.Get(name, light: true);
            Assert.Equal(Palette.Get(name), dark);
            Assert.Equal(UiTokens.Colour(name, false), dark);
            Assert.Equal(UiTokens.Colour(name, true), light);
        }
    }

    [Fact]
    public void UnknownColourNameThrows()
    {
        Assert.Throws<ArgumentException>(() => UiTokens.Colour("NoSuchColour", light: false));
    }

    [Fact]
    public void InkPrefersTheAppearanceTableOverThePalette()
    {
        var appearance = new AppearanceSettings();
        var tokens = UiTokens.For(appearance, light: false, ParseColourText);
        var expected = new Dictionary<string, UiColor>();
        foreach (var entry in ThemeColours.Brushes(appearance, false, ParseColourText))
            expected[entry.Key] = entry.Value;
        Assert.NotEmpty(expected);
        Assert.Equal(expected["TextBrush"], tokens.Ink("TextBrush"));
        Assert.Equal(Palette.Get("White"), tokens.Ink("White"));
        Assert.True(tokens.Light == false);
        Assert.Equal(UiTokens.Colour("White", true), UiTokens.For(appearance, true, ParseColourText).Ink("White"));
    }

    [Fact]
    public void SnapshotBeatsLaterSettingsEdits()
    {
        var appearance = new AppearanceSettings();
        var tokens = UiTokens.For(appearance, light: false, ParseColourText);
        var before = tokens.Ink("TextBrush");
        appearance.Text = "#FFFFFF";
        Assert.Equal(before, tokens.Ink("TextBrush"));
        Assert.Equal(Palette.Parse("#FFFFFF"), UiTokens.For(appearance, false, ParseColourText).Ink("TextBrush"));
    }

    [Fact]
    public void OpacityTableKeepsItsValues()
    {
        var tokens = UiTokens.For(new AppearanceSettings(), light: false, ParseColourText);
        Assert.Equal(0.35, tokens.Opacity("glow.halo"));
        Assert.Equal(0.70, tokens.Opacity("glow.active"));
        Assert.Equal(0.12, tokens.Opacity("glow.ring"));
        Assert.Equal(0.28, tokens.Opacity("glow.hover"));
        Assert.Equal(0.85, tokens.Opacity("glow.press"));
        Assert.Equal(0.35, tokens.Opacity("clip.outline"));
        Assert.Equal(0.8, tokens.Opacity("barMarker.stroke"));
        Assert.Equal(0.72, tokens.Opacity("playhead.shadow"));
        Assert.Equal(0.52, tokens.Opacity("transport.shadow"));
        Assert.Equal(0.20, tokens.Opacity("fretboard.halo"));
        Assert.Equal(0.7, tokens.Opacity("fretboard.releasedRing"));
        Assert.Equal(0.30, tokens.Opacity("fretboard.nextFill"));
        Assert.Equal(0.92, tokens.Opacity("fretboard.tagFill"));
        Assert.Equal(0.75, tokens.Opacity("fretboard.movement"));
        Assert.Throws<KeyNotFoundException>(() => tokens.Opacity("no.such.token"));
    }

    [Fact]
    public void WithScalesOnlyTheAlpha()
    {
        var tokens = UiTokens.For(new AppearanceSettings(), light: false, ParseColourText);
        Assert.Equal(new UiColor(89, 10, 20, 30), tokens.With(new UiColor(255, 10, 20, 30), "clip.outline"));
    }

    [Fact]
    public void ForwardersMatchThemeColours()
    {
        var first = new UiColor(255, 32, 64, 128);
        var second = new UiColor(255, 200, 100, 50);
        Assert.Equal(ThemeColours.Blend(first, second, 0.4), UiTokens.Blend(first, second, 0.4));
        Assert.Equal(ThemeColours.Shade(first, 1.25), UiTokens.Shade(first, 1.25));
        Assert.Equal(ThemeColours.InkOn(first), UiTokens.InkOn(first));
        Assert.Equal(ThemeColours.Contrast(first, second), UiTokens.Contrast(first, second));
        Assert.Equal(new UiColor(255, 255, 255, 255), UiTokens.MeasureInk);
    }
}
