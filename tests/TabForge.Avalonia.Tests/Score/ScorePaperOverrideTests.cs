using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the per-song score paper: the View menu writes only the shown song, each tab keeps its own paper across switches,
//   and a song joining a window takes the Preferences default.
// Does not own: the paper colours (ScoreSettingsBindingTests) or the menu rows (MainWindowScoreThemeMenuTests).
// Tests: this file.
public class ScorePaperOverrideTests
{
    private static MainWindowTabs Tabs(AppSettings settings, out SongDocumentHolder first)
    {
        var song = MainWindowTabs.NewSong();
        first = new SongDocumentHolder(song);
        return new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), settings: settings);
    }

    [AvaloniaFact]
    public void EachSongKeepsItsOwnPaperAcrossTabSwitches()
    {
        var settings = new AppSettings();
        var tabs = Tabs(settings, out var first);
        var score = new ScorePreviewView();
        _ = new MainWindowDocumentPanes(tabs, score);
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowScoreThemeMenu.Register(commands, score, shownSong: () => tabs.Active);

        tabs.New();
        var second = tabs.Active;
        Assert.False(score.LightPaper);

        tabs.Switch(0);
        commands.Run("View.ScoreThemeLight");
        Assert.True(score.LightPaper);
        Assert.False(first.Song.DarkPaper);

        tabs.Switch(1);
        Assert.False(score.LightPaper);
        Assert.True(second.DarkPaper);

        tabs.Switch(0);
        Assert.True(score.LightPaper);
        Assert.False(first.Song.DarkPaper);
    }

    [AvaloniaFact]
    public void ANewSongTakesTheSavedPaperDefault()
    {
        // The global default is a Preferences row: it seeds a new song, which then owns its paper.
        var settings = new AppSettings();
        settings.Appearance.ScorePaper = "Light";
        var tabs = Tabs(settings, out _);
        tabs.New();
        Assert.False(tabs.Active.DarkPaper);
        Assert.True(tabs.Active.PaperSet);
    }

    private sealed record SongDocumentHolder(SongDocument Song);
}
