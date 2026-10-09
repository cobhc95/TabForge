using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the General preferences data: updates, files, window and confirmations.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public sealed class GeneralSettings
{
    /// <summary>Songs opened from Explorer while TabForge runs: "A new tab" (default) or "A new window".</summary>
    public string OpenFromExplorer { get; set; } = "A new tab";
    /// <summary>Look for a newer TabForge release on GitHub at most once a day (one anonymous HTTPS request).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Minutes between autosaves of unsaved songs into the Recovery folder; 0 = off.</summary>
    public int AutosaveMinutes { get; set; } = 2;
    /// <summary>When the last automatic update check ran (UTC); throttles checks to once a day.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }
    /// <summary>"gp" (Guitar Pro 7/8 file with the full TabForge project embedded) or "tforge".</summary>
    public string DefaultSaveFormat { get; set; } = "gp";
    /// <summary>
    /// Windows integration: open .gp/.gp5/.gpx/.tforge files with TabForge (per-user registration).
    /// Mirrors the registry: read from it at start-up, applied to it on Apply/OK.
    /// </summary>
    public bool AssociateFiles { get; set; }
    /// <summary>Show the main toolbar.</summary>
    public bool ShowToolbar { get; set; } = true;
    /// <summary>Show the status bar at the bottom.</summary>
    public bool ShowStatusBar { get; set; } = true;
    /// <summary>Ask before closing with unsaved changes.</summary>
    public bool ConfirmOnClose { get; set; } = true;
    /// <summary>Warn before discarding staged changes in the Preferences window.</summary>
    public bool ConfirmDiscardSettingsChanges { get; set; } = true;
    /// <summary>Ask before removing all musical content in a section.</summary>
    public bool ConfirmDeleteSection { get; set; } = true;
    /// <summary>Keep the playhead in view while playing.</summary>
    public bool AutoScroll { get; set; } = true;
    /// <summary>Remember the window size/position between runs.</summary>
    public bool RestoreWindow { get; set; } = true;
    /// <summary>Id of the chapter the Tutorial window (Help > Tutorial) showed last; empty = the first chapter.</summary>
    public string TutorialLastChapter { get; set; } = "";
    /// <summary>Which guide the Tutorial window showed last: "basic" or "detailed".</summary>
    public string TutorialLastGuide { get; set; } = "basic";
    /// <summary>Size of the Tutorial window in device-independent units; 0 = the default size.</summary>
    public double TutorialWindowWidth { get; set; }
    public double TutorialWindowHeight { get; set; }
}
