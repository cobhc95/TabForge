// Owns: the light-mode timeline and track-list redesign: its palette tokens, no row wash, clip-over-lane contrast, full-colour section chips, and the dark values left as they were.
// Does not own: the painters themselves (TimelineView, TrackHeaderColumn, TimelineAudioClips) or the shared Instrument tokens.
// Tests: LightTimelineTokenTests (this file).
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class LightTimelineTokenTests
{
    private static string Hex(string name, bool light) => Palette.Hex(name, light);

    [Theory]
    [InlineData("TimelineRow", "#EAEAE8")]
    [InlineData("TimelineRowAlt", "#EAEAE8")]
    [InlineData("TimelineLane", "#D9D9D7")]
    [InlineData("TimelineSectionLane", "#D9D9D7")]
    [InlineData("TimelineLine", "#C2C2C0")]
    [InlineData("WindowBackground", "#E2E2E0")]
    [InlineData("PanelBackground", "#E2E2E0")]
    [InlineData("ChromeStrip", "#D9D9D7")]
    [InlineData("Workspace", "#A0A0A0")]
    public void LightTokensHaveTheRedesignValues(string name, string hex) => Assert.Equal(hex, Hex(name, true));

    [Fact]
    public void TabActiveIsLighterThanTheChromeStrip() =>
        Assert.True(ThemeColours.Contrast(Palette.Get("TabActive", true), Palette.Get("ChromeStrip", true)) > 1.0
            && Palette.Get("TabActive", true).G > Palette.Get("ChromeStrip", true).G);

    [Theory]
    [InlineData("TimelineRow", "InstrumentBackground")]
    [InlineData("TimelineRowAlt", "InstrumentRowAlt")]
    [InlineData("TimelineLane", "InstrumentBoard")]
    [InlineData("TimelineSectionLane", "InstrumentBackground")]
    [InlineData("TimelineLine", "InstrumentBoardEdge")]
    public void DarkTimelineTokensEqualTheValuesTheyReplaced(string token, string old) => Assert.Equal(Hex(old, false), Hex(token, false));

    [Fact]
    public void DarkWindowAndChromeValuesAreUnchanged()
    {
        Assert.Equal("#14161A", Hex("WindowBackground", false));
        Assert.Equal("#1C1F24", Hex("PanelBackground", false));
        Assert.Equal("#161616", Hex("ChromeStrip", false));
        Assert.Equal("#333333", Hex("TabActive", false));
        Assert.Equal("#0B0D10", Hex("Workspace", false));
    }

    private static TrackModel Track(string hex) => new() { ColorHex = hex, TintRow = true };

    [Fact]
    public void LightThemeWashesNoRowButDarkStillDoes()
    {
        Assert.Null(TrackRowTint.Of(Track("#FF3A9B68"), true, 40));
        Assert.NotNull(TrackRowTint.Of(Track("#FF3A9B68"), false, 40));
    }

    [Fact]
    public void ClipsContrastTheirLaneForEveryPresetColour()
    {
        var lane = Palette.Get("TimelineLane", true);
        foreach (var (name, hex) in NamedColourSwatches.All)
        {
            var parsed = ColourHex.TryParse(hex, out var rgba) ? rgba : throw new System.FormatException(hex);
            var tamed = SectionColourPresenter.Tame(parsed);
            var clip = ThemeColours.Blend(lane, new UiColor(255, tamed.R, tamed.G, tamed.B), 0.9);
            var ratio = ThemeColours.Contrast(clip, lane);
            Assert.True(ratio >= 1.5, $"{name}: clip over lane {ratio:0.00}:1 < 1.5:1.");
        }
    }

    [Fact]
    public void LightSectionChipIsTheSectionColourWhole()
    {
        var section = new UiColor(255, 0xF2, 0xB1, 0x38);
        var row = Palette.Get("TimelineRow", true);
        var ink = TimelineView.LabelInk(section, row);
        Assert.Equal(TimelineView.SectionLabelWhite, ink);
    }
}
