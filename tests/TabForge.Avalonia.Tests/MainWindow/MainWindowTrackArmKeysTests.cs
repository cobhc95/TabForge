using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Track.Arm key reaching the selected track through MainWindowKeyboard.Route: one press arms it, a second disarms it.
// Tests: this class.
public class MainWindowTrackArmKeysTests
{
    [AvaloniaFact]
    public void TrackArmKeyTogglesTheSelectedTrack()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowTrackArmKeys.Register(commands, tabs);
        var settings = new HotkeySettings();
        settings.Set("Track.Arm", 0, "F9");
        var keyboard = new MainWindowKeyboard(settings, commands);
        var track = song.Project.Tracks[song.TrackIndex];
        var before = track.RecordArm;
        Assert.True(keyboard.Route(Key.F9, KeyModifiers.None, null));
        Assert.Equal(!before, track.RecordArm);
        Assert.True(keyboard.Route(Key.F9, KeyModifiers.None, null));
        Assert.Equal(before, track.RecordArm);
    }
}
