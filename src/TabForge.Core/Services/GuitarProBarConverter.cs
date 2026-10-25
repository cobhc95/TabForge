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

// Owns: converting one imported bar (tick positions, durations, tuplets) into a measure.
// Does not own: beat details (GuitarProBeatReader) and track conversion.
// Tests: TestGp5OwnFilesImport.
/// <summary>Converts the beats of one alphaTab bar into the cells of a <see cref="MeasureModel"/>: placement on the slot grid, durations, tuplets, voices and notes.</summary>
internal static partial class GuitarProBarConverter
{
    /// <summary>alphaTab timing: ticks per quarter note.</summary>
    internal const int TicksPerQuarter = 960;
    internal const int TicksPerSlot = TicksPerQuarter / MusicTime.SlotsPerQuarter;   // 240

    internal static void ConvertBar(object bar, MeasureModel measure, TrackModel track, ImportBudget budget, ref int measureNotes)
    {
        var drumArticulations = ApplyBarHeader(bar, measure, track);

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
            ConvertVoice(voice, cells, measure, track, budget, ref measureNotes, ref beatsInMeasure, slots, drumArticulations);
        }
    }

    /// <summary>What a voice remembers from beat to beat: the last used cell, the on-beat grace just read, the exact tick cursor and the notes it placed itself.</summary>
    private sealed class VoiceState
    {
        public int LastSlot = -1;
        public int GraceSlot = -1;   // the cell and start tick of the on-beat grace just read: its principal beat shares the cell
        public int GraceTicks;
        public double ExactCursor;
        public int VoiceBeats;
        // The notes this voice placed itself: only a note from another voice or staff can be a duplicate of one (see below).
        public readonly HashSet<TabNote> OwnNotes = new();
    }

    /// <summary>Where a beat landed: its cell, slot and exact position.</summary>
    private readonly record struct BeatPlacement(TabCell Cell, int Slot, int PlacementStart, double Position, bool GraceBeat);

    private static void ConvertVoice(object voice, List<TabCell> cells, MeasureModel measure, TrackModel track, ImportBudget budget, ref int measureNotes, ref int beatsInMeasure, int slots, List<object>? drumArticulations)
    {
        var state = new VoiceState();
        foreach (var beat in AsObjects(Get(voice, "Beats")))
        {
            if (++beatsInMeasure > InputLimits.MaxBeatsPerMeasure)
                throw new InvalidDataException("A score measure contains too many beats.");
            // Guitar Pro writes an empty bar (and an unused second voice) as one "empty" beat, which alphaTab also calls a rest:
            // it is no beat at all, so the bar stays empty for the fill and the drawing instead of holding a rest. Its text, chord
            // or mix change still lands on the cell at its position.
            if (GetBool(beat, "IsEmpty", false))
            {
                var emptyStart = BeatStartTicks(beat);
                var emptySlot = Math.Max(state.LastSlot + 1, emptyStart >= 0 ? (int)Math.Round(emptyStart / (double)TicksPerSlot) : 0);
                if (emptySlot < cells.Count) ReadAnnotations(beat, cells[emptySlot], budget);
                continue;
            }
            if (PlaceBeat(beat, cells, state, slots, budget) is not { } placed) continue;
            ReadPlacedBeat(beat, placed, state, measure, track, budget, ref measureNotes, drumArticulations);
        }
    }

    /// <summary>Finds the cell of a beat: the slot its ticks give (an on-beat grace shares the cell of its principal), the exact position, and the cells added for an overfull bar. Null when the beat has no cell.</summary>
    private static BeatPlacement? PlaceBeat(object beat, List<TabCell> cells, VoiceState state, int slots, ImportBudget budget)
    {
        var start = BeatStartTicks(beat);
        var graceBeat = IsGraceBeat(beat);
        var displayStart = graceBeat ? GetInt(beat, "DisplayStart", start) : start;
        var placementStart = graceBeat ? displayStart : start;
        var tickSlot = placementStart >= 0
            ? (int)Math.Round(placementStart / (double)TicksPerSlot)
            : state.LastSlot + 1;
        // An on-beat grace sits on a slot line and its principal half a slot later (3.0 and 3.5): rounding would put the principal
        // on the next slot, leave the grace a cell of its own and push the rest of the bar a grace's length late. They share one cell.
        if (!graceBeat && state.GraceSlot >= 0 && placementStart >= state.GraceTicks && placementStart - state.GraceTicks <= TicksPerSlot / 2)
            tickSlot = state.GraceSlot;
        if (!graceBeat) state.GraceSlot = -1;
        // Keep the true tick position when the grid can represent it; otherwise push the beat
        // to the next free slot. Nothing is ever dropped - fidelity beats grid alignment.
        // Bars with more beats than sixteenth cells (32nds, 64ths) get extra cells; the true timing
        // lives in RhythmicPosition. Clamping here used to merge/drop the bar's last notes.
        var slot = Math.Max(0, Math.Max(tickSlot, state.LastSlot + 1));
        // A bar overfull past the grid's reach (eight times its length) is still read beat for beat: each further beat takes
        // the next free cell and keeps its true time in RhythmicPosition (a grace beat the cell its principal beat takes next).
        // The cells stop at MaxCellsPerMeasure, which the project validator enforces; a beat beyond that is dropped.
        var remapped = slot >= slots * 8;
        if (remapped) slot = state.LastSlot + 1;
        // A grace beat comes before its principal, so it opens the principal's cell when that is past the cells so far.
        while (slot >= cells.Count && cells.Count < InputLimits.MaxCellsPerMeasure) { cells.Add(new TabCell()); budget.AddCells(1); }
        // Grace beats use their notated display position to share the principal beat's
        // rhythmic cell; their distinct playback offset remains stored on the grace note.
        // They must not advance the voice cursor before the principal note is merged.
        if (!graceBeat) state.LastSlot = slot;
        else if (!remapped && Get(beat, "GraceType")?.ToString() == "OnBeat") { state.GraceSlot = slot; state.GraceTicks = placementStart; }   // the last grace of a group is the one next to the principal

        // alphaTab's tick positions are whole ticks, so a run of 7-, 9- or 13-tuplets drifts early by up to a tick per beat
        // (a 13-tuplet ended at slot 11.95 instead of 12). Where the file's start agrees with the exact sum of the voice's
        // previous durations (within that truncation), the exact position is kept.
        var position = placementStart / (double)TicksPerSlot;
        if (!graceBeat && placementStart >= 0)
        {
            // When both agree to float noise the file's whole-tick value is kept, so ordinary rhythms read exactly as before.
            var drift = Math.Abs(state.ExactCursor * TicksPerSlot - placementStart);
            if (drift > 1e-6 && drift <= 2 * state.VoiceBeats + 2) position = state.ExactCursor;
            state.ExactCursor = position + ExactBeatSlots(beat);
            state.VoiceBeats++;
        }

        if (slot >= cells.Count) return null;
        var cell = cells[slot];
        if (placementStart >= 0) cell.RhythmicPosition ??= position;
        return new BeatPlacement(cell, slot, placementStart, position, graceBeat);
    }

    /// <summary>Reads what a placed beat holds into its cell: annotations, a rest, the duration and the notes.</summary>
    private static void ReadPlacedBeat(object beat, BeatPlacement placed, VoiceState state, MeasureModel measure, TrackModel track, ImportBudget budget, ref int measureNotes, List<object>? drumArticulations)
    {
        var cell = placed.Cell; var graceBeat = placed.GraceBeat; var placementStart = placed.PlacementStart; var position = placed.Position;
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
            return;
        }

        // The first voice to place a note owns the cell's duration; another voice merges its
        // notes into the same cell without rewriting that duration. A note also replaces an
        // earlier rest in the same cell.
        if (cell.Notes.Count == 0 || cell.Notes.All(existing => existing.IsGraceNote))
        {
            if (!graceBeat && cell.Notes.Count > 0 && placementStart >= 0) cell.RhythmicPosition = position;   // the cell sits where its principal beat was written, not where the grace before it began
            cell.IsRest = false;
            ApplyDuration(cell, beat);
        }

        AddBeatNotes(beat, placed, state, measure, track, budget, ref measureNotes, drumArticulations);
    }

    private static void AddBeatNotes(object beat, BeatPlacement placed, VoiceState state, MeasureModel measure, TrackModel track, ImportBudget budget, ref int measureNotes, List<object>? drumArticulations)
    {
        var cell = placed.Cell; var graceBeat = placed.GraceBeat; var placementStart = placed.PlacementStart; var slot = placed.Slot;
        var sourceNotes = AsObjects(Get(beat, "Notes")).Take(InputLimits.MaxNotesPerCell + 1).ToList();
        if (sourceNotes.Count > InputLimits.MaxNotesPerCell)
            throw new InvalidDataException("A score beat contains too many notes.");
        budget.AddNotes(sourceNotes.Count, ref measureNotes);
        foreach (var sourceNote in sourceNotes)
        {
            var note = BuildNote(sourceNote, beat, cell, track, graceBeat, drumArticulations, budget);
            if (note is null) continue;
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
            // used to lose its main hit here). Neither are notes one voice wrote itself: a drum beat that
            // lists the same sound twice, a tie and a hit of the same sound, or two graces of one sound
            // are all in the file, and Guitar Pro and TuxGuitar keep every one.
            var twin = cell.Notes.FirstOrDefault(n => !state.OwnNotes.Contains(n) && n.IsGraceNote == note.IsGraceNote &&
                n.StringIndex == note.StringIndex && n.Fret == note.Fret && n.MidiValue == note.MidiValue);
            if (twin is null)
            {
                if (cell.Notes.Count >= InputLimits.MaxNotesPerCell)
                    throw new InvalidDataException("A score beat contains too many notes.");
                cell.Notes.Add(note);
                state.OwnNotes.Add(note);
            }
            else
            {
                budget.Context.SkippedDuplicates++;
                if (budget.Context.DuplicateSamples.Count < 12)
                    budget.Context.DuplicateSamples.Add($"bar{measure.Number} slot{slot} midi{note.MidiValue} grace={note.IsGraceNote}/{twin.IsGraceNote} start={placementStart} tie={note.Tied}");
            }
        }
    }

    /// <summary>Reads the bar's clef and simile mark into the measure; returns the drum articulation list of a drum track (null otherwise).</summary>
    private static List<object>? ApplyBarHeader(object bar, MeasureModel measure, TrackModel track)
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
        return drumArticulations;
    }

    /// <summary>The first note of a tie chain on the note's own string, staff and voice lane (the note itself when it is no linked tie destination).
    /// The walk stops after 64 links (a guard against malformed cyclic chains); the pitch then comes from link 64.</summary>
    private static object RootTieOrigin(object note)
    {
        for (var links = 0; GetBool(note, "IsTieDestination", false) && Get(note, "TieOrigin") is { } linked && PlayedOrigin(linked) is var origin && SameLane(note, origin); links++)
        {
            if (links == 64) break;
            note = origin;
        }
        return note;
    }

    private static bool IsGraceBeat(object? beat) => Get(beat, "GraceType")?.ToString() is "BeforeBeat" or "OnBeat" or "BendGrace";

    /// <summary>
    /// The note a tie really continues. A grace-into-bend is written as a tie, and alphaTab links it to the grace beat just before it
    /// (on the same string); the tie sounds the note before that grace, so a grace origin steps back to the last played beat's note on its string.
    /// </summary>
    private static object PlayedOrigin(object origin)
    {
        var gpString = GetInt(origin, "String", 0);
        var beat = Get(origin, "Beat");
        for (var steps = 0; steps < 8 && IsGraceBeat(beat) && Get(beat, "PreviousBeat") is { } previous; steps++)
        {
            beat = previous;
            if (IsGraceBeat(beat)) continue;
            return AsObjects(Get(beat, "Notes")).FirstOrDefault(n => GetInt(n, "String", 0) == gpString) ?? origin;
        }
        return origin;
    }

    private static bool SameLane(object a, object b)
    {
        static object? Voice(object n) => Get(Get(n, "Beat"), "Voice");
        return GetInt(a, "String", 0) == GetInt(b, "String", 0) && GetInt(Voice(a), "Index", 0) == GetInt(Voice(b), "Index", 0) &&
            ReferenceEquals(Get(Get(Voice(a), "Bar"), "Staff"), Get(Get(Voice(b), "Bar"), "Staff"));
    }

    private static int StringIndexOf(TrackModel track, int gpString) =>
        gpString > 0 ? Math.Clamp(track.StringTunings.Count - gpString, 0, Math.Max(0, track.StringTunings.Count - 1)) : 0;

    private static bool IsHarmonic(string? type) => !string.IsNullOrEmpty(type) && type != "None";

    private static double HarmonicNode(object note) =>
        Get(note, "HarmonicValue") is { } value ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 12;

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
        var (num, den) = TupletRatio(beat);
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

    /// <summary>A beat's tuplet ratio (0, 0 = none): its own numerator and denominator, else those of its Tuplet object. One source for the cell, its slots and its position.</summary>
    internal static (int Numerator, int Denominator) TupletRatio(object beat)
    {
        var num = GetInt(beat, "TupletNumerator", 0);
        var den = GetInt(beat, "TupletDenominator", 0);
        if (num == 0) num = GetInt(Get(beat, "Tuplet"), "Numerator", 0);
        if (den == 0) den = GetInt(Get(beat, "Tuplet"), "Denominator", 0);
        return (num, den);
    }

    /// <summary>A beat's exact length in sixteenth slots (dots and tuplet ratio unrounded), for placing tuplet runs.</summary>
    internal static double ExactBeatSlots(object beat)
    {
        var slots = 16.0 / Math.Clamp(DurationToDenominator(Get(beat, "Duration")), 1, 64);
        var dots = Math.Clamp(GetInt(beat, "Dots", 0), 0, 2);
        slots *= dots == 1 ? 1.5 : dots >= 2 ? 1.75 : 1.0;
        var (num, den) = TupletRatio(beat);
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
        var (num, den) = TupletRatio(beat);
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
