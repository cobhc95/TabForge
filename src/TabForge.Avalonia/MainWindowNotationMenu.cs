using System.Collections.Generic;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: the View menu's score notation commands (Tablature + standard, Tablature only, Standard notation only) on the shown score:
//   each sets the score's and the shown song's notation mode, saves it in the settings (Notation, NotationPreferenceSet), redraws,
// shows the status text and moves the menu check; the saved mode is applied at startup.
// Does not own: the menu rows (MainMenuTable in Core), the notation drawing (Core ScoreGeometryProbe and ScoreLayoutEngine), the
//   notation toggle of the score's right-click row (MainWindowScoreRows), the commands table (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.MainWindowNotationMenuTests.
public static class MainWindowNotationMenu
{
    private const string BothId = "View.NotationBoth";
    private const string TabId = "View.NotationTab";
    private const string StaffId = "View.NotationStaff";

    /// <summary>The View menu notation command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { BothId, TabId, StaffId };

    /// <param name="score">The score view whose notation mode the commands change.</param>
    /// <param name="owner">The window whose menu rows show the check; null shows none.</param>
    /// <param name="settings">The settings file the mode is saved to and restored from; null keeps it for this run only.</param>
    /// <param name="shownSong">The shown song, whose notation mode is set too; null sets none.</param>
    /// <param name="notice">Shows the status text; null shows nothing.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window? owner = null, MainWindowSettings? settings = null,
        Func<SongDocument?>? shownSong = null, Action<string>? notice = null)
    {
        commands.Add(BothId, () => SetNotation(score, NotationMode.TabAndStaff, owner, settings, shownSong, notice));
        commands.Add(TabId, () => SetNotation(score, NotationMode.TabOnly, owner, settings, shownSong, notice));
        commands.Add(StaffId, () => SetNotation(score, NotationMode.StaffOnly, owner, settings, shownSong, notice));
        if (settings is not null && settings.Settings.NotationPreferenceSet) score.Probe.Notation = NotationNames.Normalize(settings.Settings.Notation);
        if (owner is not null) owner.Opened += (_, _) => SyncChecks(owner, score.Probe.Notation);
    }

    /// <summary>The status text for <paramref name="mode"/>.</summary>
    public static string StatusText(NotationMode mode) => "Notation: " + mode switch
    {
        NotationMode.TabOnly => "tablature only",
        NotationMode.StaffOnly => "standard notation only",
        _ => "tablature + standard"
    };

    private static void SetNotation(ScorePreviewView score, NotationMode mode, Window? owner, MainWindowSettings? settings, Func<SongDocument?>? shownSong, Action<string>? notice)
    {
        score.Probe.Notation = mode;
        if (shownSong?.Invoke() is { } song) song.Notation = mode;
        if (owner is not null) SyncChecks(owner, mode);
        score.Refresh();
        notice?.Invoke(StatusText(mode));
        if (settings is null) return;
        settings.Settings.Notation = mode.ToString();
        settings.Settings.NotationPreferenceSet = true;
        settings.SaveSettingsDeferred();
    }

    private static void SyncChecks(Window owner, NotationMode mode)
    {
        MainWindowArrangementModes.SetCheck(owner, BothId, mode == NotationMode.TabAndStaff);
        MainWindowArrangementModes.SetCheck(owner, TabId, mode == NotationMode.TabOnly);
        MainWindowArrangementModes.SetCheck(owner, StaffId, mode == NotationMode.StaffOnly);
    }
}
