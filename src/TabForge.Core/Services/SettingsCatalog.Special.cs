namespace TabForge.Services;

// Owns: the wording of everything on the Common settings, Shortcuts and Advanced pages (and the Theme card's reset) that is not a catalogue row,
//       so those pages and the search read the same labels.
// Does not own: the controls (PreferencesSpecialPages, ShortcutsPage) or what the buttons do (PreferencesWindow).
// Tests: TabForge.Avalonia.Tests.SettingsSearchGuardTests.
public static partial class SettingsCatalog
{
    public const string LinkAppearance = "link.appearance", LinkAudio = "link.audio", LinkPlayback = "link.playback", LinkScore = "link.score",
        LinkFiles = "link.files", LinkShortcuts = "link.shortcuts";
    public const string InfoShortcutsNote = "info.shortcutsnote", InfoSongSettings = "info.songsettings", InfoVersion = "info.version",
        InfoSettingsFile = "info.settingsfile", InfoCompatibility = "info.compatibility", InfoArrangement = "info.arrangement";
    public const string ActionResetTheme = "action.resettheme", ActionResetAll = "action.resetall", ActionPreset = "action.preset", ActionResetCategory = "action.resetcategory";
    public const string ActionResetPage = "action.resetpage", ActionManage = "action.manage", ActionImport = "action.import", ActionExport = "action.export",
        ActionCancel = "action.cancel", ActionApply = "action.apply", ActionOk = "action.ok";
    /// <summary>The window's footer: Reset page, the Manage settings menu (import, export, reset all) and the Cancel, Apply and OK row. Found by search too.</summary>
    public static readonly IReadOnlyList<string> FooterActions = new[]
    {
        ActionResetPage, ActionManage, ActionImport, ActionExport, ActionResetAll, ActionCancel, ActionApply, ActionOk,
    };
    public const string ShortcutClear = "Clear", ShortcutReassign = "Reassign", ShortcutReset = "Reset";
    /// <summary>The accessible names of a shortcut row's buttons: the two hotkey boxes, their Clear buttons, Reassign and Reset. The page and the search both read these.</summary>
    public static string ShortcutSlotLabel(HotkeyAction action, int slot) => $"{action.Name} hotkey {slot}";
    public static string ShortcutClearLabel(HotkeyAction action, int slot) => $"{ShortcutClear} {ShortcutSlotLabel(action, slot)}";
    public static string ShortcutReassignLabel(HotkeyAction action) => $"{ShortcutReassign} {action.Name}";
    public static string ShortcutResetLabel(HotkeyAction action) => $"{ShortcutReset} {action.Name} shortcuts";

    /// <summary>The row's own button names, so a search for one finds that row only (a bare "clear" matches by its words, never through a shared word list).</summary>
    private static string ShortcutButtonLabels(HotkeyAction action) =>
        string.Join(' ', ShortcutSlotLabel(action, 1), ShortcutSlotLabel(action, 2), ShortcutClearLabel(action, 1), ShortcutClearLabel(action, 2), ShortcutReassignLabel(action), ShortcutResetLabel(action));

    private const string HomeLook = "Look", HomeSound = "Sound", HomeScore = "Score", HomeFiles = "Files", HomeShortcuts = "Shortcuts", HomeFooter = "Footer";

    /// <summary>The entry with <paramref name="id"/> from <see cref="SpecialEntries"/>.</summary>
    public static SettingsEntry Special(string id) => SpecialById.Value[id];

    private static readonly Lazy<IReadOnlyList<SettingsEntry>> SpecialList = new(BuildSpecialEntries);
    private static readonly Lazy<Dictionary<string, SettingsEntry>> SpecialById = new(() => SpecialList.Value.ToDictionary(entry => entry.Id));

    /// <summary>The buttons, links and info cards of the special pages (not catalogue rows, not shortcuts), in page order.</summary>
    public static IReadOnlyList<SettingsEntry> SpecialEntries() => SpecialList.Value;

