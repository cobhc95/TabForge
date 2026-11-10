using System.Text.Json;
using TabForge.Features.LeftStrip;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the left strip's saved-state defaults and start rules, and that a saved value survives a settings round trip and wins over the default.
// Does not own: the strip's controls (LeftStripFeature) or the settings file store.
// Tests: this class.
public sealed class LeftStripSettingsTests
{
    [Fact]
    public void FreshSettingsShowTheStripWithTheComposerClosed()
    {
        var strip = new AppSettings().LeftStrip;
        Assert.True(strip.ShownAtStart);
        Assert.False(strip.ComposerOpenAtStart);
        Assert.False(strip.BesideMenus);
        Assert.True(strip.ShowButton);
    }

    [Fact]
    public void SavedValuesWinOverTheDefaults()
    {
        var settings = new AppSettings();
        settings.LeftStrip.Visible = false;
        settings.LeftStrip.ComposerOpen = true;
        settings.LeftStrip.BesideMenus = true;
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.LeftStrip;
        Assert.False(loaded.ShownAtStart);
        Assert.False(loaded.ComposerOpenAtStart);   // the composer opens only beside a shown strip
        Assert.True(loaded.BesideMenus);
        loaded.Visible = true;
        Assert.True(loaded.ComposerOpenAtStart);
    }

    [Fact]
    public void ShowOnStartOffStartsHidden()
    {
        var strip = new LeftStripSettings { ShowOnStart = false };
        Assert.False(strip.ShownAtStart);
    }
}
