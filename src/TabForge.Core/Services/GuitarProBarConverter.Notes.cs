using System.Globalization;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Plugins;
using static TabForge.Services.GuitarProBeatReader;
using static TabForge.Services.GuitarProImporter;
using static TabForge.Services.GuitarProReflection;
using static TabForge.Services.GuitarProTrackConverter;

namespace TabForge.Services;

// Owns: building the note of one imported beat: string, fret and sounding pitch (ties, harmonics, keys and drums have their own rules), then its techniques, bend points, slide target and fingers.
// Does not own: the bar, voice and beat placement (GuitarProBarConverter.cs) or the technique reading (GuitarProBeatReader).
// Tests: TestGp5OwnFilesImport.
internal static partial class GuitarProBarConverter
{
    /// <summary>Where an imported note sits: string, fret and sounding pitch, the drawn harmonic fret and whether it is a tie destination.</summary>
    private readonly record struct NotePosition(int StringIndex, int Fret, int Midi, double? HarmonicFret, bool TiedDestination);

    /// <summary>Builds the note for one imported alphaTab note (pitch, string/fret, harmonics, drums, techniques); null when a drum hit lies outside the MIDI range.</summary>
    private static TabNote? BuildNote(object sourceNote, object beat, TabCell cell, TrackModel track, bool graceBeat, List<object>? drumArticulations, ImportBudget budget)
    {
        if (ResolveNotePosition(sourceNote, track, drumArticulations) is not { } position) return null;

    var note = new TabNote
    {
        StringIndex = position.StringIndex,
        Fret = position.Fret,
        MidiValue = position.Midi,
        // alphaTab has no Velocity: loudness is Note.Dynamics (falling back to Beat.Dynamics).
        Velocity = Dynamics.Clamp(Dynamics.VelocityForAlphaTab(
            (Get(sourceNote, "Dynamics") ?? Get(beat, "Dynamics"))?.ToString())),
        // Guitar Pro flags the *destination* note of a tie; playback must sustain the
        // origin instead of attacking again, so the flag lives on the destination.
        Tied = position.TiedDestination
    };
    note.HarmonicFret = position.HarmonicFret;
    ReadTechniques(sourceNote, beat, note);
    ReadBendPoints(sourceNote, note, budget);
    ReadSlideTarget(sourceNote, note);
    ReadCellMarks(sourceNote, beat, cell, note, budget.Context.Gp3To4);
    note.LeftHandFinger = FingerOf(Get(sourceNote, "LeftHandFinger"));
    note.RightHandFinger = FingerOf(Get(sourceNote, "RightHandFinger"));
return note;
    }

