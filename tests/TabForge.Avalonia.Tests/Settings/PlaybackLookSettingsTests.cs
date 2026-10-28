using System;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Playback look card on the Appearance page: the Follow accent / Custom playback highlight and its migration from Auto, the merge of Playing note colour into it,
/// the subtle on-by-default playing bar and its migration, the opacity mapping, and that the moved rows are found by search under their old names.
/// Does not own: the drawing of the highlight (ScoreRenderer) or the other Preferences colour rows (PreferencesColourRowsTests).
/// Tests: this class.
/// </summary>
public class PlaybackLookSettingsTests
{
    private static AppSettings Load(string json) => SettingsMigration.Normalize(json, JsonSerializer.Deserialize<AppSettings>(json)!);

    private static readonly string[] MovedKeys =
    {
        "follow.highlight", "follow.playingbar", "follow.playingbar.colour", "follow.playingbar.opacity", "follow.playingbar.stopped", "follow.bg",
        "follow.duration.enabled", "follow.durationglow", "follow.durationopacity", "follow.playhead.thickness",
    };

    [Fact]
    public void ThePlaybackHighlightOffersFollowAccentAndCustomAndDefaultsToCustom()
    {
        var row = SettingsCatalog.Build(new AppSettings()).Single(r => r.Key == "appearance.playbackhighlight");
        Assert.Equal(new[] { "Follow accent", "Custom" }, row.Choices);
        Assert.Equal("Custom", row.Get());
        Assert.DoesNotContain("Auto", row.Description);
    }

    [Fact]
    public void AStoredAutoBecomesFollowAccentAndStaysThereOnTheNextLoad()
    {
        var settings = Load("""{"Appearance":{"PlaybackHighlight":"Auto"}}""");
        Assert.Equal(PlaybackHighlightModes.FollowAccent, settings.Appearance.PlaybackHighlight);
        var saved = JsonSerializer.Serialize(settings);
        Assert.Equal(PlaybackHighlightModes.FollowAccent, Load(saved).Appearance.PlaybackHighlight);
    }

    [Fact]
    public void AnAutoWithAChangedPlayingNoteColourMovesThatColourIntoCustom()
    {
        var settings = Load("""{"Appearance":{"PlaybackHighlight":"Auto"},"Follow":{"HighlightColour":"#112233"}}""");
        Assert.Equal(PlaybackHighlightModes.Custom, settings.Appearance.PlaybackHighlight);
        Assert.Equal("#112233", settings.Appearance.PlaybackHighlightColour, ignoreCase: true);
        Assert.Equal(new FollowSettings().HighlightColour, settings.Follow.HighlightColour);
        var again = Load(JsonSerializer.Serialize(settings));
        Assert.Equal(PlaybackHighlightModes.Custom, again.Appearance.PlaybackHighlight);
        Assert.Equal("#112233", again.Appearance.PlaybackHighlightColour, ignoreCase: true);
    }

    [Fact]
    public void ACustomHighlightKeepsItsColourAndDropsTheOldPlayingNoteColour()
    {
        var settings = Load("""{"Appearance":{"PlaybackHighlight":"Custom","PlaybackHighlightColour":"#445566"},"Follow":{"HighlightColour":"#112233"}}""");
        Assert.Equal(PlaybackHighlightModes.Custom, settings.Appearance.PlaybackHighlight);
        Assert.Equal("#445566", settings.Appearance.PlaybackHighlightColour, ignoreCase: true);
        Assert.Equal(new FollowSettings().HighlightColour, settings.Follow.HighlightColour);
    }

    [Fact]
    public void PlayingNoteColourIsNoLongerARowButSearchStillFindsThePlaybackHighlightColour()
    {
        var rows = SettingsCatalog.Build(new AppSettings());
        Assert.DoesNotContain(rows, r => r.Key == "follow.colour");
        var entries = SettingsCatalog.Registry(rows, new HotkeySettings());
        var found = entries.Where(e => SettingsCatalog.Matches(e, "playing note colour")).ToList();
        Assert.Contains(found, e => e.Id == "appearance.playbackhighlightcolour");
        Assert.All(found.Where(e => e.Row is not null), e => Assert.Equal(SettingsCatalog.Appearance, e.Page));
    }

    [Fact]
    public void EveryMovedRowIsOnTheAppearancePlaybackLookCardAndFoundByItsOldPlaceAndName()
    {
        var rows = SettingsCatalog.Build(new AppSettings());
        var entries = SettingsCatalog.Registry(rows, new HotkeySettings());
        foreach (var key in MovedKeys)
        {
            var row = rows.Single(r => r.Key == key);
            Assert.Equal((SettingsCatalog.Appearance, "Playback look"), (row.Category, row.Group));
            var entry = entries.Single(e => e.Id == key);
            Assert.True(SettingsCatalog.Matches(entry, "playback practice highlighting"), key + " is not found by its old place");
            Assert.True(SettingsCatalog.Matches(entry, row.Title.ToLowerInvariant()), key + " is not found by its title");
        }
        Assert.True(SettingsCatalog.Matches(entries.Single(e => e.Id == "follow.playingbar.opacity"), "playing bar strength"));
        // Four rows always shown, the six extras behind More options.
        Assert.Equal(new[] { "follow.highlight", "follow.playingbar", "follow.playingbar.colour", "follow.playingbar.opacity" }, rows.Where(r => r.Group == "Playback look" && !r.More).OrderBy(r => r.Order).Select(r => r.Key));
        Assert.Equal(6, rows.Count(r => r.Group == "Playback look" && r.More));
    }

