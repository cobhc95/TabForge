using TabForge.Models;

namespace TabForge.Services;

/// <summary>What the paste mapper needs to know about one side of a paste (the clip's source track or the target track).</summary>
public sealed record InstrumentLayout(IReadOnlyList<int> StringTunings, int Capo, int NumberOfFrets, bool IsDrums,
    string DrumMapPreset = DrumMaps.GuitarPro5, IReadOnlyList<DrumMapEntry>? CustomDrumMap = null)
{
    public static InstrumentLayout Of(TrackModel track) => new(
        track.StringTunings.ToArray(), Math.Max(0, track.Capo), track.NumberOfFrets,
        track.Kind == TrackKind.Drums || track.MidiChannel == 9,
        string.IsNullOrEmpty(track.DrumMapPreset) ? DrumMaps.GuitarPro5 : track.DrumMapPreset,
        track.CustomDrumMap?.Select(e => e.Clone()).ToArray());

    public int StringCount => StringTunings.Count;
    public int MaxFret => NumberOfFrets > 0 ? NumberOfFrets : 24;
    public int OpenPitch(int s) => StringTunings[s] + Math.Max(0, Capo);
    public int PitchOf(int s, int fret) => OpenPitch(s) + fret;
    public int FretOf(int s, int pitch) => pitch - OpenPitch(s);
    public bool Fits(int s, int pitch) { var f = FretOf(s, pitch); return f >= 0 && f <= MaxFret; }
    public bool FitsAnywhere(int pitch) { for (var s = 0; s < StringCount; s++) if (Fits(s, pitch)) return true; return false; }
    public int LowestOpen => StringCount == 0 ? 0 : StringTunings.Min() + Math.Max(0, Capo);

    /// <summary>Same string count, tuning and capo: string/fret can be kept as they are.</summary>
    public bool SameLayout(InstrumentLayout other) =>
        IsDrums == other.IsDrums && Capo == other.Capo && StringTunings.SequenceEqual(other.StringTunings);
}

/// <summary>Paste Special mapping mode.</summary>
public enum PasteMappingMode { KeepPitch, KeepStringAndFret }

/// <summary>Owner question Q2: pasting between instruments of different range.</summary>
public enum OctavePolicy { KeepExactPitch, ShiftByOctave }

/// <summary>Owner question Q5: pasting a pitched instrument onto drums, or the reverse.</summary>
public enum DrumPastePolicy { RhythmOnOneSound, DontPaste }

/// <summary>Which owner questions a paste needs (the combined dialog shows only these).</summary>
[Flags]
public enum PasteQuestions { None = 0, Octave = 1, Drums = 2 }

public sealed record NoteMapOptions
{
    public PasteMappingMode Mode { get; init; } = PasteMappingMode.KeepPitch;
    public OctavePolicy Octave { get; init; } = OctavePolicy.KeepExactPitch;
    /// <summary>Paste Special octave shift in semitones (a multiple of 12, -24..+24), applied on top of the automatic shift; 0 = none.
    /// Pitched instruments only (ignored for drums and with <see cref="PasteMappingMode.KeepStringAndFret"/>).</summary>
    public int OctaveSemitones { get; init; }
    public DrumPastePolicy Drums { get; init; } = DrumPastePolicy.RhythmOnOneSound;
    /// <summary>GM drum note used for pitched -> drums rhythm pastes (38 = acoustic snare).</summary>
    public int DrumSound { get; init; } = NoteMapper.DefaultDrumSound;
    /// <summary>Sounding pitch for drums -> pitched rhythm pastes; null = the target's lowest open string.</summary>
    public int? RhythmPitch { get; init; }
}

public enum NoteMapKind { Passthrough, Refretted, KeepStringAndFret, DrumsByMidi, RhythmToDrumSound, RhythmToPitch, Refused }

public enum LeftOutReason { OutOfRange, NoFreeString, StringMissing }

public sealed record LeftOutNote(int BeatIndex, int MidiValue, int SourceString, int SourceFret, LeftOutReason Reason);

