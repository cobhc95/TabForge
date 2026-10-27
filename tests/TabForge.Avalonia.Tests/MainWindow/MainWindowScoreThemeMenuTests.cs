using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the View menu's score page colour commands (MainWindowScoreThemeMenu): each sets the shown song's paper to light or dark
//   and leaves the saved preferences alone.
// Does not own: the paper and ink colours (Core ScoreAppearance), the notation commands (MainWindowNotationMenuTests).
// Tests: this file.
public class MainWindowScoreThemeMenuTests
{
    [AvaloniaFact]
    public void ScoreThemeCommandsSetTheScorePaper()
    {
        var score = new ScorePreviewView();
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowScoreThemeMenu.Register(commands, score);

        Assert.All(MainWindowScoreThemeMenu.Commands, id => Assert.True(commands.Handles(id), id));
        commands.Run("View.ScoreThemeLight");
        Assert.True(score.LightPaper);
        commands.Run("View.ScoreThemeDark");
        Assert.False(score.LightPaper);
    }

    [AvaloniaFact]
    public void TheSongKeepsThePaperAndTheSavedDefaultIsNotWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-score-theme-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var settings = MainWindowSettings.Open(path);
            var song = MainWindowTabs.NewSong();
            var score = new ScorePreviewView();
            var commands = new MainWindowCommands(new Window(), () => { });
            string? status = null;
            MainWindowScoreThemeMenu.Register(commands, score, null, () => song, text => status = text);
            MainWindowNotationMenu.Register(commands, score, settings: settings, notice: text => status = text);

            commands.Run("View.ScoreThemeLight");
            Assert.False(song.DarkPaper);
            Assert.Equal("Dark", settings.Settings.Appearance.ScorePaper);   // the View menu writes the song only
            Assert.Equal("Light score page", status);
            commands.Run("View.NotationTab");
            Assert.Equal("TabOnly", settings.Settings.Notation);
            Assert.True(settings.Settings.NotationPreferenceSet);
            Assert.Equal("Notation: tablature only", status);
        }
        finally { File.Delete(path); }
    }
}
