using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Avalonia.Views.Dialogs;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a timeline zoom must not fan the settings out to every observer (that re-applied all settings on the UI thread and froze the playhead after the gesture).
/// Tests: MainWindowTimelineZoom.
/// </summary>
public class TimelineZoomSettingsQuietTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-zoom-quiet-" + Guid.NewGuid().ToString("N"));

    public TimelineZoomSettingsQuietTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }

    [AvaloniaFact]
    public void TimelineZoomSavesTheFactorWithoutNotifyingSettingsObservers()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var commands = new MainWindowCommands(new Window(), () => { });
        var panes = new MainWindowDocumentPanes(new MainWindowTabs(new SongDocument(() => null) { Project = DemoSongFactory.Create() }, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false)), new ScorePreviewView());
        MainWindowTimelineZoom.Register(commands, panes, settings);
        var notified = 0;
        settings.SettingsChanged += _ => notified++;
        var before = settings.Settings.Timeline.ZoomFactor;

        commands.Run(MainWindowTimelineZoom.ZoomInId);

        Assert.NotEqual(before, settings.Settings.Timeline.ZoomFactor);
        Assert.Equal(0, notified);
    }
}
