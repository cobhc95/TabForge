using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the settings file shared by the two apps: a settings.json written by the writer loads in the Avalonia window with the same
/// dock layout and saves back byte for byte, and the reverse. Every run uses its own temp folder; no user settings are read or written.
/// Tests: MainWindowSettings, AppSettingsStore, SettingsFileService.
/// </summary>
public sealed class MainWindowSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "settings-" + Guid.NewGuid().ToString("N"));

    public MainWindowSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string Json(DockWorkspaceState? state) => JsonSerializer.Serialize(state);

    // Generated host ids (Guid) differ per default layout built, so they are blanked when two separately built layouts are compared.
    private static string Unnamed(DockWorkspaceState state) => Regex.Replace(Json(state), "\"HostId\":\"[0-9a-f]{32}\"", "\"HostId\":\"generated\"");

    private static DockWorkspaceState EditedLayout()
    {
        var state = DockWorkspaceController.DefaultLayout();
        DockWorkspaceRules.Close(state, "lyrics");
        return DockWorkspaceRules.Capture(state);
    }

    private static DockWorkspaceState RestoredByController(DockWorkspaceState saved)
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        return DockWorkspaceRules.Capture(new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup, saved).State);
    }

    /// <summary>What the app writes: its writer, after its own load (the settings it saves were always loaded and bounded).</summary>
    private static byte[] WrittenByWpf(string path, DockWorkspaceState layout)
    {
        SettingsFileService.SaveAtomic(path, new AppSettings { Workspace = layout });
        SettingsFileService.SaveAtomic(path, SettingsFileService.Load(path));
        return File.ReadAllBytes(path);
    }

    [AvaloniaFact]
    public void ClosingTheWindowRightAfterADeferredSaveKeepsTheChange()
    {
        var path = Path.Combine(_folder, "settings.json");
        var settings = MainWindowSettings.Open(path);
        var main = new MainWindow(settings) { Width = 1200, Height = 800 };
        main.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var was = settings.Settings.Appearance.ShowDynamics;
        settings.Settings.Appearance.ShowDynamics = !was;
        settings.SaveSettingsDeferred();
        main.Close();   // before the Background pass that would have saved it
        Assert.Equal(!was, SettingsFileService.Load(path).Appearance.ShowDynamics);
    }

    [AvaloniaFact]
    public void CancelAfterASaveDuringTheSessionSavesTheRestoredValues()
    {
        var path = Path.Combine(_folder, "settings.json");
        var settings = MainWindowSettings.Open(path);
        var window = settings.CreatePreferencesWindow();
        window.Show();
        var was = settings.Settings.Appearance.ShowDynamics;
        window.Write("score.dynamics", !was);
        settings.SaveWindowPlacement();   // a resize or deactivate save while the preview is unapplied
        System.Threading.Thread.Sleep(700);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(!was, SettingsFileService.Load(path).Appearance.ShowDynamics);   // the hazard: the preview reached the disk
        window.Close();
        settings.Dispose();
        Assert.Equal(was, SettingsFileService.Load(path).Appearance.ShowDynamics);
    }

    [AvaloniaFact]
    public void OkClosesTheWindowBeforeTheOneBroadcast()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var window = settings.CreatePreferencesWindow();
        window.Show();
        window.Write("score.dynamics", !settings.Settings.Appearance.ShowDynamics);
        var broadcasts = 0;
        var openAtBroadcast = true;
        settings.SettingsChanged += _ => { broadcasts++; openAtBroadcast &= window.IsVisible; };
        window.RunFooterAction(TabForge.Services.SettingsCatalog.ActionOk);
        Assert.False(window.IsVisible);
        Assert.Equal(0, broadcasts);   // nothing ran in the click's frame
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, broadcasts);
        Assert.False(openAtBroadcast);
        settings.Dispose();
    }

    [AvaloniaFact]
    public void CancelAfterEditsClosesFirstThenRestoresWithOneBroadcast()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var window = settings.CreatePreferencesWindow();
        window.Show();
        var was = settings.Settings.Appearance.ShowDynamics;
        window.Write("score.dynamics", !was);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var broadcasts = 0;
        settings.SettingsChanged += _ => broadcasts++;
        window.Close();
        Assert.Equal(was, settings.Settings.Appearance.ShowDynamics);
        Assert.Equal(0, broadcasts);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, broadcasts);
        settings.Dispose();
    }

    [AvaloniaFact]
    public void ApplyKeepsTheWindowOpenWithOneBroadcast()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var window = settings.CreatePreferencesWindow();
        window.Show();
        window.Write("score.dynamics", !settings.Settings.Appearance.ShowDynamics);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var broadcasts = 0;
        settings.SettingsChanged += _ => broadcasts++;
        window.Apply();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);
        Assert.Equal(1, broadcasts);
        window.Close();
        settings.Dispose();
    }

    [AvaloniaFact]
    public void SettingsWindowOverTheStoreEditsTheLiveSettings()
    {
        var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var window = settings.CreatePreferencesWindow();
        Assert.Same(settings.Settings, window.Saved);
        settings.Dispose();
    }

    [AvaloniaFact]
    public void WpfSettingsLoadInAvaloniaAndSaveBackIdentically()
    {
        var path = Path.Combine(_folder, "settings.json");
        var wpfBytes = WrittenByWpf(path, EditedLayout());
        var wpfLayout = SettingsFileService.Load(path).Workspace;

        var settings = MainWindowSettings.Open(path);
        Assert.Equal(Json(wpfLayout), Json(settings.StartLayout));
        var restored = RestoredByController(settings.StartLayout);
        Assert.Equal(Json(wpfLayout), Json(restored));
        settings.SaveLayout(restored);
        settings.Dispose();
        Assert.Equal(wpfBytes, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public void AvaloniaSettingsLoadInWpfAndSaveBackIdentically()
    {
        var path = Path.Combine(_folder, "settings.json");
        var settings = MainWindowSettings.Open(path);
        Assert.Equal(Unnamed(DockWorkspaceController.DefaultLayout()), Unnamed(settings.StartLayout));   // no file yet: the default layout
        var layout = RestoredByController(EditedLayout());
        settings.SaveLayout(layout);
        settings.Dispose();
        var avaloniaBytes = File.ReadAllBytes(path);

        var wpf = SettingsFileService.Load(path);
        Assert.Equal(Json(layout), Json(wpf.Workspace));
        var again = Path.Combine(_folder, "again.json");
        SettingsFileService.SaveAtomic(again, wpf);
        Assert.Equal(avaloniaBytes, File.ReadAllBytes(again));
    }

    [AvaloniaFact]
    public void UnreadableSettingsAreNeverOverwritten()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");
        var settings = MainWindowSettings.Open(path);
        settings.SaveLayout(EditedLayout());
        settings.Dispose();
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void MalformedSettingsFallBackToDefaultsWithNotice()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");
        var settings = MainWindowSettings.Open(path);
        Assert.True(settings.LoadFailed);
        Assert.NotNull(settings.Settings);
        var window = new MainWindow(settings);
        window.Show();
        // N1: the fallback notice appears on the single top bar (the first visible notice); bottom rows stay hidden tombstones.
        var notice = window.GetLogicalDescendants().OfType<StatusNoticeBarView>().First(v => v.IsVisible);
        Assert.True(notice.IsVisible);
        var text = notice.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Name == "NoticeText").Text ?? "";
        Assert.Equal(MainWindow.SettingsLoadFailedNotice, text);
        window.Close();
        settings.Dispose();
        Assert.Equal("{ not json", File.ReadAllText(path));
    }
}