    [AvaloniaFact]
    public void TheAppearancePageShowsThePlaybackLookCardAndThePlaybackPageDoesNot()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.ShowPage(SettingsCatalog.Appearance);
        Assert.Contains(window.ShownRows, r => r.Key == "follow.playingbar.opacity");
        Assert.Contains(window.ShownRows, r => r.Key == "appearance.playbackhighlight");
        window.ShowPage(SettingsCatalog.Playback);
        Assert.DoesNotContain(window.ShownRows, r => MovedKeys.Contains(r.Key));
    }

    [Fact]
    public void ThePlayingBarIsOnByDefaultWithASubtleFivePercentOpacity()
    {
        var follow = new AppSettings().Follow;
        Assert.True(follow.PlayingBarEnabled);
        Assert.Equal(0.05, follow.PlayingBarOpacity);
        var rows = SettingsCatalog.Build(new AppSettings());
        var opacity = rows.Single(r => r.Key == "follow.playingbar.opacity");
        Assert.Equal("Playing bar opacity", opacity.Title);
        Assert.Equal(5.0, Convert.ToDouble(opacity.Get()), 6);
        Assert.Equal((0.0, 100.0), (opacity.Min, opacity.Max));
        Assert.DoesNotContain("Off by default", rows.Single(r => r.Key == "follow.playingbar").Description);
        var order = rows.Where(r => r.Group == "Playback look").OrderBy(r => r.Order).Select(r => r.Key).ToList();
        Assert.Equal(order.IndexOf("follow.playingbar.colour") + 1, order.IndexOf("follow.playingbar.opacity"));   // right under the colour
    }

    [Fact]
    public void ThePercentSetsTheOpacityLinearlyFromZeroToOneHundred()
    {
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "follow.playingbar.opacity");
        foreach (var percent in new[] { 0.0, 10, 25, 50, 100 })
        {
            row.Set(percent);
            Assert.Equal(percent / 100, settings.Follow.PlayingBarOpacity, 9);
        }
        row.Set(250.0);
        Assert.Equal(1.0, settings.Follow.PlayingBarOpacity);
        settings.Follow.PlayingBarOpacity = 0;
        Assert.Equal(0.0, SettingsValidator.Normalize(settings).Follow.PlayingBarOpacity);   // zero is kept: the band is invisible
    }

    [Fact]
    public void AnOlderFileWithTheOldPlayingBarDefaultsMovesToOnAndFivePercentOnce()
    {
        var settings = Load("""{"Follow":{"PlayingBarEnabled":false,"PlayingBarOpacity":0.2}}""");
        Assert.True(settings.Follow.PlayingBarEnabled);
        Assert.Equal(0.05, settings.Follow.PlayingBarOpacity, 9);
        Assert.Equal(2, settings.Follow.PlayingBarVersion);
        var tenPercent = Load("""{"Follow":{"PlayingBarVersion":1,"PlayingBarOpacity":0.1}}""");
        Assert.Equal(0.05, tenPercent.Follow.PlayingBarOpacity, 9);   // the 10% default of version 1 moves to 5%
        var custom = Load("""{"Follow":{"PlayingBarVersion":1,"PlayingBarOpacity":0.3}}""");
        Assert.Equal(0.3, custom.Follow.PlayingBarOpacity, 9);       // a value the user set stays
    }

    [Fact]
    public void AnOlderFileKeepsACustomisedPlayingBarOpacityAndAfterTheMoveAnOffChoiceIsKept()
    {
        var custom = Load("""{"Follow":{"PlayingBarEnabled":true,"PlayingBarOpacity":0.35}}""");
        Assert.True(custom.Follow.PlayingBarEnabled);
        Assert.Equal(0.35, custom.Follow.PlayingBarOpacity, 9);
        var moved = Load("""{"Follow":{"PlayingBarVersion":1,"PlayingBarEnabled":false,"PlayingBarOpacity":0.2}}""");
        Assert.False(moved.Follow.PlayingBarEnabled);
        Assert.Equal(0.2, moved.Follow.PlayingBarOpacity, 9);
        var saved = JsonSerializer.Serialize(moved);
        Assert.False(Load(saved).Follow.PlayingBarEnabled);   // once moved and saved, the next load changes nothing
    }

    [AvaloniaFact]
    public void TheBandAlphaIsTheOpacityTimes255()
    {
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(2, 0.5));
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var session = sessions.For(document)!;
        var view = new ScorePreviewView();
        view.BindPlayback(session);
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        session.PlayOrPause();
        session.PumpFrame();
        var probe = view.Probe;
        var track = probe.Track!;
        var position = probe.Layout.GetLayout(track).Measure(2);
        probe.Appearance.PlayingBarEnabled = true;
        foreach (var opacity in new[] { 0.0, 0.1, 0.5, 1.0 })
        {
            probe.Appearance.PlayingBarOpacity = opacity;
            Assert.Equal((byte)Math.Round(255 * opacity), probe.PlayingBarBand(track, null!, position)!.Value.Colour.A);
        }
        probe.Appearance.PlayingBarEnabled = false;
        Assert.Null(probe.PlayingBarBand(track, null!, position));
        session.Stop();
        window.Close();
    }
}
