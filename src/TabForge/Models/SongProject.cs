using System.Linq;
using System.Text.Json.Serialization;
using TabForge.Plugins;

namespace TabForge.Models;

// standard score model. New fields all have defaults so old .tforge files still load.
public sealed partial class SongProject
{
    private int _formatVersion = 2;
    /// <summary>File schema: 3 exactly while the song has an audio track, otherwise the loaded value (3 reads as 2), so songs without audio write what older releases read.</summary>
    public int FormatVersion
    {
        get => Tracks is { } tracks && tracks.Any(t => t?.IsAudio == true) ? 3 : _formatVersion == 3 ? 2 : _formatVersion;
        set => _formatVersion = value;
    }
    private string _title = "Untitled";
    public string Title
    {
        get => _title;
        set { if (_title == value) return; _title = value; DisplayStateChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Raised when the name or the unsaved mark changes, so a tab showing this song follows without polling.</summary>
    public event EventHandler? DisplayStateChanged;
    public string Subtitle { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string MusicAuthor { get; set; } = "";
    public string LyricsAuthor { get; set; } = "";
    public string Copyright { get; set; } = "";
    public string TabAuthor { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string Notice { get; set; } = "";
    public string Lyrics { get; set; } = "";
    public int Tempo { get; set; } = 120;
    public int TimeSignatureNumerator { get; set; } = 4;
    public int TimeSignatureDenominator { get; set; } = 4;
    public int KeySignature { get; set; } = 0; // -7 flats .. +7 sharps, 0 = C
    public bool KeySignatureMinor { get; set; }
    /// <summary>Presentation preference persisted with the score; musical data is never altered.</summary>
    public bool GrayInactiveVoice { get; set; }
    public List<TrackModel> Tracks { get; set; } = new();
    /// <summary>The tracks that carry notation (every kind except <see cref="TrackKind.Audio"/>), in track order.</summary>
    [JsonIgnore] public IEnumerable<TrackModel> NotationTracks => Tracks.Where(t => t.HasNotation);
    /// <summary>The first notation track; null in an audio-only song. Callers must cope with null.</summary>
    [JsonIgnore] public TrackModel? FirstNotationTrack
    {
        get
        {
            // A plain loop: playback reads this on every tick and must not allocate.
            var tracks = Tracks;
            for (var i = 0; i < tracks.Count; i++) if (tracks[i].HasNotation) return tracks[i];
            return null;
        }
    }
    /// <summary>The track whose bars hold the song-wide bar attributes (tempo, time and key changes, sections, repeats): the first notation track, else the first track of an audio-only song (its bars carry the same attributes). Null with no tracks.</summary>
    [JsonIgnore] public TrackModel? MasterBarTrack => FirstNotationTrack ?? (Tracks.Count > 0 ? Tracks[0] : null);
    public List<MarkerModel> Markers { get; set; } = new();
    /// <summary>Mixer groups (levels, pan and pitch per instrument group).</summary>
    public MixerSettings Mixer { get; set; } = new();
    public string? ImportedFrom { get; set; }

    /// <summary>
    /// Moves the track at <paramref name="from"/> to <paramref name="to"/>, shifting the rest to keep
    /// the order contiguous. Returns false when the move is a no-op or out of range. The track order is
    /// the arrangement order: index 0 is the first row, and playback/MIDI channels follow this list.
    /// </summary>
    public bool MoveTrack(int from, int to)
    {
        if (from < 0 || from >= Tracks.Count) return false;
        to = Math.Clamp(to, 0, Tracks.Count - 1);
        if (from == to) return false;
        var track = Tracks[from];
        Tracks.RemoveAt(from);
        Tracks.Insert(to, track);
        return true;
    }
}

public sealed class MarkerModel
{
    public int MeasureIndex { get; set; }
    public string Title { get; set; } = "Section";
    public string ColorHex { get; set; } = "#2E74B5";
    /// <summary>Preserves an exact colour chosen in the section editor across automatic family colour resolution.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ColorIsExplicit { get; set; }
    /// <summary>Prevents this section from being moved by timeline drag-and-drop.</summary>
    public bool LockPosition { get; set; }
    /// <summary>
    /// Bar count when the section was resized to stop before the next one (null = runs to the next section).
    /// Stored as a length, not an end bar, so inserting/deleting/moving bars before it keeps it intact.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LengthBars { get; set; }
}

public enum TrackKind
{
    Guitar,
    Bass,
    Drums,
    Keys,
    Other,
    /// <summary>Clips only: no notes, no tuning, no instrument. Never converted to or from another kind.</summary>
    Audio = 5
}

public sealed class TrackModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Guitar";
    public TrackKind Kind { get; set; } = TrackKind.Guitar;
    /// <summary>True for an audio track (clips only, see <see cref="TrackKind.Audio"/>).</summary>
    [JsonIgnore] public bool IsAudio => Kind == TrackKind.Audio;
    /// <summary>True when the track can hold notes (every kind except audio).</summary>
    [JsonIgnore] public bool HasNotation => Kind != TrackKind.Audio;
    public string ColorHex { get; set; } = "#F61A16";
    public string InstrumentName { get; set; } = "Electric Guitar";
    /// <summary>Drum tracks: notation preset (see DrumMaps); default Guitar Pro 5.</summary>
    public string DrumMapPreset { get; set; } = "Guitar Pro 5";
    /// <summary>Drum tracks: user overrides used by the "Custom" preset.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TabForge.Services.DrumMapEntry>? CustomDrumMap { get; set; }
    /// <summary>Musician who normally plays this part (display only).</summary>
    public string Performer { get; set; } = "";
    /// <summary>Free-form notes about the part, gear or sound.</summary>
    public string TrackNotes { get; set; } = "";
    public int NumberOfFrets { get; set; } = 24;
    public int MidiChannel { get; set; } = 0;
    public int MidiProgram { get; set; } = 29;
    /// <summary>Capo fret. Fret numbers are relative to the capo: sounding pitch = tuning + capo + fret (<see cref="PitchOf"/>).</summary>
    public int Capo { get; set; }

    /// <summary>The one pitch rule: open string tuning + capo + fret. <see cref="TabNote.MidiValue"/> stores this sounding pitch.</summary>
    public int PitchOf(int stringIndex, int fret)
        => StringTunings.Count == 0 ? fret
            : StringTunings[Math.Clamp(stringIndex, 0, StringTunings.Count - 1)] + Math.Max(0, Capo) + fret;

    /// <summary>Inverse of <see cref="PitchOf"/>: the fret that sounds <paramref name="pitch"/> on the string.</summary>
    public int FretOf(int stringIndex, int pitch)
        => pitch - (StringTunings[Math.Clamp(stringIndex, 0, StringTunings.Count - 1)] + Math.Max(0, Capo));

    public int MidiOutputDeviceId { get; set; } = -1;
    public int Volume { get; set; } = 100;
    public int Pan { get; set; } = 64;
    public int Chorus { get; set; } = 0;
    public int Reverb { get; set; } = 24;
    public int Transpose { get; set; } = 0;
    public bool Mute { get; set; } = false;
    public bool Solo { get; set; } = false;
    public List<int> StringTunings { get; set; } = new() { 64, 59, 55, 50, 45, 40 }; // high -> low
    public List<MeasureModel> Measures { get; set; } = new();
    public RigPreset Rig { get; set; } = new();
    /// <summary>Where the track sounds: <see cref="SoundSources.Midi"/> (default) or its plug-in chain.</summary>
    public string SoundSource { get; set; } = SoundSources.Midi;
    /// <summary>Audio clips on the track's audio lane (recordings and dropped files).</summary>
    public List<AudioClip> AudioClips { get; set; } = new();
    /// <summary>The clip lanes under the row (which ones play); see <see cref="ClipLanes"/>.</summary>
    public List<ClipLane> Lanes { get; set; } = new();
    /// <summary>Record-armed: the audio input is monitored through the track (and recorded when Record is on).</summary>
    public bool RecordArm { get; set; }
    /// <summary>Audio input used when armed: see <see cref="AudioInputs"/>.</summary>
    public string AudioInput { get; set; } = AudioInputs.Input1;
    /// <summary>While armed: listen to the input live through the track (on by default). Off: it is still recorded and metered.</summary>
    public bool MonitorInput { get; set; } = true;
    /// <summary>Tint this track's row and lane with its colour (Settings sets the strength).</summary>
    public bool TintRow { get; set; } = true;
    /// <summary>With the chain on: also play the track's General MIDI sound (off = only its VST instrument, if any).</summary>
    public bool MidiSound { get; set; } = true;
    /// <summary>GM sound was ticked automatically (no VST instrument played); untick it again when one does.</summary>
    [JsonIgnore] public bool MidiSoundAuto { get; set; }
    /// <summary>The user unticked GM sound themselves: automatic switching leaves it off.</summary>
    [JsonIgnore] public bool MidiSoundManualOff { get; set; }
    /// <summary>Set on a track added from a startup template (FX window "Add as a track on startup"): it is left out of the saved file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartupTemplateId { get; set; }
    /// <summary>Mixer group chosen for this track; null = by instrument.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MixerGroup { get; set; }

    /// <summary>Runtime only: a group bus / master stand-in (see <see cref="MixerBuses"/>) has its fixed engine slot here; -1 for real tracks.</summary>
    [JsonIgnore] public int BusSlot { get; set; } = -1;
    /// <summary>Runtime only: the bus chain this stand-in edits (null for real tracks).</summary>
    [JsonIgnore] public BusChain? Bus { get; set; }
    [JsonIgnore] public bool IsBus => BusSlot >= 0;
}

public sealed class MeasureModel
{
    public int Number { get; set; }
    public List<TabCell> Cells { get; set; } = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
    // Bar-level the reference attributes (null/default = inherit song default)
    public int? TimeSigNum { get; set; }
    public int? TimeSigDenom { get; set; }
    public int? KeySignature { get; set; }
    public bool? KeySignatureMinor { get; set; }
    public string Clef { get; set; } = Clefs.Guitar;
    public bool RepeatStart { get; set; }
    public bool RepeatEnd { get; set; }
    public int RepeatCount { get; set; } = 2;
    public int AlternateEnding { get; set; } = 0; // 0 = none, else ending number 1..8
    /// <summary>All passes this ending plays on (bit n = pass n+1), e.g. "1.2.3."; 0 = only <see cref="AlternateEnding"/>.</summary>
    public int AlternateEndingMask { get; set; }

    /// <summary>The passes this bar plays on as a bit mask (bit n = pass n+1); 0 = every pass.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int EndingPasses => AlternateEndingMask != 0 ? AlternateEndingMask : AlternateEnding > 0 ? 1 << (AlternateEnding - 1) : 0;

    /// <summary>Ending label as shown above the bar, e.g. "1." or "1.2.3.".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EndingLabel => string.Concat(Enumerable.Range(0, 8).Where(n => (EndingPasses & (1 << n)) != 0).Select(n => $"{n + 1}."));
    public bool IsDoubleBar { get; set; }
    public bool SimileOneBar { get; set; }
    public bool SimileTwoBar { get; set; }
    public string SectionName { get; set; } = "";
    public int? TempoChange { get; set; }
    /// <summary>Tempo changes inside the bar (Guitar Pro mix-table tempo on a later beat), in position order.</summary>
    public List<TempoPoint>? MidBarTempos { get; set; }
    public bool TripletFeel { get; set; }
    /// <summary>Triplet feel subdivision: None, Triplet8th or Triplet16th. TripletFeel keeps old projects compatible.</summary>
    public string TripletFeelKind { get; set; } = TripletFeels.None;
    public bool FreeTime { get; set; }
    /// <summary>Begins a new engraved system at this measure.</summary>
    public bool ForceLineBreak { get; set; }
    /// <summary>Keep this measure on the preceding system when possible.</summary>
    public bool PreventLineBreak { get; set; }
    /// <summary>Pickup / anacrusis measure (fewer beats than the time signature).</summary>
    public bool Anacrusis { get; set; }
    /// <summary>Navigation directions from the file (Da Capo, Dal Segno, Coda, Fine, ...), comma-joined.</summary>
    public string Directions { get; set; } = "";
    /// <summary>Voice 2 has an independent event grid. Empty means the score has no second voice.</summary>
    public List<TabCell> Voice2Cells { get; set; } = new();

    public List<TabCell> CellsForVoice(int voiceIndex, bool create = false)
    {
        if (voiceIndex <= 0) return Cells;
        if (Voice2Cells.Count == 0 && create)
            Voice2Cells = Enumerable.Range(0, Math.Max(16, Cells.Count)).Select(_ => new TabCell()).ToList();
        return Voice2Cells;
    }
}

/// <summary>
/// Mix Table point: parameters that change from a beat on (null = unchanged). Volume/effects use
/// The reference's 0-16 scale, pan -8..+8; Program is a GM program; Tempo applies from this bar.
/// </summary>
public sealed class MixChange
{
    public int? Program { get; set; }
    public int? Volume { get; set; }
    public int? Pan { get; set; }
    public int? Chorus { get; set; }
    public int? Reverb { get; set; }
    public int? Phaser { get; set; }
    public int? Tremolo { get; set; }
    public int? Tempo { get; set; }
    /// <summary>Beats over which the change ramps (0 = immediately).</summary>
    public int TransitionBeats { get; set; }
    public bool AllTracks { get; set; }
    [JsonIgnore]
    public bool IsEmpty => Program is null && Volume is null && Pan is null && Chorus is null && Reverb is null &&
        Phaser is null && Tremolo is null && Tempo is null;
    public MixChange Clone() => (MixChange)MemberwiseClone();
}

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

// Canonical the reference technique/effect names used in Techniques sets + UI.
public static class GpEffects
{
    public static readonly string[] All =
    {
        "PalmMute", "LetRing", "HOPO", "Bend", "Slide", "LegatoSlide", "ShiftSlide",
        "Vibrato", "WideVibrato", "TremBar", "TremBarWide", "Harmonic", "ArtificialHarmonic",
        "PinchHarmonic", "TapHarmonic", "SemiHarmonic", "FeedbackHarmonic",
        "SlideInBelow", "SlideInAbove", "SlideOutUp", "SlideOutDown", "PickSlideUp", "PickSlideDown",
        "TremBarCustom", "TremBarDive", "TremBarDip", "TremBarHold", "TremBarPredive", "TremBarPrediveDive",
        "Tapping", "LeftTap", "Slap", "Pop", "Trill", "TremoloPick", "Ghost", "Dead", "DeadSlapped",
        "Accent", "HeavyAccent", "Staccato", "Tenuto", "Legato", "FadeIn", "FadeOut",
        "WahOpen", "WahClose", "BrushDown", "BrushUp", "ArpeggioDown", "ArpeggioUp",
        "Rasgueado", "PickDown", "PickUp", "GraceBefore", "GraceOnBeat", "GraceBend", "Fermata", "Tie"
    };
}

/// <summary>A tempo change at a position inside a bar, in sixteenth-note slots from the bar start.</summary>
/// <param name="RampSlots">Transition length in slots (a Guitar Pro mix-table tempo duration): the tempo moves linearly per beat
/// from the tempo before this point to <c>Tempo</c> over this many slots (clipped to the bar end). 0 = instant.</param>
public sealed record TempoPoint(double Slot, int Tempo, double RampSlots = 0);
