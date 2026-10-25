using System.Linq;
using System.Text.Json.Serialization;
using TabForge.Platform.Theme;
using TabForge.Plugins;

namespace TabForge.Models;

// Owns: TabCell, TabNote, bend points and related models.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed class TabCell
{
    public List<TabNote> Notes { get; set; } = new();
    /// <summary>
    /// Exact onset in sixteenth-note slots when imported from a format with finer timing than the
    /// editor grid. Null means the cell index is the onset. This keeps tuplets aligned in both TAB
    /// and standard notation without changing the editable 16th-note grid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RhythmicPosition { get; set; }
    public int DurationDenominator { get; set; } = 8; // 1,2,4,8,16,32,64
    public int Dots { get; set; } = 0; // 0,1,2
    public bool IsTriplet { get; set; }
    /// <summary>Tuplet ratio numerator (e.g. 3 for a triplet, 5 for a quintuplet); 0 when not a tuplet.</summary>
    public int TupletNumerator { get; set; }
    /// <summary>Tuplet ratio denominator (e.g. 2 for a triplet, 4 for a quintuplet); 0 when not a tuplet.</summary>
    public int TupletDenominator { get; set; }
    /// <summary>Effective tuplet ratio, falling back to 3:2 when only the legacy triplet flag is set.</summary>
    [JsonIgnore]   // computed from the three fields above; a ValueTuple serialises as an empty object ("Tuplet":{}), 13 bytes of nothing per beat cell
    public (int Numerator, int Denominator) Tuplet =>
        TupletNumerator > 0 && TupletDenominator > 0 ? (TupletNumerator, TupletDenominator)
        : IsTriplet ? (3, 2)
        : (0, 0);
    public bool IsRest { get; set; }
    /// <summary>A rest the user wrote (R) rather than one the rest fill placed: navigation treats it as a written beat, as GP5.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WrittenRest { get; set; }
    public bool IsTied { get; set; }
    /// <summary>Independent sounding duration percentage; 100 preserves the normal note gate.</summary>
    public int SoundDurationPercent { get; set; } = 100;
    /// <summary>Ottava marking for this beat in semitones (-24, -12, 0, 12, 24).</summary>
    public int OctaveShiftSemitones { get; set; }
    /// <summary>Automatic, Force, or Break. Break is stored on this beat's left boundary.</summary>
    public BeamMode BeamMode { get; set; }
    public bool BreakSecondaryBeamBefore { get; set; }
    /// <summary>Automatic, Up, or Down stem override.</summary>
    public StemDirection StemDirection { get; set; }
    public bool IsGrace { get; set; }
    public bool GraceBeforeBeat { get; set; } = true;
    public bool Fermata { get; set; }
    public int Accent { get; set; } = 0; // 0 none, 1 accent, 2 heavy
    public bool Staccato { get; set; }
    public bool Tenuto { get; set; }
    /// <summary>Beat-level whammy-bar curve imported from Guitar Pro (offset 0..60, value in quarter-tones).</summary>
    public List<BendPointModel> WhammyPoints { get; set; } = new();
    /// <summary>Subdivision denominator for beat-level tremolo picking; 0 keeps the legacy default.</summary>
    public int TremoloPickDenominator { get; set; }
    /// <summary>Delay between successive strings of a brush/arpeggio stroke, in sixteenth slots (imported from the file's stroke speed); 0 keeps the default spread.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double BrushStepSlots { get; set; }
    public string? ChordName { get; set; }
    public string? Text { get; set; }
    /// <summary>Lyric line(s) attached to this beat; multiple lines are separated by '\n'.</summary>
    public string Lyrics { get; set; } = "";
    /// <summary>Mix Table change starting at this beat (F10), or null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MixChange? Mix { get; set; }

    /// <summary>True when the beat carries something to show even if it has no notes (comment, chord, lyric).</summary>
    [JsonIgnore]
    public bool HasAnnotation => !string.IsNullOrWhiteSpace(ChordName) || !string.IsNullOrWhiteSpace(Text) || !string.IsNullOrWhiteSpace(Lyrics);

    public TabCell Clone() => new()
    {
        Notes = Notes.Select(note => note.Clone()).ToList(),
        RhythmicPosition = RhythmicPosition,
        DurationDenominator = DurationDenominator,
        Dots = Dots,
        IsTriplet = IsTriplet,
        TupletNumerator = TupletNumerator,
        TupletDenominator = TupletDenominator,
        IsRest = IsRest,
        WrittenRest = WrittenRest,
        IsTied = IsTied,
        SoundDurationPercent = SoundDurationPercent,
        OctaveShiftSemitones = OctaveShiftSemitones,
        BeamMode = BeamMode,
        BreakSecondaryBeamBefore = BreakSecondaryBeamBefore,
        StemDirection = StemDirection,
        IsGrace = IsGrace,
        GraceBeforeBeat = GraceBeforeBeat,
        Fermata = Fermata,
        Accent = Accent,
        Staccato = Staccato,
        Tenuto = Tenuto,
        WhammyPoints = WhammyPoints.Select(point => point.Clone()).ToList(),
        TremoloPickDenominator = TremoloPickDenominator,
        BrushStepSlots = BrushStepSlots,
        Mix = Mix?.Clone(),
        ChordName = ChordName,
        Text = Text,
        Lyrics = Lyrics
    };
}

