using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    private static readonly int[] StdGuitar = { 64, 59, 55, 50, 45, 40 };
    private static readonly int[] Bass4 = { 43, 38, 33, 28 };
    private static readonly int[] Bass5 = { 43, 38, 33, 28, 23 };
    private static readonly int[] Guitar7 = { 64, 59, 55, 50, 45, 40, 35 };
    private static readonly int[] DropD = { 64, 59, 55, 50, 45, 38 };

    private static InstrumentLayout Lay(int[] tunings, int capo = 0, bool drums = false, string preset = DrumMaps.GuitarPro5) =>
        new(tunings, capo, 24, drums, preset);

    private static TabCell MapBeat(InstrumentLayout layout, params (int S, int F)[] notes)
    {
        var cell = new TabCell { DurationDenominator = 8 };
        foreach (var (s, f) in notes) cell.Notes.Add(new TabNote { StringIndex = s, Fret = f, MidiValue = layout.PitchOf(s, f) });
        return cell;
    }

    private static TabCell DrumBeat(params int[] midis)
    {
        var cell = new TabCell { DurationDenominator = 8 };
        foreach (var m in midis) cell.Notes.Add(new TabNote { StringIndex = 0, Fret = m, MidiValue = m });
        return cell;
    }

    private static bool ValidOn(InstrumentLayout target, IEnumerable<TabCell> beats) => beats.All(b =>
        b.Notes.All(n => n.StringIndex >= 0 && n.StringIndex < target.StringCount && n.Fret >= 0 && n.Fret <= target.MaxFret
                         && n.MidiValue == target.PitchOf(n.StringIndex, n.Fret))
        && b.Notes.Where(n => !n.IsGraceNote).Select(n => n.StringIndex).Distinct().Count() == b.Notes.Count(n => !n.IsGraceNote));

    private static List<int> Pitches(IEnumerable<TabCell> beats) => beats.SelectMany(b => b.Notes).Select(n => n.MidiValue).ToList();

    /// <summary>Paste note mapper (COPY_PASTE_DESIGN 3.4 + owner Q2/Q5): identity, keep-pitch re-fret across tunings,
    /// string counts and capo, octave choice, out-of-range reporting, drums both ways, keep string/fret mode.</summary>
    private static void TestNoteMapper()
    {
        var gtr = Lay(StdGuitar);
        var bass4 = Lay(Bass4);
        var bass5 = Lay(Bass5);
        var riff = new[] { MapBeat(gtr, (5, 0)), MapBeat(gtr, (5, 3)), MapBeat(gtr, (4, 0)), MapBeat(gtr, (0, 0)) }; // E2 G2 A2 E4

        // Same instrument: identity.
        var chordBeats = new[] { MapBeat(gtr, (5, 0), (4, 2), (3, 2), (2, 1), (1, 0), (0, 0)), MapBeat(gtr, (1, 5)) };
        chordBeats[1].Notes[0].BendPoints.Add(new BendPointModel { Offset = 30, Value = 4 });
        var same = NoteMapper.Map(gtr, Lay(StdGuitar), chordBeats);
        Eq("note mapper: same instrument is a passthrough", NoteMapKind.Passthrough, same.Report.Kind);
        Check("note mapper: same instrument keeps every string, fret and pitch",
            same.Beats.SelectMany(b => b.Notes).Zip(chordBeats.SelectMany(b => b.Notes)).All(p => p.First.StringIndex == p.Second.StringIndex && p.First.Fret == p.Second.Fret && p.First.MidiValue == p.Second.MidiValue)
            && same.Report.NotesPlaced == 7 && same.Report.LeftOut.Count == 0 && same.Beats[1].Notes[0].BendPoints.Count == 1);
        Eq("note mapper: same instrument asks nothing", PasteQuestions.None, NoteMapper.Questions(gtr, Lay(StdGuitar), chordBeats));

        // Guitar -> 4-string bass.
        Eq("note mapper: guitar -> bass asks the octave question", PasteQuestions.Octave, NoteMapper.Questions(gtr, bass4, riff));
        var exact = NoteMapper.Map(gtr, bass4, riff, new NoteMapOptions { Octave = OctavePolicy.KeepExactPitch });
        Check("note mapper: guitar -> 4-string bass keeps the exact pitch on valid positions",
            ValidOn(bass4, exact.Beats) && Pitches(exact.Beats).SequenceEqual(new[] { 40, 43, 45, 64 }) && exact.Report.OctaveShift == 0,
            string.Join(",", Pitches(exact.Beats)));
        var shifted = NoteMapper.Map(gtr, bass4, riff, new NoteMapOptions { Octave = OctavePolicy.ShiftByOctave });
        Check("note mapper: guitar -> 4-string bass with octave shift drops one octave",
            ValidOn(bass4, shifted.Beats) && shifted.Report.OctaveShift == -12 && Pitches(shifted.Beats).SequenceEqual(new[] { 28, 31, 33, 52 })
            && shifted.Beats[0].Notes[0].StringIndex == 3 && shifted.Beats[0].Notes[0].Fret == 0,
            $"shift {shifted.Report.OctaveShift}: {string.Join(",", Pitches(shifted.Beats))}");

        // Guitar -> 5-string bass.
        var exact5 = NoteMapper.Map(gtr, bass5, riff);
        var shift5 = NoteMapper.Map(gtr, bass5, riff, new NoteMapOptions { Octave = OctavePolicy.ShiftByOctave });
        Check("note mapper: guitar -> 5-string bass keeps pitch, or shifts -12 on request",
            ValidOn(bass5, exact5.Beats) && Pitches(exact5.Beats).SequenceEqual(new[] { 40, 43, 45, 64 })
            && ValidOn(bass5, shift5.Beats) && shift5.Report.OctaveShift == -12 && Pitches(shift5.Beats).SequenceEqual(new[] { 28, 31, 33, 52 }));
        Eq("note mapper: bass -> guitar octave shift is +12", 12, NoteMapper.AutoOctaveShift(bass4, gtr, new[] { MapBeat(bass4, (3, 0)), MapBeat(bass4, (3, 5)) }));

        // Chord span on the bass: A power chord (45 + 52) is played in one position, not across 12 frets.
        var power = NoteMapper.Map(gtr, bass5, new[] { MapBeat(gtr, (4, 0), (3, 2)) });
        var frets = power.Beats[0].Notes.Where(n => n.Fret > 0).Select(n => n.Fret).ToList();
        Check("note mapper: re-fretted chord keeps a small hand span", ValidOn(bass5, power.Beats) && power.Report.NotesPlaced == 2 && (frets.Count < 2 || frets.Max() - frets.Min() <= 4),
            string.Join(",", power.Beats[0].Notes.Select(n => $"{n.StringIndex}/{n.Fret}")));

        // 6 -> 7 strings: shared strings keep the same fingering.
        var g7 = Lay(Guitar7);
        var to7 = NoteMapper.Map(gtr, g7, chordBeats);
        Check("note mapper: 6 -> 7 strings keeps pitch and fingering, asks nothing",
            ValidOn(g7, to7.Beats) && Pitches(to7.Beats).SequenceEqual(Pitches(chordBeats)) && to7.Report.NotesRefretted == 0
            && NoteMapper.Questions(gtr, g7, chordBeats) == PasteQuestions.None, $"refretted {to7.Report.NotesRefretted}");

        // Drop D.
        var dropD = Lay(DropD);
        var toDrop = NoteMapper.Map(gtr, dropD, new[] { MapBeat(gtr, (5, 0)), MapBeat(gtr, (5, 3), (4, 5), (3, 5)) });
        Check("note mapper: standard -> drop D keeps pitch (low E becomes fret 2)",
            ValidOn(dropD, toDrop.Beats) && Pitches(toDrop.Beats).SequenceEqual(new[] { 40, 43, 50, 55 })
            && toDrop.Beats[0].Notes[0].StringIndex == 5 && toDrop.Beats[0].Notes[0].Fret == 2
            && NoteMapper.Questions(gtr, dropD) == PasteQuestions.None);

        // Capo 2: frets are relative to the capo; notes below the capo are reported.
        var capo = Lay(StdGuitar, capo: 2);
        var toCapo = NoteMapper.Map(gtr, capo, new[] { MapBeat(gtr, (5, 5)), MapBeat(gtr, (5, 0)) });
        Check("note mapper: capo 2 re-frets relative to the capo and reports the note below it",
            ValidOn(capo, toCapo.Beats) && toCapo.Beats[0].Notes.Count == 1 && toCapo.Beats[0].Notes[0].Fret == 3
            && toCapo.Beats[1].Notes.Count == 0 && toCapo.Report.LeftOut.Count == 1 && toCapo.Report.LeftOut[0].Reason == LeftOutReason.OutOfRange
            && toCapo.Report.LeftOut[0].BeatIndex == 1);

        // Out of range: reported with exact pitch; folded into range when octave shifting is chosen.
        var high = new[] { MapBeat(gtr, (5, 0)), MapBeat(gtr, (0, 24)) }; // E2, E6
        var highExact = NoteMapper.Map(gtr, bass4, high);
        Check("note mapper: out-of-range note is left out and reported (exact pitch)",
            highExact.Report.LeftOut.Count == 1 && highExact.Report.LeftOut[0].MidiValue == 88 && highExact.Report.NotesPlaced == 1
            && highExact.Beats.Count == 2 && highExact.Beats[1].Notes.Count == 0 && highExact.Report.Summary().Contains("left out"));
        var highShift = NoteMapper.Map(gtr, bass4, high, new NoteMapOptions { Octave = OctavePolicy.ShiftByOctave });
        Check("note mapper: octave shifting folds a remaining out-of-range note instead of dropping it",
            highShift.Report.LeftOut.Count == 0 && highShift.Report.NotesOctaveFolded == 1 && ValidOn(bass4, highShift.Beats), highShift.Report.Summary());

        // Six-note chord onto four strings: inner notes go first, top and bottom stay.
        var sixOnFour = NoteMapper.Map(gtr, bass4, new[] { chordBeats[0] });
        var kept = Pitches(sixOnFour.Beats);
        Check("note mapper: 6-note chord on 4 strings keeps the lowest and highest notes",
            ValidOn(bass4, sixOnFour.Beats) && kept.Count == 4 && kept.Contains(40) && kept.Contains(64)
            && sixOnFour.Report.LeftOut.Count == 2 && sixOnFour.Report.LeftOut.All(l => l.Reason == LeftOutReason.NoFreeString), string.Join(",", kept));

        // Techniques: a bend survives re-fretting; a natural harmonic on a string with another open pitch and a stale finger do not.
        var tech = MapBeat(gtr, (5, 12));
        tech.Notes[0].HarmonicFret = 12; tech.Notes[0].Techniques.Add(TechniqueNames.Harmonic); tech.Notes[0].LeftHandFinger = 3;
        var bend = MapBeat(gtr, (3, 7));
        bend.Notes[0].BendPoints.Add(new BendPointModel { Offset = 30, Value = 4 }); bend.Notes[0].Techniques.Add(TechniqueNames.Bend);
        var techMap = NoteMapper.Map(gtr, dropD, new[] { tech, bend });
        var h = techMap.Beats[0].Notes[0];
        Check("note mapper: harmonic on another open string and a changed-fret finger are removed; bends kept",
            h.HarmonicFret is null && !h.Techniques.Contains(TechniqueNames.Harmonic) && h.LeftHandFinger is null && techMap.Report.TechniquesDropped >= 2
            && techMap.Beats[1].Notes[0].BendPoints.Count == 1 && techMap.Beats[1].Notes[0].Techniques.Contains(TechniqueNames.Bend));

        // Pitched -> drums and back (Q5).
        var drums = Lay(new[] { 60, 59, 58, 57, 56, 55 }, drums: true);
        Eq("note mapper: guitar -> drums asks the drum question", PasteQuestions.Drums, NoteMapper.Questions(gtr, drums));
        Eq("note mapper: drums -> guitar asks the drum question", PasteQuestions.Drums, NoteMapper.Questions(drums, gtr));
        var toDrums = NoteMapper.Map(gtr, drums, new[] { chordBeats[0], new TabCell(), bend });
        Check("note mapper: guitar -> drums pastes the rhythm onto one snare hit per beat",
            toDrums.Report.Kind == NoteMapKind.RhythmToDrumSound && toDrums.Beats.Count == 3
            && toDrums.Beats[0].Notes.Count == 1 && toDrums.Beats[0].Notes[0].MidiValue == NoteMapper.DefaultDrumSound
            && toDrums.Beats[1].Notes.Count == 0 && toDrums.Beats[2].Notes.Count == 1 && toDrums.Beats[2].Notes[0].BendPoints.Count == 0
            && !toDrums.Beats[2].Notes[0].Techniques.Contains(TechniqueNames.Bend) && toDrums.Report.NotesMerged == 5 && toDrums.Report.TechniquesDropped >= 1);
        var refused = NoteMapper.Map(gtr, drums, riff, new NoteMapOptions { Drums = DrumPastePolicy.DontPaste });
        Check("note mapper: \"Don't paste\" refuses guitar -> drums", refused.Report.Kind == NoteMapKind.Refused && refused.Beats.Count == 0);
        var fromDrums = NoteMapper.Map(drums, gtr, new[] { DrumBeat(36, 42), DrumBeat(), DrumBeat(38) });
        Check("note mapper: drums -> guitar pastes the rhythm on the lowest open string",
            fromDrums.Report.Kind == NoteMapKind.RhythmToPitch && ValidOn(gtr, fromDrums.Beats)
            && fromDrums.Beats[0].Notes.Count == 1 && fromDrums.Beats[0].Notes[0].MidiValue == 40 && fromDrums.Beats[0].Notes[0].StringIndex == 5
            && fromDrums.Beats[1].Notes.Count == 0 && fromDrums.Beats[2].Notes.Count == 1);

        // Drums -> drums: by MIDI note, restrung by the target's drum map.
        var drumTab = Lay(new[] { 60, 59, 58, 57, 56, 55, 54 }, drums: true, preset: DrumMaps.DrumTab);
        var d2d = NoteMapper.Map(drums, drumTab, new[] { DrumBeat(42, 36) });
        Check("note mapper: drums -> drums keeps the MIDI note and follows the target drum map",
            d2d.Report.Kind == NoteMapKind.DrumsByMidi && d2d.Beats[0].Notes.Select(n => n.MidiValue).SequenceEqual(new[] { 42, 36 })
            && d2d.Beats[0].Notes[0].StringIndex == 1 && d2d.Beats[0].Notes[1].StringIndex == 6
            && NoteMapper.Questions(drums, drumTab) == PasteQuestions.None);

        // Keep string and fret (Paste Special): positions kept, pitch follows the target tuning, missing strings reported.
        var ksf = new NoteMapOptions { Mode = PasteMappingMode.KeepStringAndFret };
        var ksfDrop = NoteMapper.Map(gtr, dropD, new[] { MapBeat(gtr, (5, 3)) }, ksf);
        var ksfBass = NoteMapper.Map(gtr, bass4, new[] { MapBeat(gtr, (0, 5), (5, 3)) }, ksf);
        Check("note mapper: keep string and fret mode keeps positions and reports missing strings",
            ksfDrop.Report.Kind == NoteMapKind.KeepStringAndFret && ksfDrop.Beats[0].Notes[0].StringIndex == 5 && ksfDrop.Beats[0].Notes[0].Fret == 3
            && ksfDrop.Beats[0].Notes[0].MidiValue == 41
            && ksfBass.Beats[0].Notes.Count == 1 && ksfBass.Beats[0].Notes[0].MidiValue == 48
            && ksfBass.Report.LeftOut.Count == 1 && ksfBass.Report.LeftOut[0].Reason == LeftOutReason.StringMissing
            && NoteMapper.Questions(gtr, bass4, riff, PasteMappingMode.KeepStringAndFret) == PasteQuestions.None);

        // The mapper never mutates its input.
        Check("note mapper: input beats are left unchanged", chordBeats[0].Notes.Count == 6 && chordBeats[0].Notes[5].Fret == 0 && tech.Notes[0].HarmonicFret == 12);
    }
}
