using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: one window per kind and key: a second open of the live FX chain, a track's FX chain, the mixer of a song and Settings brings the open window forward
/// (restored when minimised), and a closed window is made fresh on the next open.
/// Does not own: the windows' contents or the commands that reach the entry points.
/// Tests: this class.
/// </summary>
public class ToolWindowsTests
{
    private static (SongDocument Document, FxChainWindows Windows, Window Owner) Registry()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        document.Project.Tracks.Add(new TrackModel { Name = "Second" });
        document.MarkClean();
        var owner = new Window();
        owner.Show();
        return (document, new FxChainWindows(owner, () => document, null, null, new TabForge.Plugins.PluginTrust()), owner);
    }

    private static FxChainWindow LiveWindow()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain(), "Live input");
        var host = new StandaloneFxChainHost(new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { track }, _ => { })
        {
            AppWideChainTitle = FxChainPresenter.WindowTitle("Live input"),
        };
        return new FxChainWindow(host, track);
    }

    [AvaloniaFact]
    public void TheLiveFxWindowOpensOnceAndAMinimisedOneIsRestored()
    {
        var (_, windows, owner) = Registry();
        var made = 0;
        var first = windows.OpenLive(() => { made++; return LiveWindow(); }, owner);
        first.WindowState = WindowState.Minimized;
        var second = windows.OpenLive(() => { made++; return LiveWindow(); }, owner);
        Assert.Same(first, second);
        Assert.Equal(1, made);
        Assert.Equal(WindowState.Normal, first.WindowState);
        Assert.Single(owner.OwnedWindows.OfType<FxChainWindow>());
        first.Close();
        var third = windows.OpenLive(() => { made++; return LiveWindow(); }, owner);
        Assert.NotSame(first, third);
        Assert.Equal(2, made);
        third.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void TheFxWindowIsOnePerTrack()
    {
        var (document, windows, owner) = Registry();
        var a = windows.Open(document.Project.Tracks[0]);
        var b = windows.Open(document.Project.Tracks[1]);
        Assert.NotSame(a, b);
        Assert.Same(a, windows.Open(document.Project.Tracks[0]));
        Assert.Same(b, windows.Open(document.Project.Tracks[1]));
        Assert.Equal(2, owner.OwnedWindows.OfType<FxChainWindow>().Count());
        a.Close();
        Assert.NotSame(a, windows.Open(document.Project.Tracks[0]));
        owner.OwnedWindows.OfType<FxChainWindow>().ToList().ForEach(w => w.Close());
        owner.Close();
    }

    [AvaloniaFact]
    public void AnyToolWindowKindAndKeyOpensOnceAndReopensFreshAfterClosing()
    {
        var tools = new ToolWindows();
        var owner = new Window();
        owner.Show();
        var first = tools.OpenOrActivate("Mixer", "a", () => new Window(), owner);
        Assert.Same(first, tools.OpenOrActivate("Mixer", "a", () => new Window(), owner));
        var other = tools.OpenOrActivate("Mixer", "b", () => new Window(), owner);
        Assert.NotSame(first, other);
        first.Close();
        Assert.Null(tools.Find("Mixer", "a"));
        Assert.NotSame(first, tools.OpenOrActivate("Mixer", "a", () => new Window(), owner));
        owner.Close();
    }

    [AvaloniaFact]
    public void SettingsOpensOnceFromEveryEntryPoint()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-toolwindows-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
        var owner = new Window();
        owner.Show();
        MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.AudioVst);
        MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.Timeline);
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowOptionsMenu.Register(commands, owner, settings);
        commands.Run("Options.Stylesheet");
        Assert.Single(owner.OwnedWindows.OfType<PreferencesWindow>());
        var window = owner.OwnedWindows.OfType<PreferencesWindow>().Single();
        Assert.Equal(SettingsCatalog.Timeline, window.Page);
        window.Close();
        MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.AudioVst);
        Assert.NotSame(window, owner.OwnedWindows.OfType<PreferencesWindow>().Single());
        owner.OwnedWindows.OfType<PreferencesWindow>().ToList().ForEach(w => w.Close());
        owner.Close();
    }

    [AvaloniaFact]
    public void TheMixerOpensOncePerSongFromEveryMixerCommand()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var owner = new Window();
        owner.Show();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowMixer.Register(commands, tabs, owner, null, null);
        commands.Run("View.Mixer");
        commands.Run("View.Multitrack");
        commands.Run("View.Mixer");
        var mixer = owner.OwnedWindows.OfType<MixerWindow>().Single();
        mixer.Close();
        commands.Run("View.Mixer");
        Assert.NotSame(mixer, owner.OwnedWindows.OfType<MixerWindow>().Single());
        foreach (var window in owner.OwnedWindows.ToList()) window.Close();
        owner.Close();
    }
}
