using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the View menu's notation commands (MainWindowNotationMenu): each sets the shown score's notation mode.
// Does not own: the score's right-click notation row (MainWindowRowCommandsTests), the notation drawing (Core score layout).
// Tests: this file.
public class MainWindowNotationMenuTests
{
    [AvaloniaFact]
    public void NotationCommandsSetTheScoreNotationMode()
    {
        var score = new ScorePreviewView();
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowNotationMenu.Register(commands, score);

        Assert.All(MainWindowNotationMenu.Commands, id => Assert.True(commands.Handles(id), id));
        commands.Run("View.NotationTab");
        Assert.Equal(NotationMode.TabOnly, score.Probe.Notation);
        commands.Run("View.NotationStaff");
        Assert.Equal(NotationMode.StaffOnly, score.Probe.Notation);
        commands.Run("View.NotationBoth");
        Assert.Equal(NotationMode.TabAndStaff, score.Probe.Notation);
    }

    [AvaloniaFact]
    public void OpenedSongTakesThePreferredNotationAndKeepsItsOwnOnSwitch()
    {
        var score = new ScorePreviewView();
        score.Probe.Notation = NotationMode.TabOnly;
        var first = new TabForge.Documents.SongDocument(() => null);
        score.Show(first);
        Assert.Equal(NotationMode.TabOnly, first.Notation);
        var second = new TabForge.Documents.SongDocument(() => null);
        score.Show(second);
        second.Notation = NotationMode.StaffOnly;
        score.Show(first);
        Assert.Equal(NotationMode.TabOnly, score.Probe.Notation);
        score.Show(second);
        Assert.Equal(NotationMode.StaffOnly, score.Probe.Notation);
    }

    private static bool[] Checks(MenuItem parent, params string[] ids) => ids.Select(id => parent.Items.OfType<MenuItem>().Single(i => (string?)i.Tag == id).IsChecked).ToArray();

    [AvaloniaFact]
    public void ChoiceRowsAreCheckableAndTheirCheckFollowsTheLiveValue()
    {
        var choices = new[] { "View.NotationBoth", "View.NotationTab", "View.NotationStaff", "View.ScoreThemeDark", "View.ScoreThemeLight", "View.Zoom75", "View.Zoom100", "View.Zoom150" };
        var rows = TabForge.Presenters.MainMenuTable.CommandRows(TabForge.Presenters.MainMenuTable.WithFeatureRows()).ToList();
        Assert.All(choices, id => Assert.True(rows.Single(r => r.CommandId == id).IsCheckable, id));

        var score = new ScorePreviewView();
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowNotationMenu.Register(commands, score);
        MainWindowScoreThemeMenu.Register(commands, score);
        MainWindowZoomPresets.Register(commands, score);
        var parent = new MenuItem();
        foreach (var id in choices) parent.Items.Add(new MenuItem { Tag = id, ToggleType = MenuItemToggleType.CheckBox });
        var notation = choices[..3];

        MainWindowMenu.RefreshChecks(parent, commands);
        Assert.Equal(new[] { true, false, false }, Checks(parent, notation));
        commands.Run("View.NotationTab");
        MainWindowMenu.RefreshChecks(parent, commands);
        Assert.Equal(new[] { false, true, false }, Checks(parent, notation));
        score.Probe.Notation = NotationMode.StaffOnly;   // set from elsewhere (right-click row, tab switch, Preferences)
        MainWindowMenu.RefreshChecks(parent, commands);
        Assert.Equal(new[] { false, false, true }, Checks(parent, notation));

        score.LightPaper = false;
        MainWindowMenu.RefreshChecks(parent, commands);
        Assert.Equal(new[] { true, false }, Checks(parent, "View.ScoreThemeDark", "View.ScoreThemeLight"));
        commands.Run("View.ScoreThemeLight");
        MainWindowMenu.RefreshChecks(parent, commands);
        Assert.Equal(new[] { false, true }, Checks(parent, "View.ScoreThemeDark", "View.ScoreThemeLight"));

        commands.Run("View.Zoom150");
        MainWindowMenu.RefreshChecks(parent, commands);
        Assert.Equal(new[] { false, false, true }, Checks(parent, "View.Zoom75", "View.Zoom100", "View.Zoom150"));
    }
}
