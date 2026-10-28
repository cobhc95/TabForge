using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll playback needle: it runs from the ruler marker at the marker own x (stopped too, at the stop position), is not drawn when
//   the setting is off, the right-click item and the Preferences row are the one setting (default on, saved in the settings file), and Preferences
//   search finds the row by "needle", "playback" and "playhead".
// Does not own: the marker, the follow scroll or the live keys (PianoRollGestureTests, PianoRollKeyStateTests).
// Tests: PianoRollPlayheadLayer, PianoRollMenus, SettingsCatalog.
public sealed class PianoRollNeedleTests
{
    private const string Key = "editing.pianoroll.needle";

    private sealed class Rig
    {
        public PianoRollView View = null!;
        public Window Window = null!;
        public AvaloniaPlaybackSession Session = null!;
        public AvaloniaPlaybackSessions Sessions = null!;
        public EditingSettings Editing = new();
        public AppearanceSettings Appearance = new();
        public int Saved;
    }

    private static Rig Open()
    {
        var song = PianoRollSongs.Midi();
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var rig = new Rig { Sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine) };
        rig.View = new PianoRollView(new PianoRollHost { Document = () => song, Session = () => rig.Sessions.For(song), Editing = () => rig.Editing, Appearance = () => rig.Appearance, SaveSettings = () => rig.Saved++ });
        rig.Window = new Window { Width = 900, Height = 600, Content = rig.View };
        rig.Window.Show();
        rig.View.Show(song, 0, newVisit: true);
        rig.Window.UpdateLayout();
        rig.Session = rig.Sessions.For(song)!;
        return rig;
    }

    private static void Close(Rig rig)
    {
        rig.Window.Close();
        rig.Sessions.Dispose();
    }

    [AvaloniaFact]
    public void TheNeedleSitsAtTheMarkerXForThePlaybackPositionAlsoWhenStoppedAndFollowsASeek()
    {
        var rig = Open();
        var layer = rig.View.Surface.Playhead;
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.False(double.IsNaN(layer.MarkerX));
        Assert.Equal(layer.MarkerX, layer.DrawnX);   // stopped: the needle stays at the stop position

        rig.Session.Seek(1, 0);   // bar 2 starts at beat 4
        layer.InvalidateVisual();
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.Equal(rig.View.Surface.XOf(4), layer.MarkerX, 6);
        Assert.Equal(layer.MarkerX, layer.DrawnX);
        Close(rig);
    }

    [AvaloniaFact]
    public void TheNeedleAndTheMarkerFollowThePlaybackHighlight()
    {
        var rig = Open();
        rig.Appearance.PlaybackHighlight = PlaybackHighlightModes.FollowAccent;
        rig.Appearance.Accent = "#0A0B0C";
        var layer = rig.View.Surface.Playhead;
        rig.View.RefreshNeedle();
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.Equal(new UiColor(0xFF, 0x0A, 0x0B, 0x0C), layer.DrawnColour);   // Follow accent (chosen)

        rig.Appearance.Accent = "#102030";   // the accent changed: the next refresh shows it at once
        rig.View.RefreshNeedle();
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.Equal(new UiColor(0xFF, 0x10, 0x20, 0x30), layer.DrawnColour);

        rig.Appearance.PlaybackHighlight = PlaybackHighlightModes.Custom;
        rig.Appearance.PlaybackHighlightColour = "#AA5500";
        rig.View.RefreshNeedle();
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.Equal(new UiColor(0xFF, 0xAA, 0x55, 0x00), layer.DrawnColour);
        Assert.Equal(layer.PlaybackColour, layer.DrawnColour);
        Close(rig);
    }

    [AvaloniaFact]
    public void WithTheSettingOffNoNeedleIsDrawnButTheMarkerStays()
    {
        var rig = Open();
        rig.Editing.PianoRollPlaybackNeedle = false;
        rig.View.RefreshNeedle();
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.True(double.IsNaN(rig.View.Surface.Playhead.DrawnX));
        Assert.False(double.IsNaN(rig.View.Surface.Playhead.MarkerX));
        Close(rig);
    }

    [AvaloniaFact]
    public void TheRightClickItemShowsAndTogglesTheSettingAndSavesIt()
    {
        var rig = Open();
        var item = Item(rig.View);
        Assert.Equal("Show playback needle", item.Header);
        Assert.True(item.Checkable);
        Assert.True(item.Checked);
        Assert.Equal(Key, item.SettingKey);

        rig.View.Run(PianoRollMenus.PlaybackNeedle);
        Assert.False(rig.Editing.PianoRollPlaybackNeedle);
        Assert.False(Item(rig.View).Checked);
        Assert.Equal(1, rig.Saved);
        using (rig.Window.CaptureRenderedFrame()) { }
        Assert.True(double.IsNaN(rig.View.Surface.Playhead.DrawnX));

        rig.View.Run(PianoRollMenus.PlaybackNeedle);
        Assert.True(rig.Editing.PianoRollPlaybackNeedle);
        Close(rig);
    }

    private static MenuSpec Item(PianoRollView view) => view.MenuRows().Single(r => r.Id == PianoRollMenus.PlaybackNeedle);

    [Fact]
    public void TheSettingIsOnByDefaultIsSavedInTheSettingsFileAndIsOneCatalogueRowForTheMenu()
    {
        Assert.True(new EditingSettings().PianoRollPlaybackNeedle);
        var json = JsonSerializer.Serialize(new EditingSettings { PianoRollPlaybackNeedle = false });
        Assert.False(JsonSerializer.Deserialize<EditingSettings>(json)!.PianoRollPlaybackNeedle);
        Assert.True(JsonSerializer.Deserialize<EditingSettings>("{}")!.PianoRollPlaybackNeedle);   // an older file keeps the default

        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == Key);
        Assert.Equal(SettingKind.Bool, row.Kind);
        Assert.Equal("Editing", row.Category);
        Assert.Equal("Piano roll", row.Group);
        row.Set(false);
        Assert.False(settings.Editing.PianoRollPlaybackNeedle);   // the row and the menu item are the same stored value
    }

    [Theory]
    [InlineData("needle")]
    [InlineData("playback")]
    [InlineData("playhead")]
    [InlineData("playback needle")]
    public void PreferencesSearchFindsThePlaybackNeedle(string query)
    {
        var entries = SettingsCatalog.Registry(SettingsCatalog.Build(new AppSettings()), new HotkeySettings());
        Assert.Contains(entries, entry => entry.Id == Key && SettingsCatalog.Matches(entry, query));
    }
}
