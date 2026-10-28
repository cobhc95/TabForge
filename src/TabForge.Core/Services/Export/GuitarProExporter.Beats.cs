using System.IO;
using AlphaTab.Model;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the beats of the Guitar Pro exporter: one beat per cell (a grace beat before its principal), its effects and its notes with their slides, harmonics, bends and drum articulations.
// Does not own: the score build and the master bars (GuitarProExporter.cs) or the pure helpers (GuitarProExporter.Helpers.cs).
// Tests: TestGpRoundTripCounts, TestGuitarProImportWorker.
public static partial class GuitarProExporter
{
    /// <summary>
    /// A cell that holds grace notes together with its principal notes (how the importer stores an ornamented
    /// beat) becomes two beats: a grace beat, then the principal beat with its own duration and dots. Writing
    /// the whole cell as one grace beat used to drop the principal duration and shift the rest of the bar.
    /// </summary>
    private static IEnumerable<Beat> BeatsFor(TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, Staff staff, Dictionary<string, string> chordIds, Dictionary<string, bool> customByCurve, (int Sounding, int Bake) shift)
    {
        var graces = cell.Notes.Where(n => n.IsGraceNote).ToList();
        if (graces.Count == 0 || graces.Count == cell.Notes.Count)
        {
            yield return BeatFor(cell, source, drums, track, articulations, staff, chordIds, customByCurve, shift);
            yield break;
        }
        var lead = graces[0];
        var graceSlots = lead.GraceDurationSlots > 0 ? lead.GraceDurationSlots : 1;
        var grace = new TabCell
        {
            Notes = graces.Select(n => n.Clone()).ToList(),
            DurationDenominator = graceSlots >= 2 ? 8 : graceSlots >= 1 ? 16 : graceSlots >= 0.5 ? 32 : 64,
            IsGrace = true,
            GraceBeforeBeat = lead.GraceBeforeBeat,
        };
        yield return BeatFor(grace, source, drums, track, articulations, staff, chordIds, customByCurve, shift);
        var principal = cell.Clone();
        principal.Notes = principal.Notes.Where(n => !n.IsGraceNote).ToList();
        principal.IsGrace = false;
        yield return BeatFor(principal, source, drums, track, articulations, staff, chordIds, customByCurve, shift);
    }

    private static Beat BeatFor(TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, Staff staff, Dictionary<string, string> chordIds, Dictionary<string, bool> customByCurve, (int Sounding, int Bake) shift)
    {
        var beat = BeatHead(cell, staff, chordIds);
        AddBeatEffects(beat, cell);
        // Mix-table volume and pan changes are written into the file afterwards (GpifAutomationWriter; alphaTab's GP7 writer emits only tempo automations
        // and the track's initial sound). A program change on a beat is not written. See docs/R5_CAPABILITY (A11).
        if (cell.IsRest || cell.Notes.Count == 0) return beat;
        foreach (var n in cell.Notes) AddNote(beat, n, cell, source, drums, track, articulations, shift, customByCurve);
        if (cell.Notes.Any(n => TechniqueNames.HasPalmMute(n.Techniques))) beat.IsPalmMute = true;
        return beat;
    }