    /// <summary>Resolves string, fret and sounding pitch of an imported note (ties follow their chain, harmonics, keys and drums have their own rules); null when a drum hit lies outside the MIDI range.</summary>
    private static NotePosition? ResolveNotePosition(object sourceNote, TrackModel track, List<object>? drumArticulations)
    {
        // Fretted instruments number their strings 1..n; piano/keys/other instruments
        // have no string at all. Those notes must still be imported (their pitch lives
        // in RealValue), otherwise whole tracks silently lose their content.
        // A tie with no note before it (alphaTab clears it and keeps it as UnlinkedTieDestination, patch 0004) stays as the file wrote it.
        var tiedDestination = GetBool(sourceNote, "IsTieDestination", false) || GetBool(sourceNote, "UnlinkedTieDestination", false);
        // The standard drum ties often have no pitch or articulation on the destination at all.
        // alphaTab retains the original note in TieOrigin; using the drum kit's default
        // tuning here fabricates a crash cymbal (49) instead of continuing the real hit.
        // A drum tie that names its own sound keeps it: alphaTab links every drum note to the previous beat's first note (no strings).
        // A tie never picks again: it sounds the pitch of the first note of its chain, however many links it has.
        var pitchSource = tiedDestination && !(track.Kind == TrackKind.Drums && HasDrumSound(sourceNote))
            ? RootTieOrigin(sourceNote) : sourceNote;
        // A tie destination keeps its own string and fret on the tab (and in an export); the chain's origin only gives the pitch.
        // A tie with no note before it on its string reads fret 0 (the reference reader's rule), not the fret byte the file holds.
        var unlinkedTie = tiedDestination && Get(sourceNote, "TieOrigin") is null && track.Kind != TrackKind.Drums;
        var shownSource = track.Kind == TrackKind.Drums ? pitchSource : sourceNote;
        var gpString = GetInt(shownSource, "String", 0);
        var stringIndex = StringIndexOf(track, gpString);
        var pitchStringIndex = StringIndexOf(track, GetInt(pitchSource, "String", 0));
        var rawFret = unlinkedTie ? 0 : GetInt(shownSource, "Fret", 0);
        var fret = Math.Max(0, rawFret);
        var pitchRawFret = unlinkedTie ? 0 : GetInt(pitchSource, "Fret", 0);
        var pitchFret = Math.Max(0, pitchRawFret);
        var computed = track.StringTunings.Count > pitchStringIndex ? track.PitchOf(pitchStringIndex, pitchFret) : pitchFret;   // tuning + capo + fret, the shared rule
        var real = unlinkedTie ? 0 : GetInt(pitchSource, "RealValue", 0);
        // A dead note has fret -1 in alphaTab, so its RealValue is a semitone below the string; it is unpitched,
        // and the exported file (fret 0) reads back as the open string, so use the string's own pitch.
        var midi = real > 0 && pitchRawFret >= 0 ? real : computed;
        if (midi <= 0) midi = computed;
        // Harmonics: the standard sounding pitch. Natural = the string's harmonic at that fret; artificial,
        // pinch, tapped, semi = the fretted note plus the harmonic node's interval (12 = octave).
        // alphaTab's RealValue stacked extra octaves (an A.H. at fret 3 came in 34 semitones up).
        var harmonic = Get(pitchSource, "HarmonicType")?.ToString();
        double? harmonicFret = null;
        if (IsHarmonic(harmonic) && track.Kind != TrackKind.Drums && pitchStringIndex < track.StringTunings.Count)
        {
            var open = track.StringTunings[pitchStringIndex] + Math.Max(0, track.Capo);   // the capo raises the open string
            midi = HarmonicMidi(harmonic!, open, pitchFret, HarmonicNode(pitchSource));
        }
        // The drawn mark stays with the note that wrote it: a tie destination shows its own harmonic, not the origin's.
        if (IsHarmonic(Get(sourceNote, "HarmonicType")?.ToString()) && track.Kind != TrackKind.Drums && stringIndex < track.StringTunings.Count)
            harmonicFret = Math.Round(HarmonicNode(sourceNote), 1);
        // No string in the file (piano, whistle, synth...): place the pitch on an octave "string".
        if (gpString <= 0 && track.Kind == TrackKind.Keys && midi > 0)
            (stringIndex, fret) = PlacePitch(track.StringTunings, midi);
        // Drum notes: the TAB shows the GM percussion number (like the reference) on a line per kit group.
        if (track.Kind == TrackKind.Drums)
        {
            // A percussion note has no string/fret fallback. Prefer its GM articulation
            // when available, then its sounding value; never synthesize a kit-tuning note.
            midi = DrumPitch(pitchSource, drumArticulations);
            // A hit with no sound at all (no articulation, string or fret: alphaTab reads such GP3-5 notes as fret -1, value 0) is
            // imported as the sound playback gives it: the kit-tuning string its line sits on (ScoreToMidiCompiler plays a note without
            // a pitch as tuning + fret). It used to import as value 0 and a clean .gp wrote that as articulation 0, which reopened as
            // a different drum (GM 1, 9, 12 or 16) than the one that played before the export.
            if (midi <= 0 && track.StringTunings.Count > DrumLine(0) && track.PitchOf(DrumLine(0), 0) is >= 27 and <= 87 and var kitSound) midi = kitSound;
            // Keep every hit that is in the file: a value outside the GM drum
            // range (e.g. a "0" some older tabs use) stays visible and silent instead of vanishing.
            if (midi is < 0 or > MidiLimits.Max) return null;
            stringIndex = DrumLine(midi);
            fret = midi;
        }
            return new NotePosition(stringIndex, fret, midi, harmonicFret, tiedDestination);
    }
}
