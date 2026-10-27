using System.Collections.Generic;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Documents;

namespace TabForge.Avalonia;

// Owns: the View menu's score page colour commands (Dark score page, Light score page): each sets which paper the shown score and song
// are drawn on, redraws, shows the status text and moves the menu check. The paper belongs to the song (SongDocument.DarkPaper), not
//   to the saved preferences: a new song takes the Preferences default and the View menu never writes it.
// Does not own: the menu rows (MainMenuTable in Core), the paper and ink colours (Core ScoreAppearance), the notation commands
//   (MainWindowNotationMenu), the commands table (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.MainWindowScoreThemeMenuTests.
public static class MainWindowScoreThemeMenu
{
    private const string DarkId = "View.ScoreThemeDark";
    private const string LightId = "View.ScoreThemeLight";

    /// <summary>The View menu score page colour command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { DarkId, LightId };

    /// <param name="score">The score view whose paper the commands change.</param>
    /// <param name="owner">The window whose menu rows show the check; null shows none.</param>
    /// <param name="shownSong">The shown song, whose paper is set too; null sets none.</param>
    /// <param name="notice">Shows the status text; null shows nothing.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window? owner = null,
        Func<SongDocument?>? shownSong = null, Action<string>? notice = null)
    {
        commands.Add(DarkId, () => SetLightPaper(score, false, owner, shownSong, notice));
        commands.Add(LightId, () => SetLightPaper(score, true, owner, shownSong, notice));
        commands.AddCheckState(DarkId, () => !score.LightPaper);
        commands.AddCheckState(LightId, () => score.LightPaper);
        if (owner is not null) owner.Opened += (_, _) => SyncChecks(owner, score.LightPaper);
    }

    /// <summary>The status text for the chosen paper.</summary>
    public static string StatusText(bool light) => light ? "Light score page" : "Dark score page";

    private static void SetLightPaper(ScorePreviewView score, bool light, Window? owner, Func<SongDocument?>? shownSong, Action<string>? notice)
    {
        // The View menu sets the shown song's paper only: the global default is a Preferences row, never this command.
        score.LightPaper = light;
        if (shownSong?.Invoke() is { } song) song.DarkPaper = !light;
        score.Refresh();
        if (owner is not null) SyncChecks(owner, light);
        notice?.Invoke(StatusText(light));
    }

    private static void SyncChecks(Window owner, bool light)
    {
        MainWindowArrangementModes.SetCheck(owner, DarkId, !light);
        MainWindowArrangementModes.SetCheck(owner, LightId, light);
    }
}
