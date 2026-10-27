using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Docking;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the check of the View menu's playing bar toggle on a score view (MainWindowViewMenu.TogglePlayingBar).</summary>
public sealed class MainWindowViewMenuTests
{
    [AvaloniaFact]
    public void PlayingBarToggleFlipsTheScoresBand()
    {
        var score = new ScorePreviewView();
        var before = score.Probe.Appearance.PlayingBarEnabled;

        MainWindowViewMenu.TogglePlayingBar(score, null);

        Assert.NotEqual(before, score.Probe.Appearance.PlayingBarEnabled);
    }

    private static (Window Window, MainWindowCommands Commands, MenuItem Row) FullscreenSetup()
    {
        var window = new Window();
        var commands = new MainWindowCommands(window, () => { });
        var row = new MenuItem { Tag = "View.Fullscreen", ToggleType = MenuItemToggleType.CheckBox };
        window.Content = new StackPanel { Children = { row } };
        MainWindowViewMenu.RegisterFullscreen(commands, window);
        return (window, commands, row);
    }

    [AvaloniaFact]
    public void FullscreenTogglesTrueFullScreenAndBackToThePreviousState()
    {
        var (window, commands, row) = FullscreenSetup();
        window.WindowState = WindowState.Maximized;
        commands.Run("View.Fullscreen");
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.True(row.IsChecked);
        commands.Run("View.Fullscreen");
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.False(row.IsChecked);
        window.WindowState = WindowState.Normal;
        commands.Run("View.Fullscreen");
        commands.Run("View.Fullscreen");
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [AvaloniaFact]
    public void F11IsBoundToFullscreenAndEscapeLeavesItOnlyWhileInIt()
    {
        var (window, commands, _) = FullscreenSetup();
        var keys = new MainWindowKeyboard(new HotkeySettings(), commands);
        Assert.Equal("View.Fullscreen", keys.CommandFor(Key.F11, KeyModifiers.None));
        Assert.False(keys.Route(Key.Escape, KeyModifiers.None, null));
        Assert.True(keys.Route(Key.F11, KeyModifiers.None, null));
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.True(keys.Route(Key.Escape, KeyModifiers.None, null));
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [Fact]
    public void LayoutsHoldDefaultAndSavedLayoutsOnlyAndLayoutHotkeysAreGone()
    {
        var saved = new System.Collections.Generic.List<SavedLayout> { new() { Name = "Mine" } };
        Assert.Equal(new[] { "Default", "Mine" }, DockLayoutCatalog.LayoutMenuRows(saved).Select(r => r.Name));
        Assert.All(DockLayoutCatalog.LayoutMenuRows(saved), r => Assert.Null(r.HotkeyId));
        Assert.Null(DockLayoutCatalog.Resolve(saved, "Compose"));
        Assert.NotNull(DockLayoutCatalog.Resolve(saved, "Default"));
        Assert.DoesNotContain(HotkeyCatalog.All, a => a.Id.StartsWith("View.Layout", System.StringComparison.Ordinal));
    }
}