public sealed class TabNote
{
    public int StringIndex { get; set; } // 0 = highest string
    public int Fret { get; set; }
    public int MidiValue { get; set; }
    public int Velocity { get; set; } = 100;
    public bool Ghost { get; set; }
    public bool Dead { get; set; }
    public bool IsGraceNote { get; set; }
    public bool GraceBeforeBeat { get; set; } = true;
    /// <summary>Source onset relative to the target beat in sixteenth-note slots.</summary>
    public double GraceOnsetOffsetSlots { get; set; }
    /// <summary>Written grace-note length in sixteenth-note slots.</summary>
    public double GraceDurationSlots { get; set; }
    /// <summary>True when this note is tied from the previous note on the same string (Guitar Pro
    /// "tie destination"): playback sustains the previous note instead of attacking again.</summary>
    public bool Tied { get; set; }
    /// <summary>Guitar Pro bend curve: offset is 0..60 (fraction ×60) of the note, value in quarter-tones.</summary>
    public List<BendPointModel> BendPoints { get; set; } = new();
    /// <summary>Source Guitar Pro bend kind/style; the point curve remains the authoritative pitch path.</summary>
    public string BendTypeName { get; set; } = "";
    public string BendStyleName { get; set; } = "";
    /// <summary>Sounding MIDI pitch a slide resolves to (from the file's slide target), or 0 if none.</summary>
    public int SlideTargetMidi { get; set; }
    /// <summary>Target sounding pitch for an imported trill, or 0 when the source only specifies a fret.</summary>
    public int TrillTargetMidi { get; set; }
    /// <summary>Repeated-note duration denominator for an imported trill; 0 uses the legacy default.</summary>
    public int TrillDurationDenominator { get; set; }
    /// <summary>The fret a harmonic is touched or tapped at (12, 7, 5, 17...) for natural, artificial, tapped, semi and pinch harmonics; null when absent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? HarmonicFret { get; set; }
    /// <summary>Left-hand finger imported from Guitar Pro: 0 thumb, 1 index, 2 middle, 3 ring, 4 little; null when not written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LeftHandFinger { get; set; }
    /// <summary>Right-hand finger imported from Guitar Pro (p i m a): 0 thumb, 1 index, 2 middle, 3 ring, 4 little; null when not written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RightHandFinger { get; set; }
    public HashSet<string> Techniques { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TabNote Clone() => new()
    {
        StringIndex = StringIndex,
        Fret = Fret,
        MidiValue = MidiValue,
        Velocity = Velocity,
        Ghost = Ghost,
        Dead = Dead,
        IsGraceNote = IsGraceNote,
        GraceBeforeBeat = GraceBeforeBeat,
        GraceOnsetOffsetSlots = GraceOnsetOffsetSlots,
        GraceDurationSlots = GraceDurationSlots,
        Tied = Tied,
        BendPoints = BendPoints.Select(point => point.Clone()).ToList(),
        BendTypeName = BendTypeName,
        BendStyleName = BendStyleName,
        SlideTargetMidi = SlideTargetMidi,
        TrillTargetMidi = TrillTargetMidi,
        TrillDurationDenominator = TrillDurationDenominator,
        HarmonicFret = HarmonicFret,
        LeftHandFinger = LeftHandFinger,
        RightHandFinger = RightHandFinger,
        Techniques = new HashSet<string>(Techniques, StringComparer.OrdinalIgnoreCase)
    };
}

public sealed class BendPointModel
{
    public double Offset { get; set; }
    public double Value { get; set; }

    public BendPointModel Clone() => new() { Offset = Offset, Value = Value };
}
