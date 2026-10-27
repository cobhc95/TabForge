using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: every colour row of the Preferences catalogue can be used at once (never greyed out) and takes a pick; a Playback highlight colour pick switches the mode to Custom
/// and a Custom palette pick switches the theme to Custom, while Follow accent / Dark afterwards keep the stored colour.
/// Tests: this class.
/// </summary>
public class PreferencesColourRowsTests
{
    private static MainWindowSettings Open() => MainWindowSettings.Open(Path.Combine(Path.GetTempPath(), "tf-prefs-col-" + Guid.NewGuid().ToString("N") + ".json"));

    [AvaloniaFact]
    public void EveryColourRowOpensAndTakesAPickOnceItsParentAllows()
    {
        using var settings = Open();
        var rows = PreferencesCatalog.Build(new AppSettings());
        var colours = rows.Where(r => r.Kind == SettingKind.Colour).ToList();
        Assert.NotEmpty(colours);
        var window = settings.CreatePreferencesWindow();
        foreach (var colour in colours)
        {
            for (var row = colour; row.DependsOn is { } parentKey; row = rows.Single(r => r.Key == parentKey))   // open each parent: a switch on, a choice at the value the row needs
                window.Write(parentKey, rows.Single(r => r.Key == parentKey).Kind == SettingKind.Bool ? true : row.DependsOnValue!.Split('|')[0]);
            var live = PreferencesCatalog.Build(settings.Settings);
            Assert.True(PreferencesPagePresenter.IsEnabled(live.Single(r => r.Key == colour.Key), key => live.FirstOrDefault(r => r.Key == key)), colour.Key + " is greyed out");
            window.Write(colour.Key, "#123456");
            Assert.Equal("#123456", PreferencesCatalog.Build(settings.Settings).Single(r => r.Key == colour.Key).Get()?.ToString(), ignoreCase: true);
        }
    }

    [AvaloniaFact]
    public void PlaybackHighlightColourIsDisabledUntilCustomAndKeepsItsColour()
    {
        using var settings = Open();
        var window = settings.CreatePreferencesWindow();
        bool Enabled() { var r = PreferencesCatalog.Build(settings.Settings); return PreferencesPagePresenter.IsEnabled(r.Single(x => x.Key == "appearance.playbackhighlightcolour"), k => r.FirstOrDefault(x => x.Key == k)); }
        Assert.False(Enabled());
        window.Write("appearance.playbackhighlight", PlaybackHighlightModes.Custom);
        Assert.True(Enabled());
        window.Write("appearance.playbackhighlightcolour", "#00FF00");
        window.Write("appearance.playbackhighlight", PlaybackHighlightModes.FollowAccent);
        Assert.False(Enabled());
        Assert.Equal("#00FF00", settings.Settings.Appearance.PlaybackHighlightColour, ignoreCase: true);
    }

    [AvaloniaFact]
    public void CustomPaletteRowsAreDisabledUnlessThemeIsCustomAndKeepColoursWhenLeaving()
    {
        using var settings = Open();
        var window = settings.CreatePreferencesWindow();
        bool Enabled() { var r = PreferencesCatalog.Build(settings.Settings); return PreferencesPagePresenter.IsEnabled(r.Single(x => x.Key == "appearance.panel"), k => r.FirstOrDefault(x => x.Key == k)); }
        window.Write("appearance.thememode", "Dark");
        Assert.False(Enabled());
        window.Write("appearance.thememode", "Custom");
        Assert.True(Enabled());
        window.Write("appearance.panel", "#223344");
        window.Write("appearance.thememode", "Dark");
        Assert.Equal("#223344", settings.Settings.Appearance.Panel, ignoreCase: true);
    }
}
