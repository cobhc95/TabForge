using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard note colours: the defaults are the theme colours the dots used before (so nothing changes until a colour is picked), a
///   picked default is stored as empty, the dot style falls back to Fill, an older settings file without the keys loads with the defaults, the
///   panel look carries a picked colour and the Ring style, and the Fretboard settings card offers the shared colour button for each role,
///   Reset colours and the dot style.
/// Does not own: the drawing of the dots (FretboardPlaybackMarkerTests) or the colour popup (ColourButtonTests).
/// Tests: FretNoteColours, FretboardSettingsBinding.Look, FretboardSettingsWindow's Note colours card.
/// </summary>
public class FretNoteColoursTests
{
    [Theory]
    [InlineData(FretNoteRole.Now, false, "InstrumentCurrent")]
    [InlineData(FretNoteRole.Now, true, "FretNow")]
    [InlineData(FretNoteRole.Next, false, "InstrumentNext")]
    [InlineData(FretNoteRole.Upcoming, true, "InstrumentAccent")]
    public void TheDefaultsAreTheThemeColoursTheDotsUsedBefore(FretNoteRole role, bool light, string key) =>
        Assert.Equal(Palette.Hex(key, light), FretNoteColours.DefaultHex(role, light), ignoreCase: true);

    [Fact]
    public void APickedDefaultOrNonColourIsStoredEmptyAndAnOwnColourAsHex()
    {
        Assert.Equal("", FretNoteColours.Stored(FretNoteRole.Next, FretNoteColours.DefaultHex(FretNoteRole.Next, light: true)));
        Assert.Equal("", FretNoteColours.Stored(FretNoteRole.Next, "Amber"));
        Assert.Equal("#FF0080", FretNoteColours.Stored(FretNoteRole.Next, "#ff0080"));
        var editing = new EditingSettings();
        FretNoteColours.Set(editing, FretNoteRole.Upcoming, "#123456");
        Assert.Equal("#123456", editing.FretUpcomingColour);
        Assert.Equal("#123456", FretNoteColours.Shown(FretNoteRole.Upcoming, editing.FretUpcomingColour, light: false));
        Assert.Equal(FretNoteColours.DefaultHex(FretNoteRole.Now, false), FretNoteColours.Shown(FretNoteRole.Now, editing.FretNowColour, light: false));
    }

    [Fact]
    public void AnOlderFileLoadsWithTheDefaultsAndAStrangeDotStyleBecomesFill()
    {
        var editing = JsonSerializer.Deserialize<EditingSettings>("{\"FretMarkerSizePercent\":90}")!;
        Assert.Equal(("", "", "", FretNoteColours.Fill), (editing.FretNowColour, editing.FretNextColour, editing.FretUpcomingColour, editing.FretDotStyle));
        var settings = new AppSettings();
        settings.Editing.FretDotStyle = "Square";
        settings.Editing.FretNowColour = "green";
        SettingsValidator.Normalize(settings);
        Assert.Equal(FretNoteColours.Fill, settings.Editing.FretDotStyle);
        Assert.Equal("", settings.Editing.FretNowColour);
    }

    [Fact]
    public void ThePanelLookCarriesAPickedColourAndTheRingStyle()
    {
        var settings = new AppSettings();
        Assert.False(FretboardSettingsBinding.Look.From(settings).HasNoteColours);
        settings.Editing.FretNextColour = "#102030";
        settings.Editing.FretDotStyle = FretNoteColours.Ring;
        var look = FretboardSettingsBinding.Look.From(settings);
        Assert.Equal(new TabForge.Platform.UiColor(255, 0x10, 0x20, 0x30), look.NextColour);
        Assert.Null(look.NowColour);
        Assert.True(look.RingDots);
    }

    [AvaloniaFact]
    public void TheCardOffersAColourButtonPerRoleResetColoursAndTheDotStyle()
    {
        var settings = new AppSettings();
        settings.Editing.FretNowColour = "#112233";
        var window = new FretboardSettingsWindow(settings, () => { }, () => { });
        window.Select(1);
        var buttons = window.GetLogicalDescendants().OfType<ColourButton>().ToList();
        Assert.Equal(new[] { "FretboardNoteColourNow", "FretboardNoteColourNext", "FretboardNoteColourUpcoming" }, buttons.Select(b => b.Name).ToArray());
        Assert.Equal("#112233", buttons[0].Value);
        Assert.Equal(global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.ColourSettingWidth, buttons[0].Width);
        Assert.Single(window.GetLogicalDescendants().OfType<Control>(), c => c.Name == "FretboardDotStyleRing");
        var reset = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "FretboardResetNoteColours");
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", settings.Editing.FretNowColour);
    }
}
