using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the General part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> GeneralRows(AppSettings s)
    {
        var g = s.General!;
        var a = s.Appearance!;
        return new List<SettingDescriptor>
        {
            // General
            Bool(General, "Updates", "general.checkupdates", "Check for updates automatically", v => g.CheckForUpdates = v, () => g.CheckForUpdates,
                "Once a day, ask GitHub whether a newer TabForge release exists and offer to open its download page. One anonymous HTTPS request; nothing is downloaded or installed automatically. Help > Check for updates works either way.",
                "update updates new version release github check automatic notify beta download"),
            Button(General, "Updates", "general.checknow", "Check for updates now",
                "Ask GitHub now whether a newer TabForge release exists, even when the automatic check is off. One anonymous HTTPS request; nothing is downloaded or installed.",
                "update updates check now manual new version release github"),
            Choice(General, "Files", "general.openfromexplorer", "Open songs from Explorer in", v => g.OpenFromExplorer = v, () => g.OpenFromExplorer,
                new[] { "A new tab", "A new window" },
                "When TabForge is already running, a song you double-click in Explorer opens as a new tab in that window (default), or in a separate TabForge window.",
                "open explorer double click file new tab window instance single"),
            Choice(General, "Files", "general.autosave", "Autosave unsaved songs", v => g.AutosaveMinutes = AutosaveChoices.ToMinutes(v),
                () => AutosaveChoices.ToLabel(g.AutosaveMinutes), AutosaveChoices.Labels,
                "Copies songs with unsaved changes into TabForge's Recovery folder at this interval (skipped while playing). Your own files are never overwritten; the copies are offered the next time TabForge starts after a crash, and removed when you save or close.",
                "autosave auto save backup recovery crash power loss interval minutes"),
            Choice(General, "Files", "general.saveformat", "Default save format", v => g.DefaultSaveFormat = v == "TabForge project (.tforge)" ? "tforge" : "gp",
                () => g.DefaultSaveFormat == "tforge" ? "TabForge project (.tforge)" : ".gp file (.gp)",
                new[] { ".gp file (.gp)", "TabForge project (.tforge)" },
                ".gp keeps every TabForge feature (stored inside the file). .tforge is TabForge-only.",
                "save format extension gp gp7 gp8 tforge default"),
            Bool(General, "Windows integration", "general.associate", "Open .gp and .tforge files with TabForge", v => g.AssociateFiles = v, () => g.AssociateFiles,
                "Adds TabForge to Explorer's Open with for .gp, .gp5, .gp4, .gp3, .gpx and .tforge (for your Windows account only). Turning it off removes TabForge's entries again.",
                "file association windows integration explorer open with gp gp5 gpx tforge double click"),
            Bool(General, "Application", "general.toolbar", "Show the toolbar", v => g.ShowToolbar = v, () => g.ShowToolbar,
                "Show the transport and editing toolbar.", "toolbar buttons transport bar"),
            Bool(General, "Application", "general.statusbar", "Show the status bar", v => g.ShowStatusBar = v, () => g.ShowStatusBar,
                "Show cursor position and status messages at the bottom of the window.", "status bar footer messages"),
            Bool(General, "Safety", "general.confirmclose", "Confirm before closing unsaved work", v => g.ConfirmOnClose = v, () => g.ConfirmOnClose,
                "Ask before discarding unsaved changes when closing a tab or window.", "confirm prompt unsaved dirty"),
            Bool(General, "Safety", "general.confirmdiscardsettings", "Warn before discarding unapplied changes", v => g.ConfirmDiscardSettingsChanges = v, () => g.ConfirmDiscardSettingsChanges,
                "Ask before closing Settings, Track properties or Project settings without applying edits. Turned off by \"Don't ask me again\".",
                "preferences settings cancel discard track project dont ask again remember warning"),

        };
    }
}
