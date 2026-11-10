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

// Owns: the light theme's guarantees: every palette entry has a light value, the text and icon pairs of the normal, selected and disabled states read
//   in both themes, and a reset or an older settings file never pins the dark colours under the light theme.
// Does not own: the palette values (Palette/*.json), the brush table (ThemeColours) or the resource publishing (ThemeTests).
// Tests: this class.
public sealed class LightThemeTests
{
    [Fact]
    public void EveryPaletteEntryHasALightValue()
    {
        var asm = typeof(Palette).Assembly;
        var files = asm.GetManifestResourceNames().Where(n => n.Contains(".Theme.Palette.") && n.EndsWith(".json")).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var stream = asm.GetManifestResourceStream(file)!;
            foreach (var entry in JsonSerializer.Deserialize<List<PaletteEntry>>(stream)!)
                Assert.True(entry.Value is not null || entry.Light is not null, $"{entry.Name} in {file} has no light value.");
        }
    }

    // Foreground, background, lowest contrast: body text 4.5, icons and muted labels 3 (WCAG non-text), a disabled glyph still visible at 1.5.
    private static readonly (string Ink, string Surface, double Minimum)[] Pairs =
    {
        ("TextBrush", "PanelBrush", 4.5), ("TextBrush", "WindowBrush", 4.5), ("TextBrush", "Panel2Brush", 4.5),
        ("MutedBrush", "PanelBrush", 3.0), ("SecondaryTextBrush", "PanelBrush", 3.0),
        ("TextBrush", "TabActiveBrush", 4.5), ("TextBrush", "SelectedTint", 4.5),
        ("IconDefault", "PanelBrush", 3.0), ("IconActive", "PanelBrush", 3.0), ("IconDefault", "SelectedTint", 3.0),   // a selected toggle keeps the normal glyph ink on its tint
        ("IconDisabled", "PanelBrush", 1.5),
        // The selected text on the selected tints, and the ink on the solid fills (3.0: the owner's light primary shade with white text).
        ("SelectedInk", "SelectedTint", 4.5), ("SelectedInk", "AccentSoftBrush", 4.5), ("ModePillOnText", "ModePillOn", 4.5),
        ("OnAccentBrush", "PrimaryFill", 3.0), ("PrefsPrimaryInk", "PrefsPrimary", 3.0), ("CheckTick", "CheckFill", 3.0), ("AccentBrush", "WindowBrush", 3.0),
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateInkReadsOnItsSurfaceInTheTheme(bool light)
    {
        var appearance = new AppearanceSettings();
        ThemeColours.SwitchTheme(appearance, light ? "Light" : "Dark");
        var brushes = ThemeColours.Brushes(appearance, light, Parse).ToDictionary(p => p.Key, p => p.Value);
        var accent = brushes["AccentBrush"];
        UiColor Colour(string name) => brushes.TryGetValue(name, out var c) ? c
            : !light && ThemeColours.DarkTwin(name) is { } twin ? Colour(twin == "Accent" ? "AccentBrush" : twin == "TextStrong" ? "TextStrongBrush" : twin == "WindowBackground" ? "WindowBrush" : twin)
            : ThemeColours.DerivesFromAccent(name) ? ThemeColours.FromAccent(name, accent, brushes["AccentSoftBrush"], brushes["WindowBrush"], light)
            : Palette.Get(name, light);
        foreach (var (ink, surface, minimum) in Pairs)
        {
            var contrast = ThemeColours.Contrast(Colour(ink), Colour(surface));
            Assert.True(contrast >= minimum, $"{(light ? "Light" : "Dark")}: {ink} on {surface} is {contrast:0.00}, below {minimum}.");
        }
    }

    [Fact]
    public void ResettingAPageLeavesItsColoursToThePreset()
    {
        var settings = new AppSettings();
        var defaults = SettingsCatalog.Build(new AppSettings()).ToDictionary(r => r.Key);
        settings.Appearance.Panel = settings.Appearance.Own("Panel", "#203040");
        settings.Appearance.CursorColour = settings.Appearance.Own("CursorColour", "#123456");
        // As the Settings window's page reset writes its rows (PreferencesWindow.ResetPageAsync); the Score page is left alone here.
        settings.Appearance.WriteUnowned(() =>
        {
            foreach (var row in SettingsCatalog.Build(settings).Where(r => r.Category == "Appearance" && r.Kind is SettingKind.Colour or SettingKind.Choice).OrderBy(r => r.Key == "appearance.thememode" ? 0 : 1))
                row.Set(defaults[row.Key].Get());
        });
        Assert.Equal("CursorColour", settings.Appearance.OverriddenColours);   // the Score page's pick stays the user's

        ThemeColours.SwitchTheme(settings.Appearance, "Light");
        Assert.Equal(Palette.Hex("PanelBackground", true), settings.Appearance.Panel);
        Assert.Equal(Palette.Hex("TextPrimary", true), settings.Appearance.Text);
    }

    [Fact]
    public void AColourOtherThanThePresetStaysTheUsersOwn()
    {
        var a = new AppearanceSettings();
        a.Panel = a.Own("Panel", "#203040");
        ThemeColours.SwitchTheme(a, "Light");
        Assert.Equal("#203040", a.Panel);
        Assert.True(a.IsOverridden("Panel"));
    }

    [Fact]
    public void AFileWithEveryPresetColourMarkedIsRepairedOnLoad()
    {
        // The shape of a file written after a reset: Light chosen, every colour still the dark preset and marked as the user's.
        var a = new AppearanceSettings
        {
            ThemeMode = "Light",
            OverriddenColours = string.Join(',', ThemeColours.PresetFields) + ",Accent2",
            Accent = "#4C9AFF", Panel = "#1C1F24", Background = "#14161A", Text = "#E7EAEF", CursorColour = "#123456",
        };
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new AppSettings { Appearance = a }))!;
        node["Appearance"]!.AsObject().Remove(nameof(AppearanceSettings.OverrideRepairVersion));   // written before the repair
        var json = node.ToJsonString();
        var loaded = SettingsMigration.Normalize(json, JsonSerializer.Deserialize<AppSettings>(json)!);
        var appearance = loaded.Appearance;
        Assert.Equal(Palette.Hex("PanelBackground", true), appearance.Panel);
        Assert.Equal(Palette.Hex("WindowBackground", true), appearance.Background);
        Assert.Equal(Palette.Hex("TextPrimary", true), appearance.Text);
        Assert.False(appearance.IsOverridden("Panel"));
        Assert.Equal("#123456", appearance.CursorColour);   // not a preset colour: still the user's
        Assert.True(appearance.IsOverridden("CursorColour"));
    }

    [Fact]
    public void ARepairedFileKeepsAPresetColourTheUserPicksLater()
    {
        var a = new AppearanceSettings();
        a.Accent = a.Own("Accent", "#4C9AFF");
        var json = JsonSerializer.Serialize(new AppSettings { Appearance = a });
        Assert.True(SettingsMigration.Normalize(json, JsonSerializer.Deserialize<AppSettings>(json)!).Appearance.IsOverridden("Accent"));
    }

    // The default light accent and three picks of other hues: every derived light shade keeps its text readable.
    [Theory]
    [InlineData("#2F7BE0")]
    [InlineData("#2E9E4F")]
    [InlineData("#E07A1F")]
    [InlineData("#8A4FD8")]
    public void LightAccentShadesKeepTheirTextReadable(string pick)
    {
        var accent = Parse(pick)!.Value;
        var tint = ThemeColours.LightRole(ThemeColours.LightAccentRole.Tint, accent);
        Assert.True(ThemeColours.Contrast(ThemeColours.LightRole(ThemeColours.LightAccentRole.Ink, accent), tint) >= ThemeColours.SelectedInkContrast);
        Assert.True(ThemeColours.Contrast(ThemeColours.InkLight, ThemeColours.LightRole(ThemeColours.LightAccentRole.Fill, accent)) >= ThemeColours.LightFillInkContrast);
    }

    [Fact]
    public void TheDefaultLightAccentGivesTheOutlinedShades()
    {
        var appearance = new AppearanceSettings();
        ThemeColours.SwitchTheme(appearance, "Light");
        var accent = ThemeColours.Brushes(appearance, true, Parse).Single(p => p.Key == "AccentBrush").Value;
        Near("#2F7BE0", accent);
        Near("#EAF2FC", ThemeColours.LightRole(ThemeColours.LightAccentRole.Tint, accent));
        Near("#9CC0F2", ThemeColours.LightRole(ThemeColours.LightAccentRole.Border, accent));
        Near("#6FA6EE", ThemeColours.LightRole(ThemeColours.LightAccentRole.Slider, accent));
        Near("#3A86E8", ThemeColours.LightRole(ThemeColours.LightAccentRole.Fill, accent), 16);
        Near("#1F5FBF", ThemeColours.LightRole(ThemeColours.LightAccentRole.Ink, accent), 24);
    }

    private static void Near(string expected, UiColor actual, int tolerance = 6)
    {
        var e = Parse(expected)!.Value;
        Assert.True(Math.Abs(e.R - actual.R) <= tolerance && Math.Abs(e.G - actual.G) <= tolerance && Math.Abs(e.B - actual.B) <= tolerance,
            $"expected about {expected}, got #{actual.R:X2}{actual.G:X2}{actual.B:X2}");
    }

    private static UiColor? Parse(string? text) =>
        TabForge.Models.ColourHex.TryParse(text, out var c) ? new UiColor(c.A, c.R, c.G, c.B) : null;
}
