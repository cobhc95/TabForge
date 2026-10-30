using System.Text.Json.Serialization;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the note-entry, paste and fretboard-related editing preferences data, score text styles and clip snapping.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestSettingsFileSplitSnapshots.
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
    /// <summary>How scale notes are marked: "Dots" (default), "Rings", "Cell tint", "Note names" or "Degrees".</summary>
    public string ScaleHighlightStyle { get; set; } = ScaleHighlightStyles.Dots;
    /// <summary>Opacity of the scale marks in percent (10 to 100, default 60).</summary>
    public int ScaleHighlightOpacity { get; set; } = ScaleHighlightStyles.DefaultOpacity;
    /// <summary>Draw the scale root a little stronger than the other scale notes.</summary>
    public bool ScaleEmphasiseRoot { get; set; } = true;
    /// <summary>Dim the notes outside the scale.</summary>
    public bool ScaleDimOutside { get; set; }
    /// <summary>Mark only the frets in view.</summary>
    public bool ScaleOnlyInView { get; set; }
    /// <summary>The custom scale colour as "#RRGGBB" (used when the colour is "Custom"); empty for none.</summary>
    public string ScaleHighlightCustomColour { get; set; } = "";
    /// <summary>The status bar scale readout: where it reads from, which tracks it listens to, the scale families, the common-scale preference and the fretboard link.</summary>
    public ScaleReadoutSettings ScaleReadout { get; set; } = new();
    /// <summary>Colour of the scale highlight (see <see cref="ScaleHighlightStyles.Colours"/>).</summary>
    public string ScaleHighlightColour { get; set; } = "Accent";
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
    /// <summary>Show the grey trail of notes just played (the legend's "recent"); off by default, and a settings file without the key loads as off.</summary>
    public bool PreviewPreviousNotesEnabled { get; set; } = false;
    public string? ScaleHighlight { get; set; }
    /// <summary>Ask before deleting a bar.</summary>
    public bool ConfirmDeleteBar { get; set; }
    /// <summary>"Move to the next note after writing": false keeps the cursor on the note just written.</summary>
    [JsonPropertyName("AutoAdvance")]
    public bool MoveToNextNoteAfterWriting { get; set; } = false;
    /// <summary>1 once the "keep the cursor on the note just written" default replaced the earlier auto-advance default.</summary>
    public int MoveToNextNoteVersion { get; set; } = 1;
    /// <summary>"Plus key lengthens notes": false (default) means + shortens and - lengthens; true swaps the two.</summary>
    [JsonPropertyName("ReversePlusMinusDuration")]
    public bool PlusKeyLengthensNotes { get; set; }

    /// <summary>The old names still read and write the same settings (older code and tests).</summary>
    [JsonIgnore] public bool AutoAdvance { get => MoveToNextNoteAfterWriting; set => MoveToNextNoteAfterWriting = value; }
    [JsonIgnore] public bool ReversePlusMinusDuration { get => PlusKeyLengthensNotes; set => PlusKeyLengthensNotes = value; }
    public bool PreventBarOverflow { get; set; }
    /// <summary>Edits keep every bar complete: gaps become rests, a deleted beat becomes a rest. Off: bars may stay incomplete (shown red).</summary>
    public bool FillBarsWithRests { get; set; } = true;
    /// <summary>When deleting notes with the rest fill on: false leaves a rest of the same length, true leaves merged rests.</summary>
    public bool MergeRestsOnDelete { get; set; } = true;
    /// <summary>Remembered paste answers (docs/specs/COPY_PASTE_DESIGN.md Q1..Q5): "Ask" (default) or an option id, see <see cref="PasteQuestionInfo"/>.</summary>
    public string PasteBeatsOntoNotes { get; set; } = PasteQuestionInfo.Ask;
    public string PasteOctave { get; set; } = PasteQuestionInfo.Ask;
    public string PasteBarsOntoNotes { get; set; } = PasteQuestionInfo.Ask;
    public string PasteBarSettings { get; set; } = PasteQuestionInfo.Ask;
    public string PasteDrums { get; set; } = PasteQuestionInfo.Ask;
    /// <summary>What Delete does on bars selected on the timeline: "Ask" (default, opens the choice prompt) or a remembered <see cref="BarRangeAction"/> name.</summary>
    public string BarRangeDelete { get; set; } = "Ask";
    /// <summary>What Copy, Cut, Delete and Paste act on when the timeline highlight and a selection both exist: one of <see cref="EditTargetRule.Choices"/> (default: whichever was clicked last).</summary>
    public string EditTarget { get; set; } = EditTargetRule.Choices[0];
    /// <summary>Delete and Cut on highlighted bars ask first; false after "Don't ask again" (this setting turns the question back on).</summary>
    public bool ConfirmHighlightClear { get; set; } = true;
    /// <summary>The option the Delete prompt preselects (the last one chosen), a <see cref="BarRangeAction"/> name.</summary>
    /// <summary>What converting an audio track that has MIDI clips to an instrument track does with them: "Ask" (default), "Write as notation" or "Keep as MIDI".</summary>
    public string ConvertMidiClips { get; set; } = "Ask";
    /// <summary>What moving a section does with audio or MIDI clips that start before it or end after it: "Ask" (default, opens the prompt), "Split" (cut at the section edges and move the inside parts) or "Leave" (move only the notation). See <see cref="SectionAudioChoice"/>.</summary>
    public string SectionMoveAudio { get; set; } = SectionAudioChoice.Ask;
    /// <summary>What an edit does when it makes two clips overlap on a lane: "Ask" (default, opens the prompt), "Move" (the existing clip goes to a free lane) or "Keep" (they overlap). See <see cref="ClipOverlapChoice"/>.</summary>
    public string ClipOverlap { get; set; } = ClipOverlapChoice.Ask;
    /// <summary>What dragging a section's end past the next section does: "Ask" (default, opens the prompt), "AddEmpty", "RepeatLast", "RepeatSign" or "Stop". See <see cref="SectionStretchChoice"/>.</summary>
    public string SectionStretch { get; set; } = SectionStretchChoice.Ask;
    /// <summary>What dragging a section's start back past the previous section does: "Ask" (default), "AddEmpty", "RepeatFirst", "TakePrevious" or "Stop". See <see cref="SectionStretchBackChoice"/>.</summary>
    public string SectionStretchBack { get; set; } = SectionStretchBackChoice.Ask;
    /// <summary>What a click on an empty take lane of a tab track shows: "Tab" (default, the score) or "Piano roll" (the roll on that lane; the first note written creates a MIDI take). See <see cref="EmptyLaneChoice"/>.</summary>
    public string EmptyLaneClick { get; set; } = EmptyLaneChoice.Tab;
    /// <summary>Paste Special on the timeline: when true the window starts from <see cref="PasteSpecialRange"/>, the choices of the last paste ("Remember these options").</summary>
    public bool RememberPasteSpecial { get; set; }
    public RangePasteOptions PasteSpecialRange { get; set; } = new();
    public string BarRangeLastChoice { get; set; } = nameof(BarRangeAction.Clear);
    /// <summary>Piano roll: the velocity lane's height in DIP (dragging its divider sets it) and whether it is shown (its chevron collapses it).</summary>
    public double PianoRollVelocityLane { get; set; } = 84;
    public bool PianoRollVelocityLaneShown { get; set; } = true;
    /// <summary>Piano roll: the lane under the grid the user chose last (a lane id, see PianoRollControllers; -1 is velocity), kept across songs and restarts.</summary>
    public int PianoRollLane { get; set; } = -1;
    /// <summary>Piano roll quantize options: strength in percent, and whether note starts and note ends move to the grid.</summary>
    public int PianoRollQuantizeStrength { get; set; } = 100;
    public bool PianoRollQuantizeStarts { get; set; } = true;
    public bool PianoRollQuantizeEnds { get; set; }
    /// <summary>Piano roll quantize swing in percent: every second grid line moves later (100 = a triplet feel).</summary>
    public int PianoRollQuantizeSwing { get; set; }
    /// <summary>Piano roll note colours: Track (brighter = louder), Velocity, Pitch or Channel.</summary>
    public string PianoRollColours { get; set; } = "Track";
    /// <summary>Piano roll scale highlight on its rows (the Scale highlight setting gives the scale). The SHOW ROWS choice is saved per song (SongProject.PianoRollRows).</summary>
    public bool PianoRollScaleShown { get; set; }
    /// <summary>Retired (read once, never written): the piano roll's own scale choice; the piano roll now shares the status bar's pick. Was: null follows Settings &gt; Scale highlight, "Auto" detects it
    /// from the shown notes, else a "Root Scale" highlight (a manual pick sticks until Auto is picked).</summary>
    public string? PianoRollScalePick { get; set; }
    /// <summary>True once an old piano roll pick (above, no longer written) was applied to the shared scale pick; it is applied at most once.</summary>
    public bool PianoRollScalePickMigrated { get; set; }
    /// <summary>Piano roll scale lock (off by default): drawn, moved, transposed and step-input notes snap to the scale highlight's pitches.</summary>
    public bool PianoRollScaleLock { get; set; }
    /// <summary>Piano roll transpose steps: "Semitones", or "Scale steps" (Up / Down move by diatonic steps of the scale highlight, in key).</summary>
    public string PianoRollTransposeMode { get; set; } = "Semitones";
    /// <summary>Piano roll step input and the scale: "Off" writes exactly the key pressed, "Snap" moves a key outside the scale to the nearest scale note, "Only scale notes" ignores keys outside the scale. Independent of the Lock to scale setting.</summary>
    public string PianoRollStepScaleFilter { get; set; } = "Snap";
    /// <summary>Piano roll: the length of new notes (PianoRollSnap.Lengths: Grid follows the snap) and whether faint ghost notes of the other
    /// takes and tracks show (off by default).</summary>
    public string PianoRollNoteLength { get; set; } = "Grid";
    public bool PianoRollGhosts { get; set; }
    /// <summary>Piano roll: a thin green needle runs down from the ruler's playback marker through the grid and the lane (on by default).</summary>
    public bool PianoRollPlaybackNeedle { get; set; } = true;
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
    /// <summary>Grid size: Bar, 1/2, 1/4, 1/8, 1/16 or 1/32 (note values; 1/4 = one beat in 4/4), a triplet 1/4T, 1/8T or 1/16T (three in the time of
    /// two), or a dotted 1/4D, 1/8D or 1/16D (one and a half times the value).</summary>
    public string Grid { get; set; } = "1/4";
    public static readonly string[] Grids = { "Bar", "1/2", "1/4", "1/8", "1/16", "1/32", "1/4T", "1/8T", "1/16T", "1/4D", "1/8D", "1/16D" };
    public bool ToGrid { get; set; } = true;
    public bool ToItems { get; set; } = true;
    public bool ToPlayhead { get; set; } = true;
    /// <summary>Snap to the grid however far away it is (else only within <see cref="DistancePx"/>).</summary>
    public bool GridAtAnyDistance { get; set; } = true;
    /// <summary>How near (screen pixels) an edge must be to snap to another item or the playhead.</summary>
    public int DistancePx { get; set; } = 8;
}