    /// <summary>The beat itself: duration, tuplet, lyrics, chord, fermata, grace type and whammy bar.</summary>
    private static Beat BeatHead(TabCell cell, Staff staff, Dictionary<string, string> chordIds)
    {
        var beat = new Beat
        {
            Duration = DurationOf(cell.DurationDenominator),
            Dots = cell.Dots,
            IsEmpty = false,
            Text = cell.Text ?? "",
        };
        if (cell.Tuplet is var (num, den) && num > 0) { beat.TupletNumerator = num; beat.TupletDenominator = den; }
        if (!string.IsNullOrWhiteSpace(cell.Lyrics)) beat.Lyrics = cell.Lyrics.Split('\n').ToList();
        if (!string.IsNullOrWhiteSpace(cell.ChordName))
        {
            if (!chordIds.TryGetValue(cell.ChordName!, out var chordId))
            {
                chordId = "c" + chordIds.Count;
                chordIds[cell.ChordName!] = chordId;
                staff.AddChord(chordId, new Chord { Name = cell.ChordName!, ShowName = true, ShowDiagram = false, ShowFingering = false });
            }
            beat.ChordId = chordId;
        }
        if (cell.Fermata || cell.Notes.Any(n => n.Techniques.Contains("Fermata"))) beat.Fermata = new Fermata { Type = FermataType.Medium, Length = 1 };
        // Tenuto is AccentuationType.Tenuto on the notes (see below).
        // GP7 knows only before-beat and on-beat graces: alphaTab's GP7 writer drops a BendGrace entirely (the grace became
        // a normal 32nd that pushed the principal note late), so a bend grace is written as a before-beat grace; its bend stays on the note.
        var graceTech = cell.Notes.SelectMany(n => n.Techniques).ToList();
        if (cell.IsGrace || graceTech.Contains("GraceBefore"))
            beat.GraceType = cell.IsGrace && !cell.GraceBeforeBeat && !graceTech.Contains("GraceBend") ? GraceType.OnBeat : GraceType.BeforeBeat;
        else if (graceTech.Contains("GraceOnBeat")) beat.GraceType = GraceType.OnBeat;
        else if (graceTech.Contains("GraceBend") && cell.Notes.All(n => n.IsGraceNote)) beat.GraceType = GraceType.BeforeBeat;
        var whammyName = cell.Notes.SelectMany(n => n.Techniques).FirstOrDefault(x => x.StartsWith("TremBar", StringComparison.Ordinal) && x.Length > 7);
        var hasTremBar = cell.Notes.Any(n => n.Techniques.Contains("TremBar"));
        if (cell.WhammyPoints.Count > 0 || hasTremBar || whammyName is not null)
        {
            beat.WhammyBarType = whammyName is not null && Enum.TryParse<WhammyType>(whammyName[7..], out var wt) ? wt : WhammyType.Custom;
            var pts = cell.WhammyPoints.Count > 0 ? cell.WhammyPoints.Select(p => (p.Offset, p.Value)).ToList() : new List<(double, double)> { (0, 0), (30, -4), (60, 0) };
            foreach (var (o, v) in SimplifyWhammy(pts)) beat.AddWhammyBarPoint(new BendPoint(o, v));
        }
        return beat;
    }