/// <summary>What the mapper did, for the status bar and the Paste Special preview. Notes that cannot be placed are
/// left out (skipped, never clamped) and listed in <see cref="LeftOut"/>.</summary>
public sealed class NoteMapReport
{
    public NoteMapKind Kind { get; set; }
    /// <summary>Whole-paste octave shift in semitones (Q2 "shift by an octave").</summary>
    public int OctaveShift { get; set; }
    public int NotesIn { get; set; }
    public int NotesPlaced { get; set; }
    /// <summary>Placed notes whose string or fret changed.</summary>
    public int NotesRefretted { get; set; }
    /// <summary>Notes moved by a further octave into range (ShiftByOctave only).</summary>
    public int NotesOctaveFolded { get; set; }
    /// <summary>Chord notes merged into one hit/note by a rhythm paste.</summary>
    public int NotesMerged { get; set; }
    /// <summary>Techniques, harmonic marks, bends, slides and fingerings removed because they no longer apply.</summary>
    public int TechniquesDropped { get; set; }
    public List<LeftOutNote> LeftOut { get; } = new();

    public string Summary()
    {
        if (Kind == NoteMapKind.Refused) return "Nothing pasted.";
        var parts = new List<string>();
        if (OctaveShift != 0) parts.Add($"shifted {(OctaveShift > 0 ? "up" : "down")} {Math.Abs(OctaveShift) / 12} octave{(Math.Abs(OctaveShift) == 12 ? "" : "s")}");
        if (NotesRefretted > 0) parts.Add($"{NotesRefretted} note{(NotesRefretted == 1 ? "" : "s")} re-fretted");
        if (NotesOctaveFolded > 0) parts.Add($"{NotesOctaveFolded} moved by an octave to fit");
        if (NotesMerged > 0) parts.Add($"{NotesMerged} chord note{(NotesMerged == 1 ? "" : "s")} merged into one sound");
        if (LeftOut.Count > 0) parts.Add($"{LeftOut.Count} note{(LeftOut.Count == 1 ? "" : "s")} out of range left out");
        if (TechniquesDropped > 0) parts.Add($"{TechniquesDropped} technique{(TechniquesDropped == 1 ? "" : "s")} removed");
        return parts.Count == 0 ? "" : string.Join("; ", parts);
    }
}

public sealed record NoteMapResult(IReadOnlyList<TabCell> Beats, NoteMapReport Report);

// Owns: cross-instrument note mapping for paste, pure and deterministic.
// Does not own: the paste placement (BarGrid) and the questions asked.
// Tests: TestNoteMapper, TestPasteCommands.
/// <summary>
/// Cross-instrument note mapping for paste (COPY_PASTE_DESIGN.md 3.4 + owner decisions Q2/Q5). Pure and deterministic:
/// takes cloned beats in time order and returns new beats for the target; never touches a project.
/// </summary>
public static class NoteMapper
{
    public const int DefaultDrumSound = 38; // GM acoustic snare

