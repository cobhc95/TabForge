using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Track.FxChain key opening the selected track's FX chain window through MainWindowKeyboard.Route.
// Tests: this class.
public class MainWindowTrackFxKeysTests
{
    [AvaloniaFact]
    public void TrackFxChainKeyOpensTheChainWindowOfTheSelectedTrack()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var owner = new Window();
        owner.Show();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowTrackFxKeys.Register(commands, tabs, owner, null, null, new TabForge.Plugins.PluginTrust());
        var settings = new HotkeySettings();
        settings.Set("Track.FxChain", 0, "F9");
        var keyboard = new MainWindowKeyboard(settings, commands);
        Assert.True(keyboard.Route(Key.F9, KeyModifiers.None, null));
        Assert.Single(owner.OwnedWindows.OfType<FxChainWindow>());
        foreach (var window in owner.OwnedWindows.ToList()) window.Close();
        owner.Close();
    }
}
