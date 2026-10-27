using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what windows share: a torn-off window uses the first window's settings store (its tab settings, its layout saving, the store
/// open until the last window closes), a closed window's dock layout timer is stopped, and the title bar and the hit-test hook get one chrome.
/// Tests: DocumentTabDragFlow.TearOff, MainWindowSettings.Attach, DockWorkspaceController.Dispose, WindowChromeFactory.
/// </summary>
public sealed class TornOffWindowSharingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "share-" + Guid.NewGuid().ToString("N"));

    public TornOffWindowSharingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [AvaloniaFact]
    public void TornOffWindowSharesTheStoreAndSavesItsLayoutUntilTheLastWindowCloses()
    {
        var path = Path.Combine(_folder, "settings.json");
        var settings = MainWindowSettings.Open(path);
        settings.Settings.Tabs.CloseOnDoubleClick = false;
        var source = new MainWindow(settings);
        source.Show();
        source.Songs.New();
        var torn = source.TabDrag.TearOff(1, new PixelPoint(300, 200));
        Assert.NotSame(source, torn);
        Assert.Same(settings, torn.Settings);
        Assert.Same(settings.Settings.Tabs, torn.TabStrip.Settings);
        Assert.False(torn.TabStrip.Settings.CloseOnDoubleClick);

        source.Close();
        settings.SaveLayout(DockWorkspaceRules.Capture(DockWorkspaceController.DefaultLayout()));   // the store is still open for the torn window
        torn.Close();   // the last window closes the store, which writes the pending save
        Assert.True(File.Exists(path));
    }

    [AvaloniaFact]
    public void ClosingTheWindowStopsItsDockLayoutTimer()
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var dock = new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup);
        var window = new Window();
        var tabs = new MainWindowTabs(new SongDocument(() => null), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        _ = new MainWindowLifetime(window, new MainWindowKeyboard(new HotkeySettings(), new MainWindowCommands(window, () => { })), tabs, dock);
        window.Show();
        dock.ClosePanel("lyrics");
        Assert.True(dock.IsLayoutTimerRunning);
        window.Close();
        Assert.False(dock.IsLayoutTimerRunning);
    }

    [AvaloniaFact]
    public void TheChromeOfAWindowIsOneInstance()
    {
        var window = new Window();
        Assert.Same(WindowChromeFactory.Create(window), WindowChromeFactory.Create(window));
        Assert.NotSame(WindowChromeFactory.Create(window), WindowChromeFactory.Create(new Window()));
    }
}
