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
}
