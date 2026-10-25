using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the App.CommandPalette command (the App menu's Command palette row): opens the palette over the window, listing every catalogued
//   command the window handles, and runs the command the user picks.
// Does not own: the filter and the chosen command (CommandPalettePresenter), the palette's layout (CommandPaletteWindow), the handlers
//   (MainWindowCommands), the key texts and command names (HotkeyCatalog).
// Tests: TabForge.Avalonia.Tests.MainWindowCommandPaletteTests.
/// <summary>The App menu's Command palette command, listing the commands this window handles.</summary>
public static class MainWindowCommandPalette
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "App.CommandPalette" };

    /// <param name="commands">The window's command table: the palette lists the ids it handles and runs the one picked.</param>
    /// <param name="owner">The window the palette opens over.</param>
    /// <param name="hotkeys">The hotkey settings, read each time the palette opens, for the key texts.</param>
    public static void Register(MainWindowCommands commands, Window owner, Func<HotkeySettings> hotkeys) =>
        commands.Add("App.CommandPalette", () => Open(commands, owner, hotkeys()));

    /// <summary>The palette entries: every catalogued command the table handles, titled "Category: Name".</summary>
    public static IReadOnlyList<CommandPaletteEntry> Entries(MainWindowCommands commands, HotkeySettings hotkeys) =>
        HotkeyCatalog.All
            .Where(action => commands.Handles(action.Id))
            .Select(action => new CommandPaletteEntry(action.Id, $"{action.Category}: {action.Name}", HotkeyCatalog.DisplayAll(hotkeys, action.Id), action.Description))
            .ToList();

    private static void Open(MainWindowCommands commands, Window owner, HotkeySettings hotkeys)
    {
        var palette = new CommandPaletteWindow(Entries(commands, hotkeys));
        palette.Closed += (_, _) => { if (palette.ChosenId is { } id) commands.Run(id); };
        palette.Show(owner);
    }
}