    private static IReadOnlyList<SettingsEntry> BuildSpecialEntries() => new[]
    {
        LinkEntry(LinkAppearance, HomeLook, "All appearance settings", Appearance),
        LinkEntry(LinkAudio, HomeSound, "More audio settings", AudioVst),
        LinkEntry(LinkPlayback, HomeSound, "Playback and practice settings", Playback),
        LinkEntry(LinkScore, HomeScore, "All score settings", Score),
        LinkEntry(LinkFiles, HomeFiles, "Files and backups", Files),
        InfoEntry(InfoShortcutsNote, Home, HomeShortcuts, "Shortcuts", "Pick a preset layout (TabForge or another program's) and change any key on the Shortcuts page."),
        LinkEntry(LinkShortcuts, HomeShortcuts, "Open all shortcuts", Hotkeys),
        InfoEntry(InfoSongSettings, Home, "Looking for a song's settings?", "Looking for a song's settings?",
            "Preferences are for the program. A song's own settings are in other windows: Project settings (the File menu) and Track properties (right-click a track)."),
        ActionEntry(ActionResetTheme, Appearance, "Theme", "Reset theme…", "Restores the built-in dark palette, sizing, fonts and animation defaults.",
            "Restore the built-in dark palette, sizing, fonts and animation defaults (asks first)."),
        ActionEntry(ActionPreset, Hotkeys, "Commands", "Preset", "Pick a preset layout of shortcuts", "Choose a preset shortcut layout"),
        ActionEntry(ActionResetCategory, Hotkeys, "Commands", "Reset category", "Restore default shortcuts for the displayed category.", "Restore default shortcuts for the displayed category."),
        InfoEntry(InfoVersion, Advanced, "Version", "Version", $"TabForge {AppInfo.DisplayVersion}"),
        InfoEntry(InfoSettingsFile, Advanced, "Settings file", "Settings file", UserPaths.SettingsFile),
        InfoEntry(InfoCompatibility, Advanced, "Compatibility", "Compatibility",
            "Settings files from older versions are brought up to date when you load or import them. Anything TabForge does not recognise in a file is ignored.", "json migrate migrated settings model flat fields"),
        InfoEntry(InfoArrangement, Advanced, "Arrangement playback", "Arrangement playback",
            "When you edit or reorder sections during playback, the change takes effect at the next bar line so the music never glitches. This cannot be turned off.", "route boundary refresh safety"),
        ActionEntry(ActionResetAll, Advanced, "Reset", "Reset all settings…", "Restores every setting and all keyboard shortcuts to their defaults. You can still press Cancel to undo it.",
            "Restore every setting and every shortcut to its default (asks first)."),
        ActionEntry(ActionResetPage, Home, HomeFooter, "Reset page…", "Restores the settings on this page to their defaults.",
            "Restore the settings on this page to their defaults (asks first)."),
        ActionEntry(ActionManage, Home, HomeFooter, "Manage settings", "Import, export or reset settings.", "Import, export or reset settings."),
        ActionEntry(ActionImport, Home, HomeFooter, "Import settings…", "Loads settings from a settings file.", "Load settings from a settings file."),
        ActionEntry(ActionExport, Home, HomeFooter, "Export settings…", "Saves the current settings to a settings file.", "Save the current settings to a settings file."),
        ActionEntry(ActionCancel, Home, HomeFooter, "Cancel", "Discards unapplied changes and closes settings.", "Discard unapplied changes and close settings."),
        ActionEntry(ActionApply, Home, HomeFooter, "Apply", "Commits changes and keeps this window open.", "Commit changes and keep this window open."),
        ActionEntry(ActionOk, Home, HomeFooter, "OK", "Commits changes and closes settings.", "Commit changes and close settings."),
    };

    private static SettingsEntry LinkEntry(string id, string section, string label, string target) =>
        new(id, SettingsEntryKind.Link, Home, section, label, SearchTextOf(Home, label, section, id, target), target: target);

    private static SettingsEntry InfoEntry(string id, string page, string section, string label, string text, string keywords = "") =>
        new(id, SettingsEntryKind.Info, page, section, label, SearchTextOf(page, label, text, section, id, keywords), text);

    private static SettingsEntry ActionEntry(string id, string page, string section, string label, string description, string tip) =>
        new(id, SettingsEntryKind.Action, page, section, label, SearchTextOf(page, label, description, section, id, tip), description, tip: tip);
}
