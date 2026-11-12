using System.Linq;
using System.Text.Json;
using TabForge.Features.LeftStrip;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the left strip's saved-state defaults and start rules, that a saved value survives a settings round trip and wins over the default, and the icon catalog.
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
        Assert.True(loaded.ComposerOpenAtStart);   // the composer opens on its own, strip hidden or not
        Assert.True(loaded.BesideMenus);
    }

    [Fact]
    public void CustomisedStripSurvivesARoundTrip()
    {
        var settings = new AppSettings();
        settings.LeftStrip.Items = new() { "Edit.Undo", "Separator", "File.Open" };
        settings.LeftStrip.RightSide = true;
        settings.LeftStrip.Large = true;
        settings.LeftStrip.Tooltips = false;
        settings.LeftStrip.Labels = true;
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.LeftStrip;
        Assert.Equal(new[] { "Edit.Undo", "Separator", "File.Open" }, loaded.Items);
        Assert.True(loaded.RightSide && loaded.Large && loaded.Labels && !loaded.Tooltips);
        Assert.True(new LeftStripSettings().Tooltips);
        Assert.True(new LeftStripSettings().Large);
    }

    [global::Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheCatalogOffersAtLeast36DistinctIconsAndTheDefaultStrip()
    {
        var all = TabForge.Avalonia.Features.LeftStrip.LeftStripItems.All;
        Assert.True(all.Count >= 36);
        Assert.Equal(all.Count, all.Select(i => i.Id).Distinct().Count());
        Assert.All(all, i => global::Avalonia.Media.Geometry.Parse(i.Icon));
        Assert.Equal(new[] { "File.New", "File.Open", "File.Save", "File.SaveAs", "File.Render", "Separator", "Edit.Undo", "Edit.Redo", "Separator",
                "View.ToggleComposer", "Transport.FocusTrack", "Separator", "View.BandView", "View.CompactMode", "View.LearnMode" },
            TabForge.Avalonia.Features.LeftStrip.LeftStripItems.Resolve(null));
        Assert.Equal(new[] { "File.Open" }, TabForge.Avalonia.Features.LeftStrip.LeftStripItems.Resolve(new[] { "Gone.Command", "File.Open" }));
    }

    [Fact]
    public void ShowOnStartOffStartsHidden()
    {
        var strip = new LeftStripSettings { ShowOnStart = false };
        Assert.False(strip.ShownAtStart);
    }

    [Fact]
    public void ASavedStripIsOfferedANewIconOnceAndAnIconTheUserRemovedStaysRemoved()
    {
        var state = new TabForge.Features.LeftStrip.LeftStripSettings { Items = new() { "File.Open", "View.ToggleComposer", "Separator" } };
        Assert.True(state.OfferOnce("Transport.FocusTrack", "View.ToggleComposer"));
        Assert.Equal(new[] { "File.Open", "View.ToggleComposer", "Transport.FocusTrack", "Separator" }, state.Items);
        state.Items.Remove("Transport.FocusTrack");
        Assert.False(state.OfferOnce("Transport.FocusTrack", "View.ToggleComposer"));   // offered before: not put back
        Assert.DoesNotContain("Transport.FocusTrack", state.Items);
        var plain = new TabForge.Features.LeftStrip.LeftStripSettings();
        Assert.True(plain.OfferOnce("Transport.FocusTrack", "View.ToggleComposer"));
        Assert.Null(plain.Items);   // the default strip already has it
    }
}
