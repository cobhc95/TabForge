using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia;

// Owns: the Tools.SongStats command (the Tools menu's Song stats row): opens the Song stats window on the shown song and its file.
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the window and its texts (SongStatsWindow, SongStatsPresenter).
// Tests: TabForge.Avalonia.Tests.SongStatsWindowTests.
/// <summary>The Tools menu's Song stats command, bound to the shown song.</summary>
public static class MainWindowSongStats
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.SongStats" };

    /// <param name="songs">The tabs; the command reads the shown song.</param>
    /// <param name="owner">The window the dialog opens over.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner) =>
        commands.Add("Tools.SongStats", () => _ = new SongStatsWindow(songs.Active.Project, songs.Active.Path).ShowDialog(owner));
}
