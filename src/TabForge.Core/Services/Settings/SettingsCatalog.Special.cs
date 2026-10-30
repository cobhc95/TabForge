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
    public const string ActionResetTheme = "action.resettheme", ActionResetAll = "action.resetall", ActionResetApp = "action.resetapp",
        ActionPreset = "action.preset", ActionResetCategory = "action.resetcategory";
    public const string ActionResetPage = "action.resetpage", ActionManage = "action.manage", ActionImport = "action.import", ActionExport = "action.export",
        ActionCancel = "action.cancel", ActionApply = "action.apply", ActionOk = "action.ok";
    /// <summary>The Sound banks rows' buttons: search finds them and opens the Audio & Plug-ins page where the list lives.</summary>
    public const string BankViewLicence = "action.bank.licence", BankDownload = "action.bank.download", BankDownloadAgain = "action.bank.downloadagain",
        BankDelete = "action.bank.delete", BankUse = "action.bank.use", BankChooseFile = "action.bank.choose";
    public static readonly IReadOnlyList<string> BankActions = new[] { BankViewLicence, BankDownload, BankDownloadAgain, BankDelete, BankUse, BankChooseFile };
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
        LinkEntry(LinkAppearance, HomeLook, "All appearance settings", Appearance, "look theme colours colors appearance"),
        LinkEntry(LinkAudio, HomeSound, "More audio settings", AudioVst, "sound audio vst plugins"),
        LinkEntry(LinkPlayback, HomeSound, "Playback and practice settings", Playback, "playback practice sound"),
        LinkEntry(LinkScore, HomeScore, "All score settings", Score, "score notation"),
        LinkEntry(LinkFiles, HomeFiles, "Files and backups", Files, "files backups save"),
        InfoEntry(InfoShortcutsNote, Home, HomeShortcuts, "Shortcuts", "Pick a preset layout (TabForge or another program's) and change any key on the Shortcuts page.", "shortcuts hotkeys keybinding preset layout"),
        LinkEntry(LinkShortcuts, HomeShortcuts, "Open all shortcuts", Hotkeys, "shortcuts hotkeys keys open"),
        InfoEntry(InfoSongSettings, Home, "Looking for a song's settings?", "Looking for a song's settings?",
            "Preferences are for the program. A song's own settings are in other windows: Project settings (the File menu) and Track properties (right-click a track).", "song project track settings help"),
        ActionEntry(ActionResetTheme, Appearance, "Theme", "Reset theme…", "Restores the built-in dark palette, sizing, fonts and animation defaults.",
            "Restore the built-in dark palette, sizing, fonts and animation defaults (asks first).",
            "theme reset default restore palette colours colors factory wipe"),
        ActionEntry(ActionPreset, Hotkeys, "Commands", "Preset", "Pick a preset layout of shortcuts", "Choose a preset shortcut layout", "preset layout shortcuts hotkeys keybinding default restore"),
        ActionEntry(ActionResetCategory, Hotkeys, "Commands", "Reset category", "Restore default shortcuts for the displayed category.", "Restore default shortcuts for the displayed category.", "reset category default restore shortcuts hotkeys clear"),
        InfoEntry(InfoVersion, Advanced, "Version", "Version", $"TabForge {AppInfo.DisplayVersion}", "version about"),
        InfoEntry(InfoSettingsFile, Advanced, "Settings file", "Settings file", UserPaths.SettingsFile, "settings file path location folder backup export"),
        InfoEntry(InfoCompatibility, Advanced, "Compatibility", "Compatibility",
            "Settings files from older versions are brought up to date when you load or import them. Anything TabForge does not recognise in a file is ignored.", "json migrate migrated settings model flat fields compatibility backward backwards old version update upgrade"),
        InfoEntry(InfoArrangement, Advanced, "Arrangement playback", "Arrangement playback",
            "When you edit or reorder sections during playback, the change takes effect at the next bar line so the music never glitches. This cannot be turned off.", "route boundary refresh safety arrangement sections playback seamless bar glitch loop"),
        ActionEntry(ActionResetAll, Advanced, "Reset", "Reset all settings…",
            "Restores every option and keyboard shortcut in Settings to its default. Your presets, layouts, folders and songs are not touched.",
            "Restore every option and keyboard shortcut in Settings to its default (asks first); your presets, layouts, folders and songs are not touched.",
            "reset wipe default factory clear erase restore defaults backup"),
        ActionEntry(ActionResetApp, Advanced, "Reset", "Reset entire app…",
            "Permanently deletes the app stores you tick: settings and shortcuts, layouts, presets, the plug-in library, recent folders, recovery files, SoundFonts and logs. Your songs are never touched.",
            "Permanently delete the app stores you tick (asks twice); your songs are never touched.",
            "reset wipe default factory clear erase restore defaults uninstall delete remove"),
        ActionEntry(ActionResetPage, Home, HomeFooter, "Reset page…", "Restores the settings on this page to their defaults.",
            "Restore the settings on this page to their defaults (asks first).",
            "reset page default restore clear wipe"),
        ActionEntry(ActionManage, Home, HomeFooter, "Manage settings", "Import, export or reset settings.", "Import, export or reset settings.", "manage import export reset backup restore"),
        ActionEntry(ActionImport, Home, HomeFooter, "Import settings…", "Loads settings from a settings file.", "Load settings from a settings file.", "import backup restore load open"),
        ActionEntry(ActionExport, Home, HomeFooter, "Export settings…", "Saves the current settings to a settings file.", "Save the current settings to a settings file.", "export backup save download"),
        ActionEntry(ActionCancel, Home, HomeFooter, "Cancel", "Discards unapplied changes and closes settings.", "Discard unapplied changes and close settings.", "cancel close discard"),
        ActionEntry(ActionApply, Home, HomeFooter, "Apply", "Commits changes and keeps this window open.", "Commit changes and keep this window open.", "apply save keep"),
        ActionEntry(ActionOk, Home, HomeFooter, "OK", "Commits changes and closes settings.", "Commit changes and close settings.", "ok apply save close"),
        ActionEntry(BankViewLicence, AudioVst, "Sound banks", "View licence", "Shows the licence of a Sound bank.", "Open Settings > Audio & Plug-ins > Sound banks", "sound bank soundfont sf2 licence license view"),
        ActionEntry(BankDownload, AudioVst, "Sound banks", "Download", "Downloads a Sound bank and checks it against a stored checksum.", "Open Settings > Audio & Plug-ins > Sound banks", "sound bank soundfont sf2 download install get"),
        ActionEntry(BankDownloadAgain, AudioVst, "Sound banks", "Download again", "Downloads a Sound bank again and checks it against a stored checksum.", "Open Settings > Audio & Plug-ins > Sound banks", "sound bank soundfont sf2 download again install"),
        ActionEntry(BankDelete, AudioVst, "Sound banks", "Delete", "Deletes a downloaded Sound bank after asking.", "Open Settings > Audio & Plug-ins > Sound banks", "sound bank soundfont sf2 delete remove erase uninstall"),
        ActionEntry(BankUse, AudioVst, "Sound banks", "Use", "Plays this Sound bank with the built-in synth.", "Open Settings > Audio & Plug-ins > Sound banks", "sound bank soundfont sf2 use play select"),
        ActionEntry(BankChooseFile, AudioVst, "Sound banks", "Choose file…", "Plays your own .sf2 file in place; TabForge never copies or deletes it.", "Open Settings > Audio & Plug-ins > Sound banks", "sound bank soundfont sf2 choose file browse open own"),
    };

    private static SettingsEntry LinkEntry(string id, string section, string label, string target, string keywords = "") =>
        new(id, SettingsEntryKind.Link, Home, section, label, SearchTextOf(Home, label, section, id, target, keywords), target: target);

    private static SettingsEntry InfoEntry(string id, string page, string section, string label, string text, string keywords = "") =>
        new(id, SettingsEntryKind.Info, page, section, label, SearchTextOf(page, label, text, section, id, keywords), text);

    private static SettingsEntry ActionEntry(string id, string page, string section, string label, string description, string tip, string keywords = "") =>
        new(id, SettingsEntryKind.Action, page, section, label, SearchTextOf(page, label, description, section, id, tip, keywords), description, tip: tip);
}
