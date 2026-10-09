using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the note-entry, paste and fretboard-related editing preferences data, score text styles and clip snapping.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public sealed class EditingSettings
{
    public int DefaultDuration { get; set; } = 4;
    /// <summary>After a clip is deleted, cropped or moved away, the empty bars at the end of the song are removed (never bars with notation, sections, mix points or repeats).</summary>
    public bool TrimEmptyBarsAtEnd { get; set; } = true;
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
    /// <summary>Strength of the scale highlight in percent (100 = the standard look; the root always stays stronger than the other scale notes).</summary>
    public int ScaleHighlightStrength { get; set; } = ScaleHighlightStyles.DefaultStrength;
    /// <summary>Colour of the fretboard position dots (inlays): "White" (default); "Default" follows the theme.</summary>
    public string FretMarkerColour { get; set; } = "White";
    /// <summary>Brightness of the fretboard position dots: "Original" (default), "Brighter", "Bright" or "Brightest".</summary>
    public string FretMarkerBrightness { get; set; } = FretMarkerLevels.Original;
    /// <summary>Size of the fret numbers and note bubbles: "Small" (75%), "Medium" (85%) or "Large" (100%, default).</summary>
    public string FretNumberSize { get; set; } = FretNumberSizes.Large;
    /// <summary>Note marker (bubble and number) size, 60 to 160 percent in 10% steps; 80 is the default, 100 is the original look.</summary>
    public int FretMarkerSizePercent { get; set; } = 90;
    /// <summary>Fretboard string spacing relative to the fret width: "Compact", "Natural" (default) or "Wide" (at most 1.5x natural).</summary>
    public string FretStringSpacing { get; set; } = FretStringSpacings.Natural;
    public int FretboardFrets { get; set; } = 24;
    public int PreviewHorizon { get; set; } = 6;
    public bool PreviewNotesEnabled { get; set; } = true;
    public string? ScaleHighlight { get; set; }
    /// <summary>Ask before deleting a bar.</summary>
    public bool ConfirmDeleteBar { get; set; }
    /// <summary>Move the caret forward by the entered note's value.</summary>
    public bool AutoAdvance { get; set; } = false;
    /// <summary>false: + shortens, - lengthens. true: + lengthens, - shortens.</summary>
    public bool ReversePlusMinusDuration { get; set; }
    public bool PreventBarOverflow { get; set; }
    /// <summary>Edits keep every bar complete: gaps become rests, a deleted beat becomes a rest. Off: bars may stay incomplete (shown red).</summary>
    public bool FillBarsWithRests { get; set; } = true;
    /// <summary>When deleting notes with the rest fill on: false leaves a rest of the same length, true leaves merged rests.</summary>
    public bool MergeRestsOnDelete { get; set; } = true;
    /// <summary>Remembered paste answers (docs/COPY_PASTE_DESIGN.md Q1..Q5): "Ask" (default) or an option id, see <see cref="PasteQuestionInfo"/>.</summary>
    public string PasteBeatsOntoNotes { get; set; } = PasteQuestionInfo.Ask;
    public string PasteOctave { get; set; } = PasteQuestionInfo.Ask;
    public string PasteBarsOntoNotes { get; set; } = PasteQuestionInfo.Ask;
    public string PasteBarSettings { get; set; } = PasteQuestionInfo.Ask;
    public string PasteDrums { get; set; } = PasteQuestionInfo.Ask;
    /// <summary>What Delete does on bars selected on the timeline: "Ask" (default, opens the choice prompt) or a remembered <see cref="BarRangeAction"/> name.</summary>
    public string BarRangeDelete { get; set; } = "Ask";
    /// <summary>The option the Delete prompt preselects (the last one chosen), a <see cref="BarRangeAction"/> name.</summary>
    /// <summary>What converting an audio track that has MIDI clips to an instrument track does with them: "Ask" (default), "Write as notation" or "Keep as MIDI".</summary>
    public string ConvertMidiClips { get; set; } = "Ask";
    public string BarRangeLastChoice { get; set; } = nameof(BarRangeAction.Clear);
}

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
    public bool Enabled { get; set; } = true;
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
