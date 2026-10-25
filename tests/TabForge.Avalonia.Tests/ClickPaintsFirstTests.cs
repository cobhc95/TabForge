using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the guards that a click paints its own state first: a Preferences checkbox does no settings write or broadcast before the next frame,
/// and a view toggle's save is deferred; the work still lands once the queue runs.
/// Does not own: what the settings do (PreferencesLivePreviewTests).
/// Tests: PreferencesWindow.Changed, MainWindowSettings.SaveSettingsDeferred.
/// </summary>
public class ClickPaintsFirstTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-click-first-" + Guid.NewGuid().ToString("N"));

    public ClickPaintsFirstTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    [AvaloniaFact]
    public void PreferencesCheckboxDoesNoBroadcastOrLiveWriteBeforeTheFrame()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var main = new MainWindow(settings) { Width = 1200, Height = 800 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var window = settings.CreatePreferencesWindow();
        window.Show(main);
        window.ShowPage(TabForge.Services.SettingsCatalog.Score);
        Dispatcher.UIThread.RunJobs();
        var broadcasts = 0;
        settings.SettingsChanged += _ => broadcasts++;
        var was = settings.Settings.Appearance.ShowDynamics;
        var box = window.EditorOf("score.dynamics") as CheckBox ?? window.GetVisualDescendants().OfType<CheckBox>().First();
        box.IsChecked = !box.IsChecked;
        Assert.Equal(0, broadcasts);
        Assert.Equal(was, settings.Settings.Appearance.ShowDynamics);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);   // everything up to the paint: the write waits for the Background pass after it
        Assert.Equal(was, settings.Settings.Appearance.ShowDynamics);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(!was, settings.Settings.Appearance.ShowDynamics);
        window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void FiftyRapidTicksPreviewOncePerFrame()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var window = settings.CreatePreferencesWindow();
        window.Show();
        window.ShowPage(TabForge.Services.SettingsCatalog.Score);
        Dispatcher.UIThread.RunJobs();
        var previews = 0;
        window.Previewed += () => previews++;
        var was = settings.Settings.Appearance.ShowDynamics;
        var box = (CheckBox)window.EditorOf("score.dynamics");
        for (var i = 0; i < 50; i++) box.IsChecked = !box.IsChecked;
        Assert.Equal(0, previews);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, previews);
        Assert.Equal(was, settings.Settings.Appearance.ShowDynamics);   // an even number of ticks lands where it began
        window.Close();
    }

    [AvaloniaFact]
    public void CancelRightAfterATickRestoresAndLaterFrameWritesNothing()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var window = settings.CreatePreferencesWindow();
        window.Show();
        window.ShowPage(TabForge.Services.SettingsCatalog.Score);
        Dispatcher.UIThread.RunJobs();
        var was = settings.Settings.Appearance.ShowDynamics;
        var box = (CheckBox)window.EditorOf("score.dynamics");
        box.IsChecked = !box.IsChecked;
        var broadcasts = 0;
        settings.SettingsChanged += _ => broadcasts++;
        window.Close();   // Cancel before the frame that would have written the tick
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(was, settings.Settings.Appearance.ShowDynamics);
        Assert.Equal(1, broadcasts);   // the restore only: the queued tick wrote nothing after it
    }

    [AvaloniaFact]
    public void DeferredSaveWritesNothingUntilTheQueueRuns()
    {
        var path = Path.Combine(_folder, "settings.json");
        using var settings = MainWindowSettings.Open(path);
        var broadcasts = 0;
        settings.SettingsChanged += _ => broadcasts++;
        settings.SaveSettingsDeferred();
        settings.SaveSettingsDeferred();
        Assert.Equal(0, broadcasts);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, broadcasts);
    }
}
