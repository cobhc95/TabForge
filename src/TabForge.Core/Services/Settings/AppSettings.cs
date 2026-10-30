using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Models;
using TabForge.Platform.Theme;

namespace TabForge.Services;

// Owns: the data classes of every persisted setting, with their defaults.
// Does not own: loading, saving, migrating and validating (AppSettingsStore, SettingsMigration, SettingsValidator).
// Tests: no dedicated test class.
/// <summary>
/// Everything the user can change, persisted to %APPDATA%\TabForge\settings.json. The legacy flat
/// properties are kept so older files still load; new settings live in the category objects.
/// </summary>
public sealed class AppSettings
{
    // ---- legacy flat fields (still read/written so existing files keep working) ----
    public double InstrumentHeight { get; set; } = 182;
    public double ArrangementHeight { get; set; } = 248;
    /// <summary>Width of the right-side workspace panel, draggable.</summary>
    public double BottomTabsWidth { get; set; } = 360;
    /// <summary>Height of the upper Sections panel in the right-side split layout.</summary>
    public double SectionsPanelHeight { get; set; } = 260;
    public bool ShowInstrument { get; set; } = true;
    public bool ShowArrangement { get; set; } = true;
    public bool DarkPaper { get; set; } = true;
    public bool LeftHanded { get; set; }
    public bool ShowNoteNames { get; set; }
    public bool PreviewNotes { get; set; } = true;
    public int PreviewHorizon { get; set; } = 6;
    public string? ScaleHighlight { get; set; }
    public int FretboardFrets { get; set; } = 24;
    public string? Notation { get; set; } = "TabAndStaff";
    public bool NotationPreferenceSet { get; set; }
    public double ZoomFactor { get; set; } = 1.0;   // 1 = 100%; 0 = fit width
    public bool Metronome { get; set; }
    public bool CountIn { get; set; }
    public double Speed { get; set; } = 1.0;
    public double WindowWidth { get; set; } = 1500;
    public double WindowHeight { get; set; } = 960;
    // The main window's top-left corner in screen pixels; null lets the OS place the window. Restored only while the window is mostly on a screen.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    // A fresh installation opens in the useful full-workspace layout. Existing settings still
    // preserve the user's later choice through SaveSettings().
    public bool Maximised { get; set; } = true;
    // The Settings window's size in DIPs, remembered across restarts. Files written before this
    // existed load the ChromeMetrics defaults; the window writes its bounds back here as it is resized.
    public double SettingsWindowWidth { get; set; } = ChromeMetrics.SettingsWindowWidth;
    public double SettingsWindowHeight { get; set; } = ChromeMetrics.SettingsWindowHeight;
    // The Mixer window's size in DIPs, remembered the same way (its default is smaller than the first size it opened at).
    public double MixerWindowWidth { get; set; } = 720;
    public double MixerWindowHeight { get; set; } = 480;

    // ---- categories ----
    /// <summary>App-wide mixer group rules: every song without its own rules uses these (null groups = Guitars, Basses, Drums, Other instruments).</summary>
    public MixerAppRules MixerRules { get; set; } = new();
    public TabSettings Tabs { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    /// <summary>Audio output device and VST plug-in hosting.</summary>
    public PluginSettings Plugins { get; set; } = new();
    public EditingSettings Editing { get; set; } = new();
    /// <summary>Last-used File > Render options.</summary>
    public Rendering.RenderSettings Render { get; set; } = new();
    /// <summary>Last-used File > Export MIDI options.</summary>
    public Rendering.MidiExportSettings MidiExport { get; set; } = new();
    /// <summary>Last-used File > Export > Video options.</summary>
    public Video.VideoSettings Video { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public FollowSettings Follow { get; set; } = new();
    public TimelineSettings Timeline { get; set; } = new();
    /// <summary>Live video recording (the Record video button).</summary>
    public Video.LiveVideoSettings LiveVideo { get; set; } = new();
    /// <summary>The transport bar's master level meter: its reading, number and peak hold.</summary>
    public TabForge.Audio.Metering.MasterMeterSettings MasterMeter { get; set; } = new();
    /// <summary>The track rows' level meters: the same choices, the apply-to-all flag and the pre- or post-fader tap.</summary>
    public TabForge.Audio.Metering.TrackMeterSettings TrackMeter { get; set; } = new();
    /// <summary>Keyboard mode (experimental): look-ahead and note size.</summary>
    public KeyboardMode.KeyboardModeSettings Learn { get; set; } = new();
    /// <summary>Dock tree, selected tabs, closed panels, and floating-window bounds.</summary>
    public DockWorkspaceState? Workspace { get; set; }
    /// <summary>The layout as it was when the side panel was hidden, so showing it again restores it exactly (even after a restart).</summary>
    public DockWorkspaceState? WorkspaceBeforeSideHide { get; set; }
    /// <summary>User-saved workspace layouts (a saved layout named like a built-in one replaces it). Never holds song data.</summary>
    public List<SavedLayout> SavedLayouts { get; set; } = new();
    /// <summary>Name of the layout last switched to (built-in or saved); empty when none.</summary>
    public string? LastLayout { get; set; }
    /// <summary>User presets of the note-effect editors (bend, tremolo bar...), keyed by editor kind; built-in presets are not stored.</summary>
    public List<EffectPresetEntry> EffectPresets { get; set; } = new();
    /// <summary>Preferred score layout for newly opened tabs; existing tabs retain their own view state.</summary>
    /// <summary>The folder each open, save and browse dialog last used, by its key (see DialogKeys): the dialog starts there.</summary>
    public Dictionary<string, string> RememberedFolders { get; set; } = new();
    public bool PreferredContinuousScoreView { get; set; } = true;
    public bool PreferredHorizontalScoreView { get; set; }
}

/// <summary>One user preset of a note-effect editor: a curve (bend, tremolo bar) and/or named numbers (trill, grace note, harmonic).</summary>
public sealed class EffectPresetEntry
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public List<TabForge.Models.BendPointModel> Points { get; set; } = new();
    public Dictionary<string, double> Values { get; set; } = new();
}

/// <summary>A named workspace layout: dock placement/sizes/visibility plus window state.</summary>
public sealed class SavedLayout
{
    public string Name { get; set; } = "";
    public DockWorkspaceState? State { get; set; }
    public bool Maximised { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
}
