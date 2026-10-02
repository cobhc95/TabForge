using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using TabForge.Models;
using TabForge.Plugins;
using static TabForge.Services.GuitarProBeatReader;
using static TabForge.Services.GuitarProImporter;
using static TabForge.Services.GuitarProReflection;
using static TabForge.Services.GuitarProTrackConverter;

namespace TabForge.Services;

// Owns: converting one imported bar (tick positions, durations, tuplets) into a measure.
// Does not own: beat details (GuitarProBeatReader) and track conversion.
// Tests: TestGuitarProFiles, TestTupletImport.
/// <summary>Converts the beats of one alphaTab bar into the cells of a <see cref="MeasureModel"/>: placement on the slot grid, durations, tuplets, voices and notes.</summary>
internal static class GuitarProBarConverter
{
    /// <summary>alphaTab timing: ticks per quarter note.</summary>
    internal const int TicksPerQuarter = 960;
    internal const int TicksPerSlot = TicksPerQuarter / MusicTime.SlotsPerQuarter;   // 240

    internal static void ConvertBar(object bar, MeasureModel measure, TrackModel track, ImportBudget budget, ref int measureNotes)
    {
        // GP6/7/8 drum notes point into the track's own articulation list (see DrumPitch).
        var drumArticulations = track.Kind == TrackKind.Drums
            ? AsObjects(Get(Get(Get(bar, "Staff"), "Track"), "PercussionArticulations")).ToList()
            : null;
        var clef = Get(bar, "Clef")?.ToString();
        if (!string.IsNullOrWhiteSpace(clef) && measure.Clef == Clefs.Guitar)
        {
            // G2 with an 8vb mark is TabForge's guitar clef (the default); a plain G2 is the treble clef. Other clefs keep alphaTab's name (F4, C3, C4, Neutral).
            var eightBelow = Get(bar, "ClefOttava")?.ToString() is "_8vb" or "8vb";
            if (clef != "G2") measure.Clef = clef!;
            else if (!eightBelow) measure.Clef = Clefs.Treble;
        }
        // alphaTab 1.8 keeps the simile mark on the track's Bar (MasterBar has none, so the old master-bar read was always empty).
        switch (Get(bar, "SimileMark")?.ToString())
        {
            case "Simple": measure.SimileOneBar = true; break;
            case "FirstOfDouble":
            case "SecondOfDouble": measure.SimileTwoBar = true; break;
        }

        var slots = measure.Cells.Count;
        var tone = GetInt(bar, "KeySignature", int.MinValue);
        if (tone != int.MinValue && measure.KeySignature is null) measure.KeySignature = tone;

        var voiceNumber = 0;
        var beatsInMeasure = 0;
        foreach (var voice in AsObjects(Get(bar, "Voices")))
        {
            if (voiceNumber >= InputLimits.MaxVoicesPerMeasure)
                throw new InvalidDataException("A score measure contains too many voices.");
            // GP tracks can expose two independent voices. Additional alphaTab voice lanes are
            // conservatively merged into voice 2 rather than flattened into voice 1.
            if (voiceNumber > 0 && measure.Voice2Cells.Count == 0)
                budget.AddCells(Math.Max(16, measure.Cells.Count));
            var cells = measure.CellsForVoice(voiceNumber == 0 ? 0 : 1, create: voiceNumber > 0);
            voiceNumber++;
            var lastSlot = -1;
            var exactCursor = 0.0; var voiceBeats = 0;
            foreach (var beat in AsObjects(Get(voice, "Beats")))
            {
                if (++beatsInMeasure > InputLimits.MaxBeatsPerMeasure)
                    throw new InvalidDataException("A score measure contains too many beats.");
                var start = BeatStartTicks(beat);
                var graceBeat = Get(beat, "GraceType")?.ToString() is "BeforeBeat" or "OnBeat" or "BendGrace";
                var displayStart = graceBeat ? GetInt(beat, "DisplayStart", start) : start;
                var placementStart = graceBeat ? displayStart : start;
                var tickSlot = placementStart >= 0
                    ? (int)Math.Round(placementStart / (double)TicksPerSlot)
                    : lastSlot + 1;
                // Keep the true tick position when the grid can represent it; otherwise push the beat
                // to the next free slot. Nothing is ever dropped - fidelity beats grid alignment.
                // Bars with more beats than sixteenth cells (32nds, 64ths) get extra cells; the true timing
                // lives in RhythmicPosition. Clamping here used to merge/drop the bar's last notes.
                var slot = Math.Max(0, Math.Max(tickSlot, lastSlot + 1));
                if (!graceBeat)
                    while (slot >= cells.Count && cells.Count < slots * 8) cells.Add(new TabCell());
                // Grace beats use their notated display position to share the principal beat's
                // rhythmic cell; their distinct playback offset remains stored on the grace note.
                // They must not advance the voice cursor before the principal note is merged.
                if (!graceBeat) lastSlot = slot;
                var durationSlots = BeatSlots(beat);

                // alphaTab's tick positions are whole ticks, so a run of 7-, 9- or 13-tuplets drifts early by up to a tick per beat
                // (a 13-tuplet ended at slot 11.95 instead of 12). Where the file's start agrees with the exact sum of the voice's
                // previous durations (within that truncation), the exact position is kept.
                var position = placementStart / (double)TicksPerSlot;
                if (!graceBeat && placementStart >= 0)
                {
                    // When both agree to float noise the file's whole-tick value is kept, so ordinary rhythms read exactly as before.
                    var drift = Math.Abs(exactCursor * TicksPerSlot - placementStart);
                    if (drift > 1e-6 && drift <= 2 * voiceBeats + 2) position = exactCursor;
                    exactCursor = position + ExactBeatSlots(beat);
                    voiceBeats++;
                }

                if (slot >= cells.Count) continue;
                var cell = cells[slot];
                if (placementStart >= 0) cell.RhythmicPosition ??= position;

                // Annotations belong to the beat, not to its notes. Read them first so a comment, chord
                // or lyric on a rest (or an empty beat) is never dropped - real Guitar Pro files put
                // dozens of comments on beats that carry no notes at all.
                ReadAnnotations(beat, cell, budget);

                if (GetBool(beat, "IsRest", false))
                {
                    // A rest may only claim an untouched cell. Previously this overwrote the duration
                    // of a note already placed by another voice, which turned an eighth-note downbeat
                    // into a quarter - the "first note of each bar is too long / notes melt together"
                    // defect (the score looked wrong next to TuxGuitar).
                    if (cell.Notes.Count == 0 && !cell.IsRest)
                    {
                        ApplyDuration(cell, beat);
                        cell.IsRest = true;
                    }
                    continue;
                }

                // The first voice to place a note owns the cell's duration; another voice merges its
                // notes into the same cell without rewriting that duration. A note also replaces an
                // earlier rest in the same cell.
                if (cell.Notes.Count == 0 || cell.Notes.All(existing => existing.IsGraceNote))
                {
                    cell.IsRest = false;
                    ApplyDuration(cell, beat);
                }

                var sourceNotes = AsObjects(Get(beat, "Notes")).Take(InputLimits.MaxNotesPerCell + 1).ToList();
                if (sourceNotes.Count > InputLimits.MaxNotesPerCell)
                    throw new InvalidDataException("A score beat contains too many notes.");
                budget.AddNotes(sourceNotes.Count, ref measureNotes);
                foreach (var sourceNote in sourceNotes)
                {
                    // Fretted instruments number their strings 1..n; piano/keys/other instruments
                    // have no string at all. Those notes must still be imported (their pitch lives
                    // in RealValue), otherwise whole tracks silently lose their content.
                    var tiedDestination = GetBool(sourceNote, "IsTieDestination", false);
                    // The standard drum ties often have no pitch or articulation on the destination at all.
                    // alphaTab retains the original note in TieOrigin; using the drum kit's default
                    // tuning here fabricates a crash cymbal (49) instead of continuing the real hit.
                    var pitchSource = tiedDestination && Get(sourceNote, "TieOrigin") is { } origin
                        ? origin : sourceNote;
                    var gpString = GetInt(pitchSource, "String", 0);
                    var stringIndex = gpString > 0
                        ? Math.Clamp(track.StringTunings.Count - gpString, 0, Math.Max(0, track.StringTunings.Count - 1))
                        : 0;
                    var rawFret = GetInt(pitchSource, "Fret", 0);
                    var fret = Math.Max(0, rawFret);
                    var computed = track.StringTunings.Count > stringIndex ? track.PitchOf(stringIndex, fret) : fret;   // tuning + capo + fret, the shared rule
                    var real = GetInt(pitchSource, "RealValue", 0);
                    // A dead note has fret -1 in alphaTab, so its RealValue is a semitone below the string; it is unpitched,
                    // and the exported file (fret 0) reads back as the open string, so use the string's own pitch.
                    var midi = real > 0 && rawFret >= 0 ? real : computed;
                    if (midi <= 0) midi = computed;
                    // Harmonics: the standard sounding pitch. Natural = the string's harmonic at that fret; artificial,
                    // pinch, tapped, semi = the fretted note plus the harmonic node's interval (12 = octave).
                    // alphaTab's RealValue stacked extra octaves (an A.H. at fret 3 came in 34 semitones up).
                    var harmonic = Get(sourceNote, "HarmonicType")?.ToString();
                    double? harmonicFret = null;
                    if (!string.IsNullOrEmpty(harmonic) && harmonic != "None" && track.Kind != TrackKind.Drums &&
                        stringIndex < track.StringTunings.Count)
                    {
                        var open = track.StringTunings[stringIndex] + Math.Max(0, track.Capo);   // the capo raises the open string
                        var node = Get(sourceNote, "HarmonicValue") is { } hv ? Convert.ToDouble(hv, System.Globalization.CultureInfo.InvariantCulture) : 12;
                        midi = HarmonicMidi(harmonic!, open, fret, node);
                        harmonicFret = Math.Round(node, 1);
                    }
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
                        if (midi is < 0 or > 127) continue;
                        stringIndex = DrumLine(midi);
                        fret = midi;
                    }

                    var note = new TabNote
                    {
                        StringIndex = stringIndex,
                        Fret = fret,
                        MidiValue = midi,
                        // alphaTab has no Velocity: loudness is Note.Dynamics (falling back to Beat.Dynamics).
                        Velocity = Dynamics.Clamp(Dynamics.VelocityForAlphaTab(
                            (Get(sourceNote, "Dynamics") ?? Get(beat, "Dynamics"))?.ToString())),
                        // Guitar Pro flags the *destination* note of a tie; playback must sustain the
                        // origin instead of attacking again, so the flag lives on the destination.
                        Tied = tiedDestination
                    };
                    note.HarmonicFret = harmonicFret;
                    ReadTechniques(sourceNote, beat, note);
                    ReadBendPoints(sourceNote, note, budget);
                    ReadSlideTarget(sourceNote, note);
                    ReadCellMarks(sourceNote, beat, cell, note);
                    note.LeftHandFinger = FingerOf(Get(sourceNote, "LeftHandFinger"));
                    note.RightHandFinger = FingerOf(Get(sourceNote, "RightHandFinger"));
                    // The standard per-note duration % (alphaTab keeps it on the note, as a 0..1 fraction).
                    // GP3-5 files carry no such value and alphaTab leaves garbage (a denormal) there for palm-muted
                    // notes, so it is only trusted from GPX / .gp files and only when sane.
                    if (!graceBeat && SanePerNoteDurationPercent(GetDouble(sourceNote, "DurationPercent", 1.0), budget.Context.Gp3To5) is { } durationPercent)
                        cell.SoundDurationPercent = durationPercent;

                    // Merging voices/staves can surface the same note twice (the same string at the same
                    // instant, or the same pitch on a keyboard part). A duplicate is not musical content:
                    // it doubles the velocity and makes the release collide with the retrigger, which
                    // synths swallow. Keep one.
                    // A grace note and the principal note it ornaments are never duplicates (a flam on the snare
                    // used to lose its main hit here).
                    var duplicate = cell.Notes.Any(n => n.IsGraceNote == note.IsGraceNote &&
                        n.StringIndex == note.StringIndex && n.Fret == note.Fret && n.MidiValue == note.MidiValue);
                    if (!duplicate)
                    {
                        if (cell.Notes.Count >= InputLimits.MaxNotesPerCell)
                            throw new InvalidDataException("A score beat contains too many notes.");
                        cell.Notes.Add(note);
                    }
                    else
                    {
                        budget.Context.SkippedDuplicates++;
                        if (budget.Context.DuplicateSamples.Count < 12)
                            budget.Context.DuplicateSamples.Add($"bar{measure.Number} slot{slot} midi{note.MidiValue} grace={note.IsGraceNote}/{cell.Notes.First(n => n.MidiValue == note.MidiValue && n.StringIndex == note.StringIndex).IsGraceNote} start={placementStart} dur={durationSlots} tie={note.Tied}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Beat start in ticks within the bar. alphaTab exposes several historical names for this; the
    /// most reliable in 1.8.x is PlaybackStart, so try each and fall back to -1 (unknown) rather than
    /// silently pretending every beat starts at zero.
    /// </summary>
    internal static int BeatStartTicks(object beat)
    {
        foreach (var name in new[] { "PlaybackStart", "PlayStart", "DisplayStart", "AbsolutePlaybackStart" })
        {
            var value = GetInt(beat, name, int.MinValue);
            if (value != int.MinValue) return value;
        }
        return -1;
    }

    internal static void ApplyDuration(TabCell cell, object beat)
    {
        var denominator = DurationToDenominator(Get(beat, "Duration"));
        cell.DurationDenominator = denominator;
        cell.Dots = Math.Clamp(GetInt(beat, "Dots", 0), 0, 2);

        // Tuplets: 3:2 is rendered as a triplet in our model.
        var num = GetInt(beat, "TupletNumerator", 0);
        var den = GetInt(beat, "TupletDenominator", 0);
        if (num == 0) num = GetInt(Get(beat, "Tuplet"), "Numerator", 0);
        if (den == 0) den = GetInt(Get(beat, "Tuplet"), "Denominator", 0);
        cell.IsTriplet = num == 3 && den == 2;
        if (num > 0 && den > 0)
        {
            cell.TupletNumerator = num;
            cell.TupletDenominator = den;
        }
        var soundDuration = GetInt(beat, "DurationPercent", GetInt(beat, "SoundDurationPercent", 100));
        cell.SoundDurationPercent = Math.Clamp(soundDuration, 1, 200);
        cell.OctaveShiftSemitones = ReadOctaveShift(Get(beat, "Ottava") ?? Get(beat, "OctaveShift"));
        // alphaTab's Beat.BeamingMode describes the join between this beat and the NEXT one
        // (ForceSplitToNext, ForceMergeWithNext, ForceSplitOnSecondaryToNext); TabForge stores the
        // override on the beat that starts the group, so a split reads the previous beat's mode.
        var previous = Get(Get(beat, "PreviousBeat"), "BeamingMode")?.ToString() ?? "";
        var own = Get(beat, "BeamingMode")?.ToString() ?? "";
        if (own == "ForceMergeWithNext" || previous == "ForceMergeWithNext") cell.BeamMode = BeamMode.Force;
        else if (previous == "ForceSplitToNext") cell.BeamMode = BeamMode.Break;
        cell.BreakSecondaryBeamBefore = previous == "ForceSplitOnSecondaryToNext";
        // Forced stem direction: Beat.InvertBeamDirection flips the automatic choice;
        // PreferredBeamDirection (Up/Down) is an explicit direction.
        var preferred = Get(beat, "PreferredBeamDirection")?.ToString() ?? "";
        if (preferred == "Up") cell.StemDirection = StemDirection.Up;
        else if (preferred == "Down") cell.StemDirection = StemDirection.Down;
        else if (GetBool(beat, "InvertBeamDirection", false)) cell.StemDirection = StemDirection.Invert;
    }

    internal static int ReadOctaveShift(object? value)
    {
        var text = value?.ToString() ?? "";
        if (text.Contains("15ma", StringComparison.OrdinalIgnoreCase) || text.Contains("TwoOctavesAbove", StringComparison.OrdinalIgnoreCase)) return 24;
        if (text.Contains("15mb", StringComparison.OrdinalIgnoreCase) || text.Contains("TwoOctavesBelow", StringComparison.OrdinalIgnoreCase)) return -24;
        if (text.Contains("8va", StringComparison.OrdinalIgnoreCase) || text.Contains("OctaveAbove", StringComparison.OrdinalIgnoreCase)) return 12;
        if (text.Contains("8vb", StringComparison.OrdinalIgnoreCase) || text.Contains("OctaveBelow", StringComparison.OrdinalIgnoreCase)) return -12;
        return 0;
    }

    /// <summary>A beat's exact length in sixteenth slots (dots and tuplet ratio unrounded), for placing tuplet runs.</summary>
    internal static double ExactBeatSlots(object beat)
    {
        var slots = 16.0 / Math.Clamp(DurationToDenominator(Get(beat, "Duration")), 1, 64);
        var dots = Math.Clamp(GetInt(beat, "Dots", 0), 0, 2);
        slots *= dots == 1 ? 1.5 : dots >= 2 ? 1.75 : 1.0;
        var num = GetInt(beat, "TupletNumerator", 0);
        var den = GetInt(beat, "TupletDenominator", 0);
        if (num > 0 && den > 0) slots = slots * den / num;
        return slots;
    }

    internal static int BeatSlots(object beat)
    {
        var denominator = DurationToDenominator(Get(beat, "Duration"));
        var slots = Math.Max(1, 16 / Math.Clamp(denominator, 1, 64));
        var dots = Math.Clamp(GetInt(beat, "Dots", 0), 0, 2);
        if (dots == 1) slots = (int)Math.Round(slots * 1.5);
        else if (dots >= 2) slots = (int)Math.Round(slots * 1.75);
        var num = GetInt(beat, "TupletNumerator", 0);
        var den = GetInt(beat, "TupletDenominator", 0);
        if (num > 0 && den > 0) slots = Math.Max(1, (int)Math.Round(slots * den / (double)num));
        return Math.Max(1, slots);
    }

    internal static int DurationToDenominator(object? duration)
    {
        if (duration is null) return 16;
        var text = duration.ToString() ?? "";
        if (int.TryParse(text, out var n) && n > 0) return NormalizeDenominator(n);
        return text.ToLowerInvariant() switch
        {
            "whole" => 1,
            "half" => 2,
            "quarter" => 4,
            "eighth" => 8,
            "sixteenth" => 16,
            "thirtysecond" or "thirty-second" => 32,
            "sixtyfourth" or "sixty-fourth" => 64,
            "twohundredfiftysecond" => 64,
            _ => 16
        };
    }

    /// <summary>alphaTab's Duration enum may use tick counts; normalise to note denominators.</summary>
    internal static int NormalizeDenominator(int value)
    {
        if (value is 1 or 2 or 4 or 8 or 16 or 32 or 64) return value;
        if (value >= 3840) return 1;
        if (value >= 1920) return 2;
        if (value >= 960) return 4;
        if (value >= 480) return 8;
        if (value >= 240) return 16;
        if (value >= 120) return 32;
        return 64;
    }

}
