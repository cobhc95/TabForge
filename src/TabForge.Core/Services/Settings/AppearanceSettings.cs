using System.Text.Json.Serialization;
using TabForge.Platform.Theme;

namespace TabForge.Services;

// Owns: the Appearance and score-look preferences data and the converter for older null values.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestSettingsFileSplitSnapshots.
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
    /// <summary>The colour settings the user set explicitly (Accent, Panel...), comma separated: a theme preset leaves them as they are, so a custom accent survives a Dark/Light switch.
    /// Reset to preset clears it. A string, so a copy of the settings never shares it.</summary>
    /// <summary>The accent before the switch to Light lightened it (empty = not lightened); switching back to Dark restores it.</summary>
    public string AccentBeforeLight { get; set; } = "";
    public string OverriddenColours { get; set; } = "";
    public bool IsOverridden(string colour) => OverriddenColours.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(colour);
    /// <summary>Marks <paramref name="colour"/> as set by the user and returns <paramref name="value"/>, so a setter can write <c>a.Accent = a.Own("Accent", v)</c>.</summary>
    public string Own(string colour, string value)
    {
        if (!IsOverridden(colour)) OverriddenColours = string.Join(',', OverriddenColours.Split(',', StringSplitOptions.RemoveEmptyEntries).Append(colour));
        return value;
    }
    public double UiScale { get; set; } = 1.0;
    public bool ReduceAnimations { get; set; }
    public double AnimationSpeed { get; set; } = 1.0;
    public string SelectionColour { get; set; } = Palette.Hex("Selection", false);
    /// <summary>The selection colour in use: the user's own pick (marked, or any colour other than a preset's), else the accent (the selection follows the accent until the user picks its own).</summary>
    public string SelectionColourInUse() =>
        IsOverridden(nameof(SelectionColour)) || !(string.Equals(SelectionColour, Palette.Hex("Selection", false), StringComparison.OrdinalIgnoreCase) || string.Equals(SelectionColour, Palette.Hex("Selection", true), StringComparison.OrdinalIgnoreCase))
            ? SelectionColour : Accent;
    /// <summary>Colour of the lit notes and the playing line in the score: "Follow accent" (default) or "Custom" (<see cref="PlaybackHighlightColour"/>). A stored "Auto" from older files is moved on at load (SettingsMigration).</summary>
    public string PlaybackHighlight { get; set; } = PlaybackHighlightModes.FollowAccent;
    /// <summary>The colour used when <see cref="PlaybackHighlight"/> is "Custom".</summary>
    public string PlaybackHighlightColour { get; set; } = Palette.Hex("FollowHighlightText", false);
    public string HoverColour { get; set; } = Palette.Hex("HoverOutline", false);
    public string Accent { get; set; } = Palette.Hex("Accent", false);
    public string Background { get; set; } = Palette.Hex("WindowBackground", false);
    public string Panel { get; set; } = Palette.Hex("PanelBackground", false);
    public string TitleBarColour { get; set; } = Palette.Hex("ChromeStrip", false);
    public string ActiveTabColour { get; set; } = Palette.Hex("TabActive", false);
    public string TabHoverColour { get; set; } = Palette.Hex("TabHover", false);
    public string Text { get; set; } = Palette.Hex("TextPrimary", false);
    public string Muted { get; set; } = Palette.Hex("TextMuted", false);
    public string DarkScorePaperColour { get; set; } = Palette.Hex("ScoreDarkPaperDefault", false);
    public string LightScorePaperColour { get; set; } = Palette.Hex("ScoreLightPaperDefault", true);
    public string DarkScoreInkColour { get; set; } = Palette.Hex("ScoreDarkInkDefault", false);
    public string LightScoreInkColour { get; set; } = Palette.Hex("ScoreLightInkDefault", true);
    public string DarkScoreLinesColour { get; set; } = Palette.Hex("ScoreDarkStaffLineDefault", false);
    public string LightScoreLinesColour { get; set; } = Palette.Hex("ScoreLightStaffLineDefault", true);
    /// <summary>Which typefaces the interface and score use: "Windows" (Segoe UI / Consolas), "Bundled" (Inter / JetBrains Mono) or empty for the default of this operating system (see FontProvider).</summary>
    public string FontSource { get; set; } = "";
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
    /// <summary>"ExtraCompact", "Compact" (default), "Comfortable" or "Spacious": how tightly toolbar and row heights are packed.</summary>
    public string Density { get; set; } = "Compact";
    /// <summary>1 once the move of a saved "Comfortable" to the new Compact default has run (see SettingsMigration).</summary>
    public int DensityVersion { get; set; } = 1;
    /// <summary>Show icon glyphs on the toolbar (off = text-only buttons).</summary>
    public bool ShowToolbarIcons { get; set; } = true;
    /// <summary>Score paper: "Dark" or "Light".</summary>
    public string ScorePaper { get; set; } = "Dark";
    /// <summary>Palette tool ids pinned to the menu-row toolbar, in order.</summary>
    public List<string> PinnedTools { get; set; } = new();
    /// <summary>The top bar's icon row, as button ids and "|" separators in order; empty = the default order (Views/IconRow).</summary>
    public List<string> IconRowOrder { get; set; } = new();
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
    /// <summary>Draw the thin outline around the cell under the mouse, sized to the note length.</summary>
    public bool ShowNoteOutline { get; set; } = true;
    public double SelectionHighlightIntensity { get; set; } = 0.25;
    public string PlayheadColour { get; set; } = Palette.Hex("Playhead", false);
    public string CursorColour { get; set; } = Palette.Hex("ScoreCursor", false);
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

public static class PlaybackHighlightModes
{
    public const string FollowAccent = "Follow accent", Custom = "Custom";
    public static string[] All => new[] { FollowAccent, Custom };
}

/// <summary>Reads a bool that older settings files stored as null (auto) as true.</summary>
public sealed class NullIsTrueBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.TokenType is System.Text.Json.JsonTokenType.Null or System.Text.Json.JsonTokenType.True;

    public override bool HandleNull => true;

    public override void Write(System.Text.Json.Utf8JsonWriter writer, bool value, System.Text.Json.JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
