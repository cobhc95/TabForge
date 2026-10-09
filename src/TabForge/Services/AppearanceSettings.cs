using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the Appearance and score-look preferences data and the converter for older null values.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public sealed class AppearanceSettings
{
    /// <summary>Track rows and timeline lanes tinted with the track's colour, in percent (0 = off).</summary>
    public int TrackTintPercent { get; set; } = 20;
    /// <summary>How strongly a muted track is greyed in the track list and the timeline, in percent (0 = not dimmed).</summary>
    public int MutedTrackDimPercent { get; set; } = 80;
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
    /// <summary>Engrave dynamics markings (ppp..fff) under the staff where the dynamic changes.</summary>
    public bool ShowDynamics { get; set; } = true;
    public bool ShowScoreBarNumbers { get; set; } = true;
    public int ScoreBarNumberFrequency { get; set; } = 1;
    public double SystemVerticalSpacing { get; set; } = 1.0;
    public double MeasureHorizontalSpacing { get; set; } = 1.0;
    /// <summary>Opacity of the staff lines and the ledger lines (one unit, 0..1); 1.0 shows the staff-line colour as chosen.</summary>
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
    /// <summary>The fretboard / keyboard pane sits below the score (above the timeline) instead of above it.</summary>
    public bool FretboardAtBottom { get; set; }
    /// <summary>1 once the lock default was changed to unlocked; files without it get a stored lock reset once (it was the old default).</summary>
    public int InstrumentLockVersion { get; set; } = 1;
    /// <summary>The locked pane height (instrument content, DIPs); 0 = take the current height when first locked.</summary>
    public double InstrumentPaneHeight { get; set; }
    /// <summary>The locked keyboard pane height (DIPs, 60-120 in practice); kept apart from the fretboard's so each view restores its own; 0 = not set yet.</summary>
    public double KeyboardPaneHeight { get; set; }
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
