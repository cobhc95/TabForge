using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the standing rule that keyboard mode never shows the fretboard: entering hides the
//   instrument panel, every reshow path refuses while the mode is on, and exiting restores it.
// Does not own: the Keyboard mode layout maths (KeyboardModeLayoutSwapTests), the dock rules or the panes.
// Tests: MainWindowLearnMode (enter/exit), DockWorkspaceController.SuppressInstrumentPanel, MainWindowViewMenu (instrument toggle).
public sealed class KeyboardModeNoInstrumentTests
{
    private const string InstrumentPanelId = "instrument";

    private static (Window Window, MainWindowCommands Commands, DockWorkspaceController Dock, MainWindowLearnMode Learn) Open()
    {
        var window = new Window();
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var dock = new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup);
        var commands = new MainWindowCommands(window, () => { });
        var learn = MainWindowLearnMode.Register(commands, window, dock, null, null);
        MainWindowViewMenu.Register(commands, window, dock, new ScorePreviewView(), null);
        return (window, commands, dock, learn);
    }

    private static void EnsureInstrumentShown(DockWorkspaceController dock)
    {
        if (!dock.IsPanelVisible(InstrumentPanelId)) dock.SetPanelVisible(InstrumentPanelId, true);
        Assert.True(dock.IsPanelVisible(InstrumentPanelId));
    }

    [AvaloniaFact]
    public void KeyboardModeEnterHidesInstrumentAndBlocksEveryReshowPathUntilExit()
    {
        var (_, commands, dock, learn) = Open();
        EnsureInstrumentShown(dock);

        commands.Run("View.LearnMode");
        Assert.True(learn.IsOn);
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));

        dock.SetPanelVisible(InstrumentPanelId, true);
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));
        dock.ResetPanel(InstrumentPanelId);
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));
        dock.ApplyLayout(DockWorkspaceController.DefaultLayout());
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));
        Assert.False(dock.FloatPanel(InstrumentPanelId));

        var withFloating = DockWorkspaceController.DefaultLayout();
        FloatingSessionRules.OpenFloating(withFloating, InstrumentPanelId, "float-instrument", new UiRect(200, 160, 400, 300), out _);
        dock.ApplyLayout(withFloating);
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));

        commands.Run("View.LearnMode");
        Assert.False(learn.IsOn);
        Assert.True(dock.IsPanelVisible(InstrumentPanelId));
    }

    [AvaloniaFact]
    public void InstrumentViewToggleStaysOffWhileKeyboardModeIsOn()
    {
        var (window, commands, dock, _) = Open();
        var row = new MenuItem { Tag = "View.InstrumentPanel", IsChecked = true };
        window.Content = row;
        EnsureInstrumentShown(dock);

        commands.Run("View.LearnMode");
        commands.Run("View.InstrumentPanel");
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));
        Assert.False(row.IsChecked);

        commands.Run("View.LearnMode");
        commands.Run("View.InstrumentPanel");
        Assert.False(dock.IsPanelVisible(InstrumentPanelId));
        Assert.False(row.IsChecked);
        commands.Run("View.InstrumentPanel");
        Assert.True(dock.IsPanelVisible(InstrumentPanelId));
        Assert.True(row.IsChecked);
    }
}
