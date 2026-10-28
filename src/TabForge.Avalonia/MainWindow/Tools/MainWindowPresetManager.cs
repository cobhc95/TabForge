using System.Collections.Generic;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Presets;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia;

// Owns: the Tools.PresetManager command (the Tools menu's Preset manager row): configures the one manager helper with the stores (the settings that
//   hold the bend and tremolo presets) and opens it on the plug-in presets group.
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the manager and its presets
//   (PresetManager, PresetManagerWindow).
// Tests: TabForge.Avalonia.Tests.PresetManagerWindowTests, PresetManagerOpenTests.
/// <summary>The Tools menu's Preset manager command.</summary>
public static class MainWindowPresetManager
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.PresetManager" };

    /// <param name="owner">The window the manager opens over.</param>
    /// <param name="settings">The app settings (null before they load): the bend, tremolo and ornament presets live in them.</param>
    /// <param name="saveSettings">Writes the settings after a preset of those kinds changes.</param>
    public static void Register(MainWindowCommands commands, Window owner, Func<AppSettings?> settings, Action saveSettings)
    {
        var stores = new PresetStoreFactory(new EffectPresetSource(settings, saveSettings));
        PresetManager.Configure(() => stores);
        commands.Add("Tools.PresetManager", () => PresetManager.Open(owner, PresetCatalogKind.PluginPreset, null));
    }
}
