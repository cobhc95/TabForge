using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the pinned theme values of three shared looks: the highlighted menu row (HoverBrush) keeps text contrast of 4.5:1, the selected and hover
/// tab shades are the look table's Panel2Brush and HoverBrush, and the light dialog body is a mid grey (WindowBrush) darker than its footer panel with a visible frame edge.
/// Does not own: the values themselves (ThemeColours) or the styles that apply them (AvaloniaLooks, AvaloniaFormControlStyles).
/// Tests: this class.
/// </summary>
public class MenuTabDialogShadeTests
{
    private const double MinimumTextContrast = 4.5;
    private const double MinimumFrameContrast = 1.2;

    private static UiColor? ParseHex(string? text) =>
        text is { Length: 7 } && text[0] == '#' && int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? new UiColor(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : null;

    private static Dictionary<string, UiColor> Brushes(bool light)
    {
        var settings = new AppearanceSettings();
        ThemeColours.ApplyPreset(settings, light);
        return ThemeColours.Brushes(settings, light, ParseHex).ToDictionary(p => p.Key, p => p.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighlightedMenuRowKeepsReadableText(bool light)
    {
        var b = Brushes(light);
        Assert.True(ThemeColours.Contrast(b["TextBrush"], AvaloniaPalette.Get("HoverSurface", light)) >= MinimumTextContrast);
        var highlighted = LookTable.Looks["MenuItem"].Rules.First(r => r.Name == "Highlighted");
        Assert.Equal("HoverBrush", highlighted.Set.Single().Brush);
    }

    [Fact]
    public void SelectedAndHoverTabShadesAreTheLookTableBrushes()
    {
        var rules = LookTable.Looks["Tab"].Rules;
        Assert.Equal("Panel2Brush", rules.First(r => r.Name == "Selected").Set.First().Brush);
        Assert.Equal("HoverBrush", rules.First(r => r.Name == "HoverNotSelected").Set.Single().Brush);
        foreach (var light in new[] { false, true })
            Assert.NotEqual(Brushes(light)["Panel2Brush"], AvaloniaPalette.Get("HoverSurface", light));
    }

    [Fact]
    public void LightDialogIsMidGreyWithDarkerBodyThanFooterAndVisibleFrame()
    {
        var b = Brushes(true);
        // Stale pin updated after the light-mode restyle: the light dialog shade is the palette token (WindowBackground, a light grey), no longer a hard-coded #B4B4B4,
        // and the body is no longer darker than the footer panel: both are the same token.
        Assert.Equal(AvaloniaPalette.Get("WindowBackground", true), b["WindowBrush"]);
        Assert.True(ThemeColours.Luminance(b["WindowBrush"]) <= ThemeColours.Luminance(b["PanelBrush"]));
        Assert.True(ThemeColours.Contrast(b["BorderBrush"], b["WindowBrush"]) >= MinimumFrameContrast);
    }
}
