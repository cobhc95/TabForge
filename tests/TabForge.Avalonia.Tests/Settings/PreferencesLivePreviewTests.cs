using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks that the Settings window changes the live settings at once: the score, the timeline lane mode and the window theme change before OK,
/// Cancel (closing) restores them, OK keeps them in the store.
/// Does not own: the rows (PreferencesCatalog) or the bindings (ScoreSettingsBindingTests, ThemeSettingsBindingTests).
/// Tests: this class.
/// </summary>
public class PreferencesLivePreviewTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-prefs-live-" + Guid.NewGuid().ToString("N"));

    public PreferencesLivePreviewTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    [AvaloniaFact]
    public void ScoreAndThemeRowsChangeBeforeOkAndCancelRestoresThem()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = Shown(settings);
        var window = settings.CreatePreferencesWindow();
        window.Show(main);
        Assert.Equal(ThemeVariant.Dark, main.RequestedThemeVariant);

        window.Write("score.dynamics", false);
        Assert.False(settings.Settings.Appearance.ShowDynamics);
        Assert.False(ScoreOf(main).Probe.Appearance.ShowDynamics);

        // A second edit inside the 100 ms preview interval reaches the consumers on the trailing notice.
        window.Write("appearance.thememode", "Light");
        System.Threading.Thread.Sleep(150);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Light, main.RequestedThemeVariant);

        window.Close();
        Dispatcher.UIThread.RunJobs();   // the revert notice goes out after the window is gone
        Assert.True(settings.Settings.Appearance.ShowDynamics);
        Assert.True(ScoreOf(main).Probe.Appearance.ShowDynamics);
        Assert.Equal(ThemeVariant.Dark, main.RequestedThemeVariant);
        main.Close();
    }

    [AvaloniaFact]
    public void PreferencesWindowFollowsTheThemeWhenOpenedAfterAndDuringASwitch()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = Shown(settings);
        var app = global::Avalonia.Application.Current!;
        try
        {
            settings.Settings.Appearance.ThemeMode = "Light"; settings.SaveSettings();
            var window = settings.CreatePreferencesWindow();
            window.Show(main);
            var plain = new global::Avalonia.Controls.Window(); plain.Show(main);
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            Assert.Equal(ThemeVariant.Light, plain.ActualThemeVariant);

            settings.Settings.Appearance.ThemeMode = "Dark"; settings.SaveSettings();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.Equal(ThemeVariant.Dark, plain.ActualThemeVariant);
            window.Close();
        }
        finally { app.RequestedThemeVariant = null; main.Close(); }
    }

    [AvaloniaFact]
    public void OkKeepsThePreviewedValueInTheStore()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = Shown(settings);
        var window = settings.CreatePreferencesWindow();
        window.Show(main);
        window.Write("score.dynamics", false);
        window.Apply();
        window.Close();
        Assert.False(settings.Settings.Appearance.ShowDynamics);
        Assert.False(ScoreOf(main).Probe.Appearance.ShowDynamics);
        main.Close();
    }

    [AvaloniaFact]
    public void ALaneModeRowChangesTheTimeline()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = Shown(settings);
        var timeline = main.GetVisualDescendants().OfType<TimelineView>().First();
        Assert.False(timeline.ShowContinuousBlocks);
        var window = settings.CreatePreferencesWindow();
        window.Show(main);
        window.Write("timeline.continuousline", true);
        Assert.True(timeline.ShowContinuousBlocks);
        window.Close();
        Dispatcher.UIThread.RunJobs();   // the revert notice goes out after the window is gone
        Assert.False(timeline.ShowContinuousBlocks);
        main.Close();
    }

    [AvaloniaFact]
    public void HoldersOfASectionSeeThePreviewedValueAndCancelRestoresIt()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var routing = new TabForge.Avalonia.Transport.AvaloniaEngineRouting();
        routing.Attach(settings.Settings.Audio, settings.Settings.Plugins);
        var exports = new MainWindowFileExports(() => false, settings.Settings);
        routing.SetMasterPercent(40);
        var window = settings.CreatePreferencesWindow();
        var before = settings.Settings.Audio.MetronomeVolume;
        var limiter = exports.RenderSettings.SafetyLimiter;

        window.Write("audio.metrovolume", 20);
        window.Write("vst.renderlimiter", !limiter);
        Assert.Equal(20, settings.Settings.Audio.MetronomeVolume);
        Assert.Equal(!limiter, exports.RenderSettings.SafetyLimiter);
        Assert.Equal(40, routing.MasterPercent);

        window.Close();
        Assert.Equal(before, settings.Settings.Audio.MetronomeVolume);
        Assert.Equal(limiter, exports.RenderSettings.SafetyLimiter);
        Assert.Equal(40, routing.MasterPercent);
    }

    [AvaloniaFact]
    public void CopyIntoKeepsTheIdentityOfEverySection()
    {
        var live = new TabForge.Services.AppSettings();
        var sections = typeof(TabForge.Services.AppSettings).GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.PropertyType.IsClass && p.PropertyType != typeof(string) && p.Name != "Workspace" && p.GetValue(live) is not null
                && !typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)).ToList();
        Assert.NotEmpty(sections);
        var held = sections.ToDictionary(p => p.Name, p => p.GetValue(live));
        PreferencesCatalog.CopyInto(new TabForge.Services.AppSettings(), live, includeWorkspace: false);
        foreach (var section in sections) Assert.Same(held[section.Name], section.GetValue(live));
    }

    private static MainWindow Shown(MainWindowSettings settings)
    {
        var main = new MainWindow(settings) { Width = 1200, Height = 800 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    private static ScorePreviewView ScoreOf(MainWindow main) => main.GetVisualDescendants().OfType<ScorePreviewView>().First();
}
