namespace TabForge.Services.Features;

// Owns: the shape of one feature module: the rows it adds to the central tables (settings, layout, hotkeys, commands, menu), its settings bounds.
// Does not own: the tables (SettingsCatalog, HotkeyCatalog, MainWindow.Commands, MainWindow.Menus), the handlers, or the feature code.
// Tests: TestFeatureModuleContributions.

/// <summary>Marker for the object that runs the commands of the modules (the main window); a module asks for its own host interface.</summary>
public interface IFeatureHost { }

/// <summary>A hotkey row inserted straight after the catalogue row <see cref="AfterId"/>.</summary>
public sealed record FeatureHotkey(HotkeyAction Action, string AfterId);

/// <summary>A Preferences group inserted on <see cref="Page"/> after the last entry of group <see cref="AfterGroup"/>; <see cref="Keys"/> as in SettingsCatalog's layout.</summary>
public sealed record FeatureLayout(string Page, string Group, string Keys, string AfterGroup);

/// <summary>A plain command: its catalogue id and what it runs.</summary>
public sealed record FeatureCommand(string Id, Action Run);

/// <summary>A main-menu row for command <see cref="Id"/>, inserted right after the row of command <see cref="AfterId"/> in menu <see cref="Menu"/>.</summary>
public sealed record FeatureMenuRow(string Menu, string AfterId, string Header, string Id);

/// <summary>One feature's contributions to the central tables. Every member has a no-op default.</summary>
public interface IFeatureModule
{
    string Name { get; }
    IReadOnlyList<FeatureHotkey> Hotkeys => Array.Empty<FeatureHotkey>();
    IReadOnlyList<FeatureMenuRow> MenuRows => Array.Empty<FeatureMenuRow>();
    IReadOnlyList<FeatureLayout> Layout => Array.Empty<FeatureLayout>();
    IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => Array.Empty<SettingDescriptor>();
    IEnumerable<FeatureCommand> Commands(IFeatureHost host) => Array.Empty<FeatureCommand>();
    /// <summary>Clamps the feature's saved values to safe bounds (called from SettingsValidator at the place the feature's own call used to be).</summary>
    void Normalize(AppSettings settings) { }
}