    /// <summary>The playing effects of the beat: tremolo picking, tap, slap, pop, fade, wah, brush, pick stroke, legato, rasgueado and ottava.</summary>
    private static void AddBeatEffects(Beat beat, TabCell cell)
    {
        var all = cell.Notes.SelectMany(n => n.Techniques).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Tremolo picking as slashes (1 = 1/8, 2 = 1/16, 3 = 1/32, GP7's range): the TremoloSpeed setter did not round-trip
        // (a 1/16 came back as 1/4 or 1/8 depending on the beat); 1/64 has no GP7 spelling and is written as 1/32.
        if (cell.TremoloPickDenominator > 0 || all.Contains("TremoloPick"))
        {
            var speed = cell.TremoloPickDenominator > 0 ? cell.TremoloPickDenominator : 8;
            beat.TremoloPicking = new TremoloPickingEffect { Marks = speed >= 32 ? 3 : speed >= 16 ? 2 : 1 };
        }
        if (all.Contains("Tapping")) beat.Tap = true;
        if (all.Contains("DeadSlapped")) beat.DeadSlapped = true;
        if (all.Contains("Slap") && !all.Contains("DeadSlapped")) beat.Slap = true;
        if (all.Contains("Pop")) beat.Pop = true;
        // The value of each effect is the technique row's GpId (TechniqueInfo); the order of the names is the precedence when a beat carries more than one.
        if (TechniqueInfo.GpValue<FadeType>(all, "Fade", "FadeIn", "FadeOut") is { } fade) beat.Fade = fade;
        if (TechniqueInfo.GpValue<WahPedal>(all, "WahPedal", "WahOpen", "WahClose") is { } wah) beat.WahPedal = wah;
        // The importer tags an arpeggio stroke with BOTH its Arpeggio* name and a Brush* name, so the arpeggio must be tested first.
        if (TechniqueInfo.GpValue<BrushType>(all, "BrushType", "ArpeggioDown", "ArpeggioUp") is { } arpeggio) beat.BrushType = arpeggio;
        else if (all.Contains("BrushDown")) beat.BrushType = BrushType.BrushDown; else if (all.Contains("BrushUp")) beat.BrushType = BrushType.BrushUp;
        // The stroke's spread: the importer reads BrushDuration back as 3 string steps (ticks / 3 / 240 = BrushStepSlots).
        // No spread set: none is written, so the reopened stroke keeps the player's default spread (a fixed 60 ticks made it faster).
        if (beat.BrushType != BrushType.None && double.IsFinite(cell.BrushStepSlots) && cell.BrushStepSlots > 0)
            beat.BrushDuration = Math.Round(cell.BrushStepSlots * 3 * 240);
        if (all.Contains("PickDown")) beat.PickStroke = PickStroke.Down; else if (all.Contains("PickUp")) beat.PickStroke = PickStroke.Up;
        // Legato is a slur from this beat to the next (alphaTab derives the destination from the previous beat); a rasgueado keeps its pattern ("Rasgueado<pattern>"), ii_1 when it has none.
        if (all.Contains("Legato")) beat.IsLegatoOrigin = true;
        if (all.Contains("Rasgueado"))
        {
            var pattern = all.FirstOrDefault(x => x.StartsWith("Rasgueado", StringComparison.OrdinalIgnoreCase) && x.Length > 9);
            beat.Rasgueado = pattern is not null && Enum.TryParse<Rasgueado>(pattern[9..], true, out var parsed) && parsed != Rasgueado.None ? parsed : Rasgueado.Ii;
        }
        beat.Ottava = cell.OctaveShiftSemitones switch
        {
            >= 24 => Ottavia._15ma, >= 12 => Ottavia._8va, <= -24 => Ottavia._15mb, <= -12 => Ottavia._8vb, _ => Ottavia.Regular
        };
    }

    /// <summary>One note of the beat with its flags, position (string and fret, or drum articulation), slides, dynamics, harmonics, trill and bend.</summary>
    private static void AddNote(Beat beat, TabNote n, TabCell cell, TrackModel source, bool drums, Track track, Dictionary<int, int> articulations, (int Sounding, int Bake) shift, Dictionary<string, bool> customByCurve)
    {
        var t = n.Techniques;
        var note = new Note
        {
            IsDead = n.Dead, IsGhost = n.Ghost, IsTieDestination = n.Tied || cell.IsTied,
            IsPalmMute = TechniqueNames.HasPalmMute(t), IsLetRing = t.Contains("LetRing"),
            IsStaccato = cell.Staccato && !n.Ghost,   // staccato is an <Accent> bit too: Guitar Pro 8 drops a ghost mark when the note has any <Accent> (re-save probe), so it goes on the plain notes of the chord
            IsHammerPullOrigin = t.Contains("HOPOOrigin") || t.Contains("HOPO") && !t.Contains("HOPODestination"),
            Vibrato = t.Contains("WideVibrato") ? VibratoType.Wide : t.Contains("Vibrato") ? VibratoType.Slight : VibratoType.None,
            // GP keeps one mark of the three per note: heavy accent, accent, tenuto (alphaTab's AccentuationType.Tenuto; the importer reads it back onto the beat).
            // A ghost note carries none of them, nor staccato: Guitar Pro 8 keeps a ghost mark only on a note with no <Accent> element at all (re-saves of a note written
            // with the ghost mark plus each of staccato, accent, heavy accent, tenuto and two pairs kept the mark and dropped the ghost mark every time; the ghost mark
            // alone survived; no ghost note of 294 in real .gp files has an <Accent>), and the ghost mark is the per-note fact. The beat's marks stay on the other
            // notes of the chord; only a beat whose notes are all ghost loses them (GpExportPreflight reports that).
            Accentuated = n.Ghost ? AccentuationType.None : cell.Accent == 2 ? AccentuationType.Heavy : cell.Accent == 1 ? AccentuationType.Normal : cell.Tenuto || t.Contains("Tenuto") ? AccentuationType.Tenuto : AccentuationType.None,
            LeftHandFinger = FingerFor(n.LeftHandFinger), RightHandFinger = FingerFor(n.RightHandFinger),
        };
        AssignNotePosition(note, n, t, source, track, drums, articulations, shift);
        SetSlideTypes(note, t);
        // Loudness through the shared dynamics table (ppp..fff); a beat carries the first note's dynamic.
        var dynamic = ToDynamicValue(TabForge.Models.Dynamics.NearestIndex(n.Velocity));
        note.Dynamics = dynamic;
        if (ReferenceEquals(n, cell.Notes[0])) beat.Dynamics = dynamic;
        if (TechniqueInfo.GpValue<HarmonicType>(t, "HarmonicType", "PinchHarmonic", "ArtificialHarmonic", "TapHarmonic", "SemiHarmonic", "FeedbackHarmonic", "Harmonic") is { } harmonic)
        {
            note.HarmonicType = harmonic;
            note.HarmonicValue = n.HarmonicFret ?? (harmonic == HarmonicType.Natural ? n.Fret : 12);
        }
        if (t.Contains("LeftTap")) note.IsLeftHandTapped = true;
        if (t.Contains("Trill"))
        {
            note.TrillValue = Math.Clamp((n.TrillTargetMidi > 0 ? n.TrillTargetMidi : n.MidiValue + 2) + shift.Sounding, 0, MidiLimits.Max);   // an absolute pitch: follows the track transpose
            note.TrillSpeed = DurationOf(n.TrillDurationDenominator > 0 ? n.TrillDurationDenominator : 16);
        }
        var bendScale = 60 / BendPointModel.SpanOf(n.BendPoints);
        foreach (var (offset, value) in SimplifyBend(n.BendPoints.Select(p => (p.Offset * bendScale, p.Value)).ToList(), customByCurve))
            note.AddBendPoint(new BendPoint(offset, value));
        // alphaTab writes only a midpoint or peak middle stretch; the curve's own fit is written into the file afterwards (GuitarProBendCurve.Patch).
        if (!drums && GuitarProBendCurve.Fit(GuitarProBendCurve.Normalise(n.BendPoints.Select(p => (p.Offset, p.Value)))) is { } bendFit) GuitarProBendCurve.Plan(note, bendFit);
        beat.AddNote(note);
    }

    private static void SetSlideTypes(Note note, HashSet<string> t)
    {
        if (t.Contains("ShiftSlide")) note.SlideOutType = SlideOutType.Shift;
        else if (t.Contains("LegatoSlide") || t.Contains("Slide")) note.SlideOutType = SlideOutType.Legato;
        else if (t.Contains("SlideOutDown")) note.SlideOutType = SlideOutType.OutDown;
        else if (t.Contains("SlideOutUp")) note.SlideOutType = SlideOutType.OutUp;
        else if (t.Contains("PickSlideDown")) note.SlideOutType = SlideOutType.PickSlideDown;
        else if (t.Contains("PickSlideUp")) note.SlideOutType = SlideOutType.PickSlideUp;
        if (t.Contains("SlideInBelow")) note.SlideInType = SlideInType.IntoFromBelow;
        else if (t.Contains("SlideInAbove")) note.SlideInType = SlideInType.IntoFromAbove;
    }

    /// <summary>Where the note sits: a drum articulation, or string and fret (moved when the sounding pitch is not the written one or the track is transposed).</summary>
    private static void AssignNotePosition(Note note, TabNote n, HashSet<string> t, TrackModel source, Track track, bool drums, Dictionary<int, int> articulations, (int Sounding, int Bake) shift)
    {
        if (drums)
        {
            var midi = n.MidiValue > 0 ? n.MidiValue : n.Fret;
            if (!articulations.TryGetValue(midi, out var index))
            {
                index = track.PercussionArticulations.Count;
                // the format's own definition for this GM sound (name, staff line, notehead), so GP7/8 draws
                // and names the drum like its own files; a generic entry only for sounds it does not know.
                track.PercussionArticulations.Add(GpArticulations.Get(midi) is { } known && (int)known.OutputMidiNumber == midi
                    ? known
                    : new InstrumentArticulation { Id = midi, OutputMidiNumber = midi, ElementType = "Drum", StaffLine = 3 });
                articulations[midi] = index;
            }
            note.PercussionArticulation = index;
        }
        else
        {
            var (stringIndex, fret) = (n.StringIndex, n.Fret);
            // A Guitar Pro file holds pitch only as string tuning + capo + fret. A note whose sounding pitch is not that (a track whose
            // tuning the source did not give, so the pitch came from the file's own note value) is written where it sounds right: it
            // used to reopen 16 semitones low. Harmonics carry the harmonic's pitch, so they stay; a tie destination is moved exactly like its origin (an unmoved one kept an out-of-range fret that could not be re-imported).
            if (n.MidiValue > 0 && !n.Dead && n.HarmonicFret is null && !t.Any(x => x.Contains("Harmonic", StringComparison.Ordinal))
                && source.StringTunings.Count > 0 && source.PitchOf(stringIndex, fret) is var written && written != n.MidiValue)
                (stringIndex, fret) = TransposedPosition(source, stringIndex, fret, n.MidiValue - written);
            if (shift.Bake != 0) (stringIndex, fret) = TransposedPosition(source, stringIndex, fret, shift.Bake);
            note.String = Math.Max(1, source.StringTunings.Count - stringIndex);
            note.Fret = fret;
        }
    }
}
