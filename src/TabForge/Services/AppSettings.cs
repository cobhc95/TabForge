using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

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
    public double ZoomFactor { get; set; }
    public bool Metronome { get; set; }
    public bool CountIn { get; set; }
    public double Speed { get; set; } = 1.0;
    public double WindowWidth { get; set; } = 1500;
    public double WindowHeight { get; set; } = 960;
    // A fresh installation opens in the useful full-workspace layout. Existing settings still
    // preserve the user's later choice through SaveSettings().
    public bool Maximised { get; set; } = true;

    // ---- categories ----
    public TabSettings Tabs { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    /// <summary>Audio output device and VST plug-in hosting.</summary>
    public PluginSettings Plugins { get; set; } = new();
    public EditingSettings Editing { get; set; } = new();
    /// <summary>Last-used File > Render options.</summary>
    public Rendering.RenderSettings Render { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public FollowSettings Follow { get; set; } = new();
    public TimelineSettings Timeline { get; set; } = new();
    /// <summary>Dock tree, selected tabs, closed panels, and floating-window bounds.</summary>
    public DockWorkspaceState? Workspace { get; set; }
    /// <summary>The layout as it was when the side panel was hidden, so showing it again restores it exactly (even after a restart).</summary>
    public DockWorkspaceState? WorkspaceBeforeSideHide { get; set; }
    /// <summary>User-saved workspace layouts (a saved layout named like a built-in one replaces it). Never holds song data.</summary>
    public List<SavedLayout> SavedLayouts { get; set; } = new();
    /// <summary>Name of the layout last switched to (built-in or saved); empty when none.</summary>
    public string? LastLayout { get; set; }
    /// <summary>Preferred score layout for newly opened tabs; existing tabs retain their own view state.</summary>
    public bool PreferredContinuousScoreView { get; set; } = true;
    public bool PreferredHorizontalScoreView { get; set; }
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

/// <summary>
/// Keeping the score under the player's eyes while they play. Modelled on TuxGuitar's
/// TablatureScrollPlaying / TGControl: a jump mode with anticipation, a smooth mode paced to the
/// music, no scrolling once the end is already on screen, and "stop following if the user scrolls".
/// </summary>
public sealed class FollowSettings
{
    /// <summary>"Off", "Jump" or "Smooth".</summary>
    public string Mode { get; set; } = FollowModes.Smooth;
    /// <summary>Where the played system is parked, as a percentage of the viewport height from the top.</summary>
    public int MarginPercent { get; set; } = 20;
    /// <summary>System edge threshold as a percentage of the score viewport.</summary>
    public int VerticalTriggerPercent { get; set; } = 80;
    /// <summary>Follow score movement horizontally while playback is active.</summary>
    public bool HorizontalFollow { get; set; } = true;
    /// <summary>Follow score movement vertically while playback is active.</summary>
    public bool VerticalFollow { get; set; } = true;
    /// <summary>Bars of look-ahead required to stay visible in jump mode (TuxGuitar: 1).</summary>
    public int AnticipationBars { get; set; } = 1;
    /// <summary>Refresh ceiling for smooth scrolling, in frames per second (TuxGuitar: 40).</summary>
    /// <summary>Follow-scroll frame-rate cap; 240 = match the display refresh (default).</summary>
    public int MaxFps { get; set; } = 240;
    /// <summary>Follow style: false = page turn (instant half-screen / next-line jump, default); true = the same turns, glided.</summary>
    public bool ContinuousScroll { get; set; }
    /// <summary>Stop following when the user scrolls by hand; following resumes on the next transport action.</summary>
    public bool StopOnManualScroll { get; set; } = true;
    /// <summary>Do not scroll once the last system (or the loop end) is already fully visible.</summary>
    public bool StopAtEnd { get; set; } = true;
    /// <summary>Tint the beat that is currently sounding.</summary>
    public bool HighlightPlayedBeat { get; set; } = true;
    /// <summary>Show active note duration shading behind the playhead.</summary>
    public bool DurationTintEnabled { get; set; } = false;
    /// <summary>Colour of the sounding note heads, fret numbers and playhead.</summary>
    public string HighlightColour { get; set; } = "#3FB950";
    /// <summary>Background tint behind the sounding beat (TuxGuitar tints the played beat too).</summary>
    public string HighlightBackground { get; set; } = "#1E3A2A";
    /// <summary>Independent colour of the vertical playback cursor.</summary>
    public string PlayheadColour { get; set; } = "#3FB950";
    /// <summary>Colour of the active-note duration glow.</summary>
    public string DurationGlowColour { get; set; } = "#3FB950";
    /// <summary>Opacity of duration shading and optional beat tint (0..1); zero disables both fills.</summary>
    public double DurationGlowOpacity { get; set; } = 0;
    /// <summary>Intensity of hover/active section glows (0..1).</summary>
    public double SectionGlowIntensity { get; set; } = 0.45;
    public double PlayheadThickness { get; set; } = 1.7;
    /// <summary>Scroll the fretboard so the played position stays visible.</summary>
    public bool FollowFretboard { get; set; } = true;
}

public static class FollowModes
{
    public const string Off = "Off";
    public const string Jump = "Jump";
    public const string Smooth = "Smooth";
}

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
    /// <summary>Whether the playback controller is docked into the right sidebar.</summary>
    public bool PlaybackControllerDocked { get; set; } = true;
    /// <summary>Last floating playback-controller X position in device-independent workspace units.</summary>
    public double PlaybackControllerX { get; set; } = 20;
    /// <summary>Last floating playback-controller Y position; a negative value selects the sheet-bottom default.</summary>
    public double PlaybackControllerY { get; set; } = -1;
    /// <summary>Height of the playback controller when docked, in device-independent units.</summary>
    public double PlaybackControllerHeight { get; set; } = 100;
}

public sealed class AppearanceSettings
{
    /// <summary>Track rows and timeline lanes tinted with the track's colour, in percent (0 = off).</summary>
    public int TrackTintPercent { get; set; } = 20;
    /// <summary>Colour per mixer group (Guitars, Basses, Drums...) for "Colour tracks by group"; missing = default.</summary>
    public Dictionary<string, string> GroupColours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecentColours { get; set; } = new();
    /// <summary>Dark, Light, System, or Custom (retains explicit palette fields).</summary>
    public string ThemeMode { get; set; } = "Dark";
    /// <summary>1 once the Dark/Light preset palette has been written into the colour fields (older settings
    /// kept hard-coded theme colours instead).</summary>
    public int ThemePresetVersion { get; set; }
    public double UiScale { get; set; } = 1.0;
    public bool ReduceAnimations { get; set; }
    public double AnimationSpeed { get; set; } = 1.0;
    public string SelectionColour { get; set; } = "#4C9AFF";
    public string HoverColour { get; set; } = "#98A1AE";
    public string Accent { get; set; } = "#4C9AFF";
    public string Background { get; set; } = "#14161A";
    public string Panel { get; set; } = "#1C1F24";
    public string TitleBarColour { get; set; } = "#161616";
    public string ActiveTabColour { get; set; } = "#333333";
    public string TabHoverColour { get; set; } = "#292929";
    public string Text { get; set; } = "#E7EAEF";
    public string Muted { get; set; } = "#98A1AE";
    public string DarkScorePaperColour { get; set; } = "#15181D";
    public string LightScorePaperColour { get; set; } = "#E6E6E6"; // soft grey, matching the light grey theme
    public string DarkScoreInkColour { get; set; } = "#E7EAEF";
    public string LightScoreInkColour { get; set; } = "#111111";
    public string DarkScoreLinesColour { get; set; } = "#343940";
    public string LightScoreLinesColour { get; set; } = "#B2B2B2";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 12;
    /// <summary>Shared typeface for text rendered on the score page.</summary>
    public string ScoreFontFamily { get; set; } = "Segoe UI";
    /// <summary>Per-area score text overrides keyed by area name (Header, BarInfo, Fret, Technique, Chord, Lyrics, General).</summary>
    public Dictionary<string, ScoreTextAreaStyle> ScoreTextAreas { get; set; } = new();
    /// <summary>Base score text size; does not change page or bar layout.</summary>
    public double ScoreTextSize { get; set; } = 13.5;
    public bool ScoreTextBold { get; set; }
    public bool ScoreTextItalic { get; set; }
    /// <summary>Icon glyph size in the toolbar and title bar, in device-independent pixels.</summary>
    public double IconSize { get; set; } = 14;
    /// <summary>Stroke width of the active-section brackets in the arrangement, in device-independent pixels.</summary>
    public double SectionBracketThickness { get; set; } = 10;
    /// <summary>Scale applied to the tablature line spacing and fret numbers (readability on stage).</summary>
    public double ScoreSpacing { get; set; } = 1.0;
    /// <summary>"Standard", "Minimal" or "Hidden" score ledger lines.</summary>
    public string LedgerLines { get; set; } = "Minimal";
    /// <summary>"Compact" tightens toolbar and row heights.</summary>
    public string Density { get; set; } = "Comfortable";
    /// <summary>Show icon glyphs on the toolbar (off = text-only buttons).</summary>
    public bool ShowToolbarIcons { get; set; } = true;
    /// <summary>Score paper: "Dark" or "Light".</summary>
    public string ScorePaper { get; set; } = "Dark";
    /// <summary>Palette tool ids pinned to the menu-row toolbar, in order.</summary>
    public List<string> PinnedTools { get; set; } = new();
    public bool ShowSectionHeadings { get; set; } = true;
    public bool ShowScoreBarNumbers { get; set; } = true;
    public int ScoreBarNumberFrequency { get; set; } = 1;
    public double SystemVerticalSpacing { get; set; } = 1.0;
    public double MeasureHorizontalSpacing { get; set; } = 1.0;
    public double LedgerLineOpacity { get; set; } = 1.0;
    public double StaffLineOpacity { get; set; } = 1.0;
    public double HoverHighlightIntensity { get; set; } = 0.27;
    public double SelectionHighlightIntensity { get; set; } = 0.25;
    public string PlayheadColour { get; set; } = "#3FB950";
    public string CursorColour { get; set; } = "#F2C14E";
    /// <summary>Thumb colour for the arrangement timeline scrollbar.</summary>
    public string TimelineScrollBarThumbColour { get; set; } = "#758396";
    /// <summary>Show the fretboard / instrument panel.</summary>
    public bool ShowFretboard { get; set; } = true;
    /// <summary>Fretboard / keyboard pane height is locked (splitter drags do not change it).</summary>
    public bool LockInstrumentSize { get; set; }
    /// <summary>1 once the lock default was changed to unlocked; files without it get a stored lock reset once (it was the old default).</summary>
    public int InstrumentLockVersion { get; set; } = 1;
    /// <summary>The locked pane height (instrument content, DIPs); 0 = take the current height when first locked.</summary>
    public double InstrumentPaneHeight { get; set; }
    /// <summary>Persisted horizontal fretboard snap position: Left, Centre or Right.</summary>
    public string FretboardPosition { get; set; } = "Centre";
    /// <summary>Show the arrangement overview.</summary>
    public bool ShowArrangementOverview { get; set; } = true;
    /// <summary>Show the document tab strip in the title bar.</summary>
    public bool ShowTabStrip { get; set; } = true;
}

/// <summary>Reads a bool that older settings files stored as null (auto) as true.</summary>
public sealed class NullIsTrueBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.TokenType is System.Text.Json.JsonTokenType.Null or System.Text.Json.JsonTokenType.True;

    public override bool HandleNull => true;

    public override void Write(System.Text.Json.Utf8JsonWriter writer, bool value, System.Text.Json.JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

/// <summary>Audio engine (plug-in playback) and plug-in folders. Windows MIDI playback does not use these.</summary>
public sealed class PluginSettings
{
    /// <summary>"WASAPI (shared)", "WASAPI (exclusive)", "ASIO" or "DirectSound".</summary>
    public string Driver { get; set; } = AudioDrivers.WasapiShared;
    /// <summary>Audio input (recording) device name; empty = the Windows default recording device.</summary>
    public string InputDevice { get; set; } = "";
    /// <summary>Output device name; empty = the Windows default device.</summary>
    public string Device { get; set; } = "";
    public int SampleRate { get; set; } = 48000;
    /// <summary>ASIO ignores the Windows volume; on: the Windows master volume and mute also scale TabForge's audio (off by default).</summary>
    public bool FollowWindowsVolume { get; set; } = true;
    /// <summary>Play every track through the audio engine (its General MIDI synth) so the song uses the chosen audio driver. Off: tracks without plug-ins use Windows MIDI. Older files stored null (auto): read as on.</summary>
    [JsonConverter(typeof(NullIsTrueBoolConverter))]
    public bool PlayAllThroughEngine { get; set; } = true;
    /// <summary>Retired (kept so old files load): the engine is on by default and no rule switches it on or off any more.</summary>
    public bool AutoPlayThroughEngine { get; set; } = true;
    /// <summary>True while "play the whole song through the engine" is on because the automatic rule turned it on (not the user): only then does removing the last plug-in (without ASIO) turn it off again.</summary>
    public bool PlayAllSetAutomatically { get; set; }
    /// <summary>App-wide monitoring effects chain (speaker / room calibration), used by every song that keeps "Use for all projects" ticked. Live output only: never rendered or exported.</summary>
    public TabForge.Models.BusChain MonitorFx { get; set; } = new();
    /// <summary>Tick a track's GM sound automatically when no VST instrument plays it (chain off, instrument bypassed / removed), untick when one does; manual unticks are respected.</summary>
    public bool AutoGmSound { get; set; } = true;
    /// <summary>Output latency of the Windows MIDI synth (ms); plug-in tracks are delayed by the difference so both stay in time.</summary>
    public int WindowsMidiLatencyMs { get; set; } = 200;
    /// <summary>1 once the old guessed default of 60 ms has been moved to the measured 200 ms (a stored 60 in an older file is that guess; any other value is the user's own).</summary>
    public int WindowsMidiLatencyVersion { get; set; } = 1;
    /// <summary>
    /// Recording offset in ms (a manual input offset), on top of the input latency the device reports: positive moves takes
    /// earlier (use it when recordings sound late), negative later. -1000..1000; 0 by default.
    /// </summary>
    public int RecordingOffsetMs { get; set; }
    /// <summary>ASIO: use the driver's inputs (recording and monitoring).</summary>
    public bool AsioInputsEnabled { get; set; } = true;
    /// <summary>ASIO: first input channel (0-based).</summary>
    public int AsioInputChannel { get; set; }
    /// <summary>ASIO: last input channel (0-based; equal to the first = one mono input, as a guitar on input 2).</summary>
    public int AsioInputLastChannel { get; set; } = 1;
    /// <summary>ASIO: first output channel (0-based).</summary>
    public int AsioOutputChannel { get; set; }
    /// <summary>ASIO: last output channel (0-based; equal to the first = one mono output, the mix summed to it).</summary>
    public int AsioOutputLastChannel { get; set; } = 1;
    /// <summary>Buffer size in samples per channel.</summary>
    public int BufferSize { get; set; } = 256;
    /// <summary>Folders the user added (browsed to) that hold plug-ins.</summary>
    public List<string> Folders { get; set; } = new();
    /// <summary>Also scan the standard VST folders (off: only the folders above, as in Equalizer APO).</summary>
    public bool ScanStandardFolders { get; set; }
    /// <summary>The "common folders" the Add plug-in window's "Scan common folders" (and the standard-folders option) scan; may use %ENV% variables. Empty = the built-in list (<see cref="TabForge.Plugins.VstScannerService.DefaultCommonFolders"/>).</summary>
    public List<string> CommonFolders { get; set; } = new();
    /// <summary>Run each plug-in in its own process (safest, uses more CPU and memory). Off: one shared engine.</summary>
    public bool SeparateProcessPerPlugin { get; set; }
    /// <summary>Plug-ins switched off after they crashed (full paths).</summary>
    public List<string> Quarantined { get; set; } = new();
    /// <summary>Plug-in paths the user explicitly approved although they are not from their scan (full normalised paths; UNC/removable need this).</summary>
    public List<string> ApprovedPluginPaths { get; set; } = new();
    /// <summary>Opt-in: scan the plug-in folders once and remember the list (Rescan updates it). Off: scan each time.</summary>
    public bool RememberScan { get; set; }
    /// <summary>The remembered plug-in list (when <see cref="RememberScan"/> is on).</summary>
    public List<KnownPlugin> ScanCache { get; set; } = new();
    /// <summary>Roles / vendors found by probing plug-ins, kept so each is probed once.</summary>
    public List<KnownPlugin> Probed { get; set; } = new();
    /// <summary>Fingerprints of scanned and approved plug-ins in user-writable locations (see <see cref="TabForge.Plugins.PluginTrust"/>): a file that changed since is only trusted again when it has the same Authenticode signer.</summary>
    public List<PluginFingerprint> TrustRecords { get; set; } = new();
    /// <summary>Show the plug-in's window inside the FX chain window (on) or floating on its own.</summary>
    public bool DockPluginWindows { get; set; } = true;
    /// <summary>Floating plug-in windows stay above other windows.</summary>
    public bool PluginWindowsOnTop { get; set; }
    /// <summary>Default FX chains per instrument type, applied to tracks of that type ("Auto-load for this instrument").</summary>
    public List<TabForge.Plugins.AutoChain> AutoChains { get; set; } = new();
    /// <summary>Chains added as a (never armed) track to every song that is opened or created ("Add as a track on startup").</summary>
    public List<TabForge.Plugins.StartupTrack> StartupTracks { get; set; } = new();
    /// <summary>Default for every chain: measure each VST instrument's sounding octave and transpose it to match the notes (per chain override in the FX window).</summary>
    public bool AutoPitchMatch { get; set; } = true;
}

public sealed class KnownPlugin
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Format { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Role { get; set; } = "";
}

/// <summary>What a plug-in file looked like when the user scanned or approved it (full normalised plug-in path; for a VST3 bundle the size, time and hash are of its x64 binary).</summary>
public sealed class PluginFingerprint
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long LastWriteUtcTicks { get; set; }
    /// <summary>Hex SHA-256 of the binary; empty = the file did not exist when approved (any file appearing there counts as changed).</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>Subject of the valid Authenticode signer (WinVerifyTrust), empty when unsigned or the signature does not verify.</summary>
    public string Signer { get; set; } = "";
    /// <summary>Recorded by a scan (trusted while the file is unchanged); false = recorded by an explicit approval only.</summary>
    public bool Scanned { get; set; }
}

public static class AudioDrivers
{
    public const string WasapiShared = Audio.Contracts.AudioDriverNames.WasapiShared;
    public const string WasapiExclusive = Audio.Contracts.AudioDriverNames.WasapiExclusive;
    public const string Asio = Audio.Contracts.AudioDriverNames.Asio;
    public const string DirectSound = Audio.Contracts.AudioDriverNames.DirectSound;
    public static readonly string[] All = Audio.Contracts.AudioDriverNames.All;
    public static readonly int[] SampleRates = { 44100, 48000, 88200, 96000 };
    /// <summary>Common buffer sizes offered in the list; any value from <see cref="MinBuffer"/> to <see cref="MaxBuffer"/> can be typed.</summary>
    public static readonly int[] BufferSizes = { 16, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 2048, 4096, 8192 };
    public const int MinBuffer = 16, MaxBuffer = 8192;
}

public sealed class AudioSettings
{
    public bool Metronome { get; set; }
    public bool CountIn { get; set; }
    public int CountInBars { get; set; } = 1;
    public double Speed { get; set; } = 1.0;
    public bool PreviewNotes { get; set; } = true;
    /// <summary>How long a previewed note sounds, in milliseconds.</summary>
    public int PreviewLengthMs { get; set; } = 260;
    /// <summary>Let-ring tail ceiling in milliseconds.</summary>
    public int LetRingCapMs { get; set; } = 2000;
    /// <summary>GM note used for the metronome click.</summary>
    public int MetronomeClick { get; set; } = 34;
    /// <summary>GM note used for the accented (first beat) click.</summary>
    public int MetronomeAccent { get; set; } = 33;
    /// <summary>Master metronome output level, 0–100 percent.</summary>
    public int MetronomeVolume { get; set; } = 70;
    /// <summary>Layer extra percussion hits on every click so it is much louder than the song.</summary>
    public bool MetronomeBoost { get; set; } = true;
    public int CountInVolume { get; set; } = 70;
    /// <summary>"same" or "accent,click" MIDI notes for the count-in.</summary>
    public string CountInSound { get; set; } = "same";
    public bool CountInOnlyAtSongStart { get; set; }
    public bool CountInEachSection { get; set; }
    /// <summary>Clicking Loop with no selected area loops the section being played (off: loops the whole song).</summary>
    public bool LoopButtonLoopsSection { get; set; }
    /// <summary>Loop repeats; 0 = infinite.</summary>
    public int LoopCount { get; set; }
    public bool LoopCountInEachLoop { get; set; }
    public bool LoopSpeedTrainer { get; set; }
    public int LoopTrainerFrom { get; set; } = 50;
    public int LoopTrainerTo { get; set; } = 100;
    public int LoopTrainerStep { get; set; } = 10;
    /// <summary>First-beat volume, 0–100 percent.</summary>
    public int MetronomeAccentVolume { get; set; } = 100;
    /// <summary>Regular click volume, 0–100 percent.</summary>
    public int MetronomeClickVolume { get; set; } = 76;
    /// <summary>Clicks per beat: 1, 2, 3 (triplets), or 4.</summary>
    public int MetronomeSubdivision { get; set; } = 1;
    /// <summary>What the loop button loops when no area is selected: "Section", "Bar" or "Song".</summary>
    public string LoopDefaultScope { get; set; } = "Section";
    /// <summary>Forget the selected loop area when looping is switched off.</summary>
    public bool LoopClearAreaOnDisable { get; set; } = true;
    /// <summary>Fretboard preview layout: "TabForge" (fixed look-ahead) or one of the "the reference: …" beat/bar layouts.</summary>
    public string FretboardStyle { get; set; } = "TabForge";
    /// <summary>Master volume applied on top of every track's volume, 0–100 percent.</summary>
    public int MasterVolume { get; set; } = 100;
    /// <summary>What the master volume knob scales.</summary>
    public bool MasterAffectsTracks { get; set; } = true;
    public bool MasterAffectsMetronome { get; set; }
    public bool MasterAffectsPreview { get; set; }
    /// <summary>Arrangement pan controls: rotary knobs (true) or horizontal sliders.</summary>
    public bool PanKnobs { get; set; }
    /// <summary>One-time switch of existing settings to the standard pan slider.</summary>
    public bool PanSliderAdopted { get; set; }
    public bool VolumeKnobs { get; set; }
}

public sealed class EditingSettings
{
    public int DefaultDuration { get; set; } = 4;
    /// <summary>Distance moved by one standard mouse-wheel notch over the score page.</summary>
    public int ScoreWheelScrollPixels { get; set; } = 32;
    public bool ShowNoteNames { get; set; }
    public bool LeftHanded { get; set; }
    /// <summary>What the instrument panel draws: "Match the instrument" (out of the box: a fretboard with the
    /// track's strings for stringed instruments, drum pads for drums, a keyboard for everything else), or
    /// always a Fretboard / Keyboard / Drums.</summary>
    public string InstrumentView { get; set; } = InstrumentViews.MatchInstrument;
    /// <summary>1 once the "match the instrument" default replaced the earlier fixed-fretboard default.</summary>
    public int InstrumentViewVersion { get; set; } = 1;
    /// <summary>Keys on the keyboard view: 88 (full piano, default), 76, 61, 49, 37 or 25.</summary>
    public int KeyboardKeys { get; set; } = 88;
    /// <summary>Keyboard key colours: "Match the theme" (grey keys in dark, white in light), "Grey keys" or "White keys".</summary>
    public string KeyboardKeyColours { get; set; } = KeyboardKeyStyles.MatchTheme;
    /// <summary>How scale notes are marked: "Shaded" (default), "Circles" or "Rings".</summary>
    public string ScaleHighlightStyle { get; set; } = ScaleHighlightStyles.Shaded;
    /// <summary>Colour of the scale highlight (see <see cref="ScaleHighlightStyles.Colours"/>).</summary>
    public string ScaleHighlightColour { get; set; } = "Blue";
    /// <summary>Colour of the fretboard position dots (inlays): "Default" follows the theme.</summary>
    public string FretMarkerColour { get; set; } = "Default";
    /// <summary>Brightness of the fretboard position dots: "Original", "Brighter" (default), "Bright" or "Brightest".</summary>
    public string FretMarkerBrightness { get; set; } = FretMarkerLevels.Brighter;
    /// <summary>Size of the fret numbers and note bubbles: "Small" (75%), "Medium" (85%, default) or "Large" (100%).</summary>
    public string FretNumberSize { get; set; } = FretNumberSizes.Medium;
    /// <summary>Fretboard string spacing relative to the fret width: "Compact", "Natural" (default) or "Wide" (at most 1.5x natural).</summary>
    public string FretStringSpacing { get; set; } = FretStringSpacings.Natural;
    public int FretboardFrets { get; set; } = 24;
    public int PreviewHorizon { get; set; } = 6;
    public bool PreviewNotesEnabled { get; set; } = true;
    public string? ScaleHighlight { get; set; }
    /// <summary>Ask before deleting a bar.</summary>
    public bool ConfirmDeleteBar { get; set; }
    /// <summary>Move the caret forward by the entered note's value.</summary>
    public bool AutoAdvance { get; set; } = true;
    /// <summary>false: + shortens, - lengthens. true: + lengthens, - shortens.</summary>
    public bool ReversePlusMinusDuration { get; set; }
    public bool PreventBarOverflow { get; set; }
}

/// <summary>Timeline and section presentation/interaction preferences.</summary>
/// <summary>Text style override for one area of the score (null / 0 = inherit the global score text style).</summary>
public sealed class ScoreTextAreaStyle
{
    public string? Font { get; set; }
    public double SizePercent { get; set; } = 100;
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public string? Colour { get; set; }
    public string? OutlineColour { get; set; } = "#000000";
    public double OutlineThickness { get; set; }
}

/// <summary>Snapping of audio and MIDI clips on the timeline.</summary>
public sealed class SnapSettings
{
    public bool Enabled { get; set; }
    /// <summary>Grid size: Bar, 1/2, 1/4, 1/8, 1/16 or 1/32 (note values; 1/4 = one beat in 4/4).</summary>
    public string Grid { get; set; } = "1/4";
    public static readonly string[] Grids = { "Bar", "1/2", "1/4", "1/8", "1/16", "1/32" };
    public bool ToGrid { get; set; } = true;
    public bool ToItems { get; set; } = true;
    public bool ToPlayhead { get; set; } = true;
    /// <summary>Snap to the grid however far away it is (else only within <see cref="DistancePx"/>).</summary>
    public bool GridAtAnyDistance { get; set; } = true;
    /// <summary>How near (screen pixels) an edge must be to snap to another item or the playhead.</summary>
    public int DistancePx { get; set; } = 8;
}

public sealed class TimelineSettings
{
    /// <summary>Snap settings for moving and trimming clips.</summary>
    public SnapSettings Snap { get; set; } = new();
    /// <summary>Clicking the tuning-fork icon opens the global tuning window (otherwise the type-in box).</summary>
    public bool TuningIconOpensWindow { get; set; } = true;
    public bool ShowSectionNames { get; set; } = true;
    public bool ShowBarNumbers { get; set; } = true;
    public bool SectionDragAnimation { get; set; } = true;
    /// <summary>Show the [ ] brackets around the current section on the timeline.</summary>
    public bool ShowSectionBrackets { get; set; } = true;
    /// <summary>Give sections with the same base name ("Verse 1" / "Verse 2") the same colour.</summary>
    public bool MatchSimilarSectionColours { get; set; } = true;
    /// <summary>Order of the arrangement track-control columns (null = default order).</summary>
    public List<string>? TrackColumnOrder { get; set; }
    /// <summary>Track columns hidden from the track list (only "volume" and "pan" can be hidden; set in the Mixer).</summary>
    public List<string> HiddenTrackColumns { get; set; } = new();
    /// <summary>Widths of the fixed-width track-control columns by id (null = defaults).</summary>
    public Dictionary<string, double>? TrackColumnWidths { get; set; }
    /// <summary>Width of the whole track-controls area; 0 = default.</summary>
    public double TrackControlsWidth { get; set; }
    /// <summary>Resize the track list / arrangement panel to fit all rows when tracks or groups change.</summary>
    public bool AutoFitTrackList { get; set; } = true;
}

/// <summary>Action id → key gesture string (e.g. "Ctrl+Shift+T"). Missing ids use the catalog default.</summary>
public sealed class HotkeySettings
{
    /// <summary>Base key layout (see HotkeyPresets); Bindings hold the user's own changes on top.</summary>
    public string Preset { get; set; } = "TabForge";
    public Dictionary<string, string> Bindings { get; set; } = new();
    /// <summary>Commands explicitly unbound by the user; absence from Bindings otherwise means default.</summary>
    public List<string> DisabledActions { get; set; } = new();

    [JsonIgnore]
    public string this[string action]
    {
        get => Bindings.TryGetValue(action, out var v) ? v : "";
        set
        {
            DisabledActions.RemoveAll(id => id.Equals(action, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(value)) Bindings.Remove(action);
            else Bindings[action] = value;
        }
    }

    public bool IsDisabled(string action) => DisabledActions.Contains(action, StringComparer.OrdinalIgnoreCase);
    public void Disable(string action)
    {
        Bindings.Remove(action);
        if (!IsDisabled(action)) DisabledActions.Add(action);
    }
    public void Reset(string action)
    {
        Bindings.Remove(action);
        DisabledActions.RemoveAll(id => id.Equals(action, StringComparison.OrdinalIgnoreCase));
    }
}

public static class InstrumentViews
{
    public const string Fretboard = "Fretboard";
    public const string Keyboard = "Keyboard";
    public const string Drums = "Drums";
    public const string MatchInstrument = "Match the instrument";
    public static readonly string[] All = { MatchInstrument, Fretboard, Keyboard, Drums };
    public static readonly int[] KeyboardSizes = { 88, 76, 61, 49, 37, 25 };
}

public static class ScaleHighlightStyles
{
    public const string Shaded = "Shaded";
    public const string Circles = "Circles";
    public const string Rings = "Rings";
    public static readonly string[] All = { Shaded, Circles, Rings };
    /// <summary>Colour names; the colours themselves are <see cref="ThemeService.ScaleHighlightColour"/> (the settings types stay WPF-free).</summary>
    public static readonly string[] Colours = { "Blue", "Green", "Amber", "Purple", "Red", "Teal", "Grey" };
}

public static class FretMarkerLevels
{
    public const string Original = "Original";
    public const string Brighter = "Brighter";
    public static readonly string[] All = { Original, Brighter, "Bright", "Brightest" };
    public static readonly string[] Colours = { "Default", "White", "Silver", "Amber", "Blue", "Green" };

    /// <summary>0 = the original dim dots, rising to 1 = brightest.</summary>
    public static double Level(string? level) => level switch { "Original" => 0, "Bright" => 0.6, "Brightest" => 1, _ => 0.3 };
    // The colours for Colours are ThemeService.FretMarkerColour.
}

public static class FretNumberSizes
{
    public const string Small = "Small";
    public const string Medium = "Medium";
    public const string Large = "Large";
    public static readonly string[] All = { Small, Medium, Large };
    public static string Label(string size) => size switch { Small => "Small (75%)", Large => "Large (100%)", _ => "Medium (85%, default)" };
    public static double Scale(string? size) => size switch { Small => 0.75, Large => 1.0, _ => 0.85 };
}

/// <summary>How far apart the fretboard strings may be drawn: a cap on the string gap relative to the fret width, so a tall or
/// narrow pane never stretches the board (see <see cref="TabForge.Visualization.FretboardGeometry"/>).</summary>
public static class FretStringSpacings
{
    public const string Compact = "Compact";
    public const string Natural = "Natural";
    public const string Wide = "Wide";
    public static readonly string[] All = { Compact, Natural, Wide };
    public static string Label(string v) => v switch { Compact => "Compact (75%)", Wide => "Wide (150%, the maximum)", _ => "Natural (default)" };
    /// <summary>Multiplier of the natural string-gap cap; never above 1.5.</summary>
    public static double Factor(string? v) => v switch { Compact => 0.75, Wide => 1.5, _ => 1.0 };
    public static string Next(string? v) => v switch { Compact => Natural, Natural => Wide, _ => Compact };
}

public static class KeyboardKeyStyles
{
    public const string MatchTheme = "Match the theme";
    public const string Grey = "Grey keys";
    public const string White = "White keys";
    public static readonly string[] All = { MatchTheme, Grey, White };
}