    private static readonly HashSet<string> DrumValidTechniques = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accent", "HeavyAccent", "Ghost", "Staccato", "Tenuto", "Fermata", TechniqueNames.TremoloPick,
        "GraceBefore", "GraceOnBeat", "Tie", TechniqueNames.FadeIn, TechniqueNames.FadeOut,
    };

    private static readonly string[] SlideTechniques = { "Slide", TechniqueNames.LegatoSlide, TechniqueNames.ShiftSlide };

    /// <summary>The owner questions this paste needs; the combined dialog shows only these. <paramref name="beats"/> is optional
    /// (without it Q2 is decided from the tunings alone).</summary>
    public static PasteQuestions Questions(InstrumentLayout source, InstrumentLayout target, IEnumerable<TabCell>? beats = null,
        PasteMappingMode mode = PasteMappingMode.KeepPitch)
    {
        if (source.IsDrums != target.IsDrums) return PasteQuestions.Drums;
        if (source.IsDrums || mode == PasteMappingMode.KeepStringAndFret || source.SameLayout(target)) return PasteQuestions.None;
        var natural = NaturalOctaves(source, target);
        if (natural != 0) return PasteQuestions.Octave;
        if (beats is null) return PasteQuestions.None;
        var pitches = PitchesOf(beats);
        return BestOctaveShift(pitches, target, natural) != 0 ? PasteQuestions.Octave : PasteQuestions.None;
    }

    /// <summary>The whole-paste octave shift (semitones) "Shift by an octave automatically" would apply.</summary>
    public static int AutoOctaveShift(InstrumentLayout source, InstrumentLayout target, IEnumerable<TabCell> beats) =>
        source.IsDrums || target.IsDrums ? 0 : BestOctaveShift(PitchesOf(beats), target, NaturalOctaves(source, target));

    public static NoteMapResult Map(InstrumentLayout source, InstrumentLayout target, IReadOnlyList<TabCell> beats, NoteMapOptions? options = null)
    {
        options ??= new NoteMapOptions();
        var report = new NoteMapReport();
        foreach (var beat in beats) report.NotesIn += beat.Notes.Count;

        if (source.IsDrums && target.IsDrums) return new(MapDrumsToDrums(target, beats, report), report);
        if (source.IsDrums != target.IsDrums)
        {
            if (options.Drums == DrumPastePolicy.DontPaste) { report.Kind = NoteMapKind.Refused; return new(Array.Empty<TabCell>(), report); }
            return new(target.IsDrums ? RhythmToDrum(target, beats, options, report) : RhythmToPitch(target, beats, options, report), report);
        }
        if (target.StringCount == 0) { report.Kind = NoteMapKind.Refused; return new(Array.Empty<TabCell>(), report); }
        if (source.SameLayout(target) && (options.OctaveSemitones == 0 || options.Mode == PasteMappingMode.KeepStringAndFret))
            return new(Passthrough(target, beats, report, NoteMapKind.Passthrough), report);
        if (options.Mode == PasteMappingMode.KeepStringAndFret) return new(Passthrough(target, beats, report, NoteMapKind.KeepStringAndFret), report);
        return new(Refret(source, target, beats, options, report), report);
    }

    // ---------- same layout / keep string+fret ----------

    private static List<TabCell> Passthrough(InstrumentLayout target, IReadOnlyList<TabCell> beats, NoteMapReport report, NoteMapKind kind)
    {
        report.Kind = kind;
        var result = new List<TabCell>(beats.Count);
        for (var b = 0; b < beats.Count; b++)
        {
            var cell = beats[b].Clone();
            cell.Notes = new List<TabNote>();
            foreach (var src in beats[b].Notes)
            {
                if (src.StringIndex < 0 || src.StringIndex >= target.StringCount)
                { report.LeftOut.Add(new(b, src.MidiValue, src.StringIndex, src.Fret, LeftOutReason.StringMissing)); continue; }
                if (src.Fret < 0 || src.Fret > target.MaxFret)
                { report.LeftOut.Add(new(b, src.MidiValue, src.StringIndex, src.Fret, LeftOutReason.OutOfRange)); continue; }
                var note = src.Clone();
                if (kind == NoteMapKind.KeepStringAndFret)
                {
                    // Same string and fret on another tuning: the sounding pitch follows the target's tuning.
                    var delta = target.PitchOf(note.StringIndex, note.Fret) - note.MidiValue;
                    note.MidiValue += delta;
                    if (delta != 0) ShiftTargets(note, delta);
                }
                cell.Notes.Add(note);
                report.NotesPlaced++;
            }
            result.Add(cell);
        }
        return result;
    }

    private static void ShiftTargets(TabNote note, int delta)
    {
        if (note.SlideTargetMidi != 0) note.SlideTargetMidi += delta;
        if (note.TrillTargetMidi != 0) note.TrillTargetMidi += delta;
    }

    // ---------- keep pitch, re-fret ----------

    private static List<TabCell> Refret(InstrumentLayout source, InstrumentLayout target, IReadOnlyList<TabCell> beats, NoteMapOptions options, NoteMapReport report)
    {
        report.Kind = NoteMapKind.Refretted;
        var shift = (options.Octave == OctavePolicy.ShiftByOctave ? AutoOctaveShift(source, target, beats) : 0) + options.OctaveSemitones;
        report.OctaveShift = shift;
        double? anchor = null;
        var lastStringOfPitch = new Dictionary<int, int>();
        var result = new List<TabCell>(beats.Count);

        for (var b = 0; b < beats.Count; b++)
        {
            var srcCell = beats[b];
            var cell = srcCell.Clone();
            cell.Notes = new List<TabNote>();
            if (srcCell.Notes.Count == 0) { result.Add(cell); continue; }
            anchor ??= Math.Clamp(MeanFret(srcCell.Notes.Where(n => n.Fret > 0).Select(n => (double)n.Fret)) ?? 0, 0, target.MaxFret);

            // Pitch per note after the whole-paste shift (and per-note folding when the user chose octave shifting).
            var pitches = new int[srcCell.Notes.Count];
            var fits = new bool[srcCell.Notes.Count];
            for (var i = 0; i < pitches.Length; i++)
            {
                var p = srcCell.Notes[i].MidiValue + shift;
                if (!target.FitsAnywhere(p) && options.Octave == OctavePolicy.ShiftByOctave)
                {
                    var folded = Fold(p, target);
                    if (folded is int f) { p = f; report.NotesOctaveFolded++; }
                }
                pitches[i] = p;
                fits[i] = target.FitsAnywhere(p);
            }

            var main = Enumerable.Range(0, pitches.Length).Where(i => !srcCell.Notes[i].IsGraceNote && fits[i]).ToList();
            var assignment = AssignStrings(main, pitches, srcCell.Notes, source, target, anchor.Value, lastStringOfPitch);
            var placedFrets = new List<double>();

            for (var i = 0; i < pitches.Length; i++)
            {
                var src = srcCell.Notes[i];
                int s;
                if (!fits[i]) { report.LeftOut.Add(new(b, src.MidiValue, src.StringIndex, src.Fret, LeftOutReason.OutOfRange)); continue; }
                if (src.IsGraceNote) s = BestSingleString(pitches[i], target, anchor.Value);
                else if (!assignment.TryGetValue(i, out s)) { report.LeftOut.Add(new(b, src.MidiValue, src.StringIndex, src.Fret, LeftOutReason.NoFreeString)); continue; }

                var note = src.Clone();
                var delta = pitches[i] - src.MidiValue;
                note.MidiValue = pitches[i];
                if (delta != 0) ShiftTargets(note, delta);
                note.StringIndex = s;
                note.Fret = target.FretOf(s, pitches[i]);
                PruneAfterRefret(src, note, source, target, report);
                if (note.StringIndex != src.StringIndex || note.Fret != src.Fret) report.NotesRefretted++;
                report.NotesPlaced++;
                cell.Notes.Add(note);
                if (!note.IsGraceNote) { lastStringOfPitch[note.MidiValue] = s; if (note.Fret > 0) placedFrets.Add(note.Fret); }
            }
            if (MeanFret(placedFrets) is double m) anchor = m;
            result.Add(cell);
        }
        return result;
    }

    private static void PruneAfterRefret(TabNote src, TabNote note, InstrumentLayout source, InstrumentLayout target, NoteMapReport report)
    {
        var fretDelta = note.Fret - src.Fret;
        var sameOpen = src.StringIndex >= 0 && src.StringIndex < source.StringCount && source.OpenPitch(src.StringIndex) == target.OpenPitch(note.StringIndex);
        if (note.HarmonicFret is double h)
        {
            if (note.Techniques.Contains(TechniqueNames.Harmonic))
            {
                // A natural harmonic depends on the open string: keep it only on a string with the same open pitch.
                if (!sameOpen) { note.HarmonicFret = null; note.Techniques.Remove(TechniqueNames.Harmonic); report.TechniquesDropped++; }
            }
            else if (fretDelta != 0) note.HarmonicFret = h + fretDelta; // artificial/tapped/pinch: relative to the fretted note
        }
        if (fretDelta != 0 && note.LeftHandFinger is not null) { note.LeftHandFinger = null; report.TechniquesDropped++; }
        if (note.SlideTargetMidi != 0 && !target.Fits(note.StringIndex, note.SlideTargetMidi))
        {
            note.SlideTargetMidi = 0;
            foreach (var t in SlideTechniques) note.Techniques.Remove(t);
            report.TechniquesDropped++;
        }
    }

    private static int? Fold(int pitch, InstrumentLayout target)
    {
        for (var k = 1; k <= 4; k++)
        {
            if (target.FitsAnywhere(pitch - 12 * k)) return pitch - 12 * k;
            if (target.FitsAnywhere(pitch + 12 * k)) return pitch + 12 * k;
        }
        return null;
    }

    private static int BestSingleString(int pitch, InstrumentLayout target, double anchor)
    {
        var best = -1; var bestCost = double.MaxValue;
        for (var s = 0; s < target.StringCount; s++)
        {
            if (!target.Fits(s, pitch)) continue;
            var c = PositionCost(target.FretOf(s, pitch), anchor);
            if (c < bestCost) { bestCost = c; best = s; }
        }
        return best;
    }

    private static double PositionCost(int fret, double anchor) =>
        fret == 0 ? 0 : Math.Abs(fret - anchor) + (fret > 12 ? (fret - 12) * 0.2 : 0);

    /// <summary>One distinct string per chord note, minimising distance from the hand anchor, the chord's fret span and high frets;
    /// a tied note stays on the string of the note it continues. Notes that cannot all be placed drop inner notes first.</summary>
    private static Dictionary<int, int> AssignStrings(List<int> noteIdx, int[] pitches, List<TabNote> srcNotes, InstrumentLayout source,
        InstrumentLayout target, double anchor, Dictionary<int, int> lastStringOfPitch)
    {
        var result = new Dictionary<int, int>();
        if (noteIdx.Count == 0) return result;
        var order = noteIdx.OrderBy(i => pitches[i]).ToList();
        var lowest = order[0]; var highest = order[^1];
        var cand = order.Select(i =>
        {
            var list = new List<(int S, double Cost)>();
            for (var s = 0; s < target.StringCount; s++)
            {
                if (!target.Fits(s, pitches[i])) continue;
                var fret = target.FretOf(s, pitches[i]);
                var c = PositionCost(fret, anchor) + 5; // costs stay >= 0 so the branch-and-bound pruning is exact
                var src = srcNotes[i];
                if (src.StringIndex == s && src.StringIndex < source.StringCount && source.OpenPitch(s) == target.OpenPitch(s)) c -= 5; // the source fingering still exists: keep it
                if (src.Tied && lastStringOfPitch.TryGetValue(pitches[i], out var tiedString) && tiedString != s) c += 50;
                list.Add((s, c));
            }
            return list.OrderBy(x => x.Cost).ToList();
        }).ToList();

        var used = new bool[target.StringCount];
        var current = new int[order.Count];
        var best = new int[order.Count];
        var bestCost = double.MaxValue;
        var budget = 200_000;

        void Search(int k, double cost)
        {
            if (--budget < 0 || cost >= bestCost) return;
            if (k == order.Count)
            {
                var min = int.MaxValue; var max = int.MinValue;
                for (var j = 0; j < order.Count; j++)
                {
                    if (current[j] < 0) continue;
                    var f = target.FretOf(current[j], pitches[order[j]]);
                    if (f == 0) continue;
                    min = Math.Min(min, f); max = Math.Max(max, f);
                }
                var span = max >= min ? max - min : 0;
                var total = cost + span * 0.3 + Math.Max(0, span - 4) * 3;
                if (total < bestCost) { bestCost = total; Array.Copy(current, best, current.Length); }
                return;
            }
            foreach (var (s, c) in cand[k])
            {
                if (used[s]) continue;
                used[s] = true; current[k] = s;
                Search(k + 1, cost + c);
                used[s] = false;
            }
            current[k] = -1; // leave this note out: inner notes before the outer ones
            Search(k + 1, cost + (order[k] == lowest || order[k] == highest ? 2000 : 1000));
        }
        Search(0, 0);
        if (bestCost == double.MaxValue) return result;
        for (var j = 0; j < order.Count; j++) if (best[j] >= 0) result[order[j]] = best[j];
        return result;
    }

    // ---------- octave choice ----------

    private static int NaturalOctaves(InstrumentLayout source, InstrumentLayout target) =>
        source.StringCount == 0 || target.StringCount == 0 ? 0
            : (int)Math.Round((target.LowestOpen - source.LowestOpen) / 12.0, MidpointRounding.AwayFromZero);

    private static List<int> PitchesOf(IEnumerable<TabCell> beats) => beats.SelectMany(c => c.Notes).Select(n => n.MidiValue).ToList();

    /// <summary>The octave shift (semitones) that fits the most notes into the target; ties go to the shift nearest the
    /// instruments' register difference (guitar -> bass = -12).</summary>
    private static int BestOctaveShift(List<int> pitches, InstrumentLayout target, int naturalOctaves)
    {
        if (pitches.Count == 0) return naturalOctaves * 12;
        var bestK = 0; var bestFit = -1; var bestDist = int.MaxValue;
        for (var k = -4; k <= 4; k++)
        {
            var fit = pitches.Count(p => target.FitsAnywhere(p + 12 * k));
            var dist = Math.Abs(k - naturalOctaves) * 10 + Math.Abs(k);
            if (fit > bestFit || (fit == bestFit && dist < bestDist)) { bestK = k; bestFit = fit; bestDist = dist; }
        }
        return bestK * 12;
    }

    // ---------- drums ----------

    private static TrackModel DrumTrack(InstrumentLayout layout) => new()
    {
        Kind = TrackKind.Drums, DrumMapPreset = layout.DrumMapPreset,
        CustomDrumMap = layout.CustomDrumMap?.Select(e => e.Clone()).ToList(), StringTunings = layout.StringTunings.ToList()
    };

    private static int DrumLine(TrackModel drumTrack, InstrumentLayout layout, int midi) =>
        Math.Clamp(DrumMaps.For(drumTrack, midi).TabLine, 0, Math.Max(0, layout.StringCount - 1));

    private static List<TabCell> MapDrumsToDrums(InstrumentLayout target, IReadOnlyList<TabCell> beats, NoteMapReport report)
    {
        report.Kind = NoteMapKind.DrumsByMidi;
        var track = DrumTrack(target);
        var result = new List<TabCell>(beats.Count);
        foreach (var beat in beats)
        {
            var cell = beat.Clone();
            foreach (var note in cell.Notes)
            {
                var midi = note.MidiValue > 0 ? note.MidiValue : note.Fret;
                note.MidiValue = midi; note.Fret = midi;
                note.StringIndex = DrumLine(track, target, midi);
                report.NotesPlaced++;
            }
            result.Add(cell);
        }
        return result;
    }

    private static List<TabCell> RhythmToDrum(InstrumentLayout target, IReadOnlyList<TabCell> beats, NoteMapOptions options, NoteMapReport report)
    {
        report.Kind = NoteMapKind.RhythmToDrumSound;
        var track = DrumTrack(target);
        var midi = Math.Clamp(options.DrumSound, 0, 127);
        var line = DrumLine(track, target, midi);
        var result = new List<TabCell>(beats.Count);
        foreach (var beat in beats)
        {
            var cell = beat.Clone();
            cell.Notes = new List<TabNote>();
            cell.WhammyPoints = new List<BendPointModel>();
            var hits = beat.Notes.Where(n => !n.IsGraceNote).ToList();
            // A beat whose notes all continue a tie is a sustain, not a new hit: it stays silent on drums.
            if (hits.Count > 0 && !hits.All(n => n.Tied))
            {
                var loudest = hits.OrderByDescending(n => n.Velocity).First();
                var hit = new TabNote { StringIndex = line, Fret = midi, MidiValue = midi, Velocity = loudest.Velocity, Ghost = hits.All(n => n.Ghost) };
                foreach (var t in loudest.Techniques) if (DrumValidTechniques.Contains(t)) hit.Techniques.Add(t);
                report.TechniquesDropped += loudest.Techniques.Count - hit.Techniques.Count;
                cell.Notes.Add(hit);
                report.NotesPlaced++;
                report.NotesMerged += hits.Count - 1;
            }
            else report.NotesMerged += hits.Count;
            report.NotesMerged += beat.Notes.Count - hits.Count; // grace notes fold into the hit
            result.Add(cell);
        }
        return result;
    }

    private static List<TabCell> RhythmToPitch(InstrumentLayout target, IReadOnlyList<TabCell> beats, NoteMapOptions options, NoteMapReport report)
    {
        report.Kind = NoteMapKind.RhythmToPitch;
        if (target.StringCount == 0) { report.Kind = NoteMapKind.Refused; return new List<TabCell>(); }
        var lowString = Enumerable.Range(0, target.StringCount).OrderBy(s => target.OpenPitch(s)).First();
        var pitch = options.RhythmPitch ?? target.OpenPitch(lowString);
        var s0 = target.Fits(lowString, pitch) ? lowString : BestSingleString(pitch, target, 0);
        var result = new List<TabCell>(beats.Count);
        for (var b = 0; b < beats.Count; b++)
        {
            var beat = beats[b];
            var cell = beat.Clone();
            cell.Notes = new List<TabNote>();
            var hits = beat.Notes.Where(n => !n.IsGraceNote).ToList();
            if (hits.Count > 0)
            {
                if (s0 < 0) { report.LeftOut.Add(new(b, pitch, -1, -1, LeftOutReason.OutOfRange)); result.Add(cell); continue; }
                var loudest = hits.OrderByDescending(n => n.Velocity).First();
                var note = new TabNote { StringIndex = s0, Fret = target.FretOf(s0, pitch), MidiValue = pitch, Velocity = loudest.Velocity, Ghost = hits.All(n => n.Ghost) };
                foreach (var t in loudest.Techniques) note.Techniques.Add(t);
                cell.Notes.Add(note);
                report.NotesPlaced++;
                report.NotesMerged += hits.Count - 1;
            }
            report.NotesMerged += beat.Notes.Count - hits.Count;
            result.Add(cell);
        }
        return result;
    }

    private static double? MeanFret(IEnumerable<double> frets)
    {
        double sum = 0; var n = 0;
        foreach (var f in frets) { sum += f; n++; }
        return n == 0 ? null : sum / n;
    }
}
