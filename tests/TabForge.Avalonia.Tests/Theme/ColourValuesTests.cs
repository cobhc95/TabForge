using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the one opacity composite and the one strict hex read shared by the Avalonia views.
/// Does not own: palette lookup (AvaloniaPalette) or the Core colour rules (ColourHex).
/// Tests: TabForge.Avalonia.Tests.ColourValuesTests.
/// </summary>
public class ColourValuesTests
{
    [Theory]
    [InlineData(255, 0.5, 128)]
    [InlineData(200, 0.5, 100)]
    [InlineData(3, 0.5, 2)]
    [InlineData(100, 0.8, 80)]
    [InlineData(64, 1.0, 64)]
    public void WithOpacityRoundsTheSourceAlphaAsTheThreeOldCopiesDid(byte alpha, double opacity, byte expected) =>
        Assert.Equal(expected, new UiColor(alpha, 1, 2, 3).WithOpacity(opacity).A);

    [Fact]
    public void WithOpacityKeepsTheOtherChannelsAndClampsAtOpaque()
    {
        var faded = new UiColor(200, 10, 20, 30).WithOpacity(2.0);   // opacity is clamped to 1, so the alpha is unchanged
        Assert.Equal((10, 20, 30, (byte)200), (faded.R, faded.G, faded.B, faded.A));
    }

    [Theory]
    [InlineData("#4C9AFF", true, 0x4C, 0x9A, 0xFF, 255)]
    [InlineData("4C9AFF", true, 0x4C, 0x9A, 0xFF, 255)]
    [InlineData("#804C9AFF", true, 0x4C, 0x9A, 0xFF, 0x80)]
    [InlineData("80 4C9AFF", false, 0, 0, 0, 0)]
    [InlineData("#4C9", false, 0, 0, 0, 0)]
    [InlineData("#GGGGGG", false, 0, 0, 0, 0)]
    [InlineData("red", false, 0, 0, 0, 0)]
    [InlineData("", false, 0, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0, 0)]
    public void StrictHexAcceptsOnlyTheLongHexForms(string? text, bool parsed, byte r, byte g, byte b, byte a)
    {
        var colour = ColourValues.FromHex(text);
        Assert.Equal(parsed, colour is not null);
        if (colour is { } value) Assert.Equal((r, g, b, a), (value.R, value.G, value.B, value.A));
    }

    [Fact]
    public void StrictHexReturnsTheCallersFallbackForAnythingElse()
    {
        var fallback = new Rgba(0x80, 0x80, 0x80);
        Assert.Equal(fallback, ColourValues.FromHex("not a colour", fallback));
        Assert.Null(ColourValues.FromHex("not a colour"));
    }
}
