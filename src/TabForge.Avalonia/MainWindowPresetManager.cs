using System.Collections.Generic;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Presets;
using TabForge.Presenters;

namespace TabForge.Avalonia;

// Owns: the Tools.PresetManager command (the Tools menu's Preset manager row): configures the one manager helper with the delete
//   setting and opens it on the plug-in presets group.
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the manager and its presets
//   (PresetManager, PresetManagerWindow) or the delete setting itself (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.PresetManagerWindowTests, PresetManagerOpenTests.
/// <summary>The Tools menu's Preset manager command.</summary>
public static class MainWindowPresetManager
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.PresetManager" };

    /// <param name="owner">The window the manager opens over.</param>
    /// <param name="confirmDeletePresets">Read at each delete: true asks the question before a user preset is deleted.</param>
    /// <param name="rememberDeletePresets">Turns the setting off and saves it when the delete question is ticked and answered Yes.</param>
    public static void Register(MainWindowCommands commands, Window owner, Func<bool> confirmDeletePresets, Action<bool> rememberDeletePresets)
    {
        PresetManager.Configure(confirmDeletePresets, rememberDeletePresets);
        commands.Add("Tools.PresetManager", () => PresetManager.Open(owner, PresetCatalogKind.PluginPreset, null));
    }
}
