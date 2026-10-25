using System.Globalization;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationArcs;

namespace TabForge.Views;

// Owns: the rhythm and geometry of one measure on the staff: beats, beam groups, stems, accidentals and the mark skyline, consumed
//   by both score staves.
// Does not own: drawing (StaffNotationDrawing), arcs and ties (StaffNotationArcs) and score edits (Views/Score).
// Tests: no dedicated test class.
/// <summary>Resolves a measure's musical events into beats, beam groups, stems, accidentals and the mark skyline.</summary>
internal static partial class StaffNotationLayoutBuilder
{
    /// <summary>Creates the single rhythmic/geometry layout consumed by both score staves.</summary>
    internal static StaffNotationMeasureLayout CreateLayout(
        TrackModel? track,
        MeasureModel measure,
        int measureIndex,
        int slots,
        double x,
        double staffTop,
        double slotWidth,
        int numerator = 4,
        int denominator = 4,
        int keySignature = 0,
        double staffScale = 1.0,
        IReadOnlyList<TabCell>? cellsOverride = null,
        Func<double, double>? centerOf = null)
    {
        staffScale = Math.Clamp(double.IsFinite(staffScale) ? staffScale : 1.0, 0.5, 2.0);
        var isDrum = track?.Kind == TrackKind.Drums;
        var staffBottom = staffTop + 4 * StaffGap;
        var clef = ClefInfo.From(measure.Clef);
        var keyAlterations = KeyAlterations(keySignature);
        var sourceCells = cellsOverride ?? measure.Cells;
        var voiceIndex = ReferenceEquals(sourceCells, measure.Voice2Cells) ? 1 : 0;
        var twoVoices = measure.Voice2Cells.Any(c => c.Notes.Count > 0);
        var beats = BuildBeats(track, measure, measureIndex, sourceCells, voiceIndex, twoVoices, isDrum, clef, keyAlterations, keySignature, x, staffTop, slotWidth, centerOf);

        if (!isDrum) beats = FoldSameOnsetCells(beats);
        beats.Sort((a, b) =>
        {
            var onset = a.StartSlots.CompareTo(b.StartSlots);
            return onset != 0 ? onset : a.CellIndex.CompareTo(b.CellIndex);
        });

        // An overfull bar has beats past its nominal end; the metric pattern covers them too.
        var coveredSlots = beats.Count == 0 ? slots : Math.Max(slots, (int)Math.Ceiling(beats.Max(beat => beat.StartSlots + beat.DurationSlots)));
        var metricGroups = BuildMetricGroups(numerator, denominator, coveredSlots);
        var beamGroups = isDrum ? new List<StaffNotationBeamGroup>() : BuildBeamGroups(beats, metricGroups);
        AssignBeamStemDirections(beamGroups, staffTop + MiddleLineOffset);
        AssignNoteheadsAndStems(beats, staffTop, staffBottom, isDrum);
        var drumStems = new List<StaffNotationBeat>();
        List<StaffNotationBeamSegment> beamSegments;
        if (isDrum) { drumStems = StaffDrumStems.Build(beats, metricGroups, out beamGroups, out beamSegments); }
        else beamSegments = BuildBeamSegments(beamGroups);
        var tuplets = BuildTupletGroups(beats, beamGroups);
        ResolveAccidentals(beats, keyAlterations);
        AssignAccidentalColumns(beats);
        var ties = BuildTies(track, measureIndex, slots, beats, voiceIndex, staffTop, x, x + slotWidth * Math.Max(1, slots));
        var slurs = BuildHopoSlurs(beats, staffTop);

        return new StaffNotationMeasureLayout
        {
            StaffTop = staffTop,
            SlotWidth = slotWidth,
            StaffScale = staffScale,
            Right = x + slotWidth * Math.Max(1, slots),
            Beats = beats,
            BeamGroups = beamGroups,
            BeamSegments = beamSegments,
            DrumStems = drumStems,
            TupletGroups = tuplets,
            Ties = ties,
            HopoSlurs = slurs,
            Slides = BuildSlideStrokes(track, measureIndex, voiceIndex, beats, staffTop),
            IsSecondVoice = voiceIndex == 1
        };
    }

    /// <summary>One beat per cell that holds notes, a rest or an annotation: its position, flags and the written notes with their pitch spelling and height on the staff.</summary>
    private static List<StaffNotationBeat> BuildBeats(TrackModel? track, MeasureModel measure, int measureIndex, IReadOnlyList<TabCell> sourceCells, int voiceIndex, bool twoVoices, bool isDrum, ClefInfo clef, int[] keyAlterations, int keySignature, double x, double staffTop, double slotWidth, Func<double, double>? centerOf)
    {
        var beats = new List<StaffNotationBeat>();
        var staffBottom = staffTop + 4 * StaffGap;
        var inferredCursor = 0.0;
        var otherVoiceOnsets = isDrum ? StaffDrumStems.NoteOnsets(voiceIndex == 1 ? measure.Cells : measure.Voice2Cells) : null;
        for (var i = 0; i < sourceCells.Count; i++)
        {
            var cell = sourceCells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation) continue;

            var rawStart = cell.RhythmicPosition ?? Math.Max(i, inferredCursor);
            var start = Math.Max(0, double.IsFinite(rawStart) ? rawStart : i);
            var duration = MusicTime.CellSlots(cell);
            inferredCursor = Math.Max(inferredCursor, start + duration);
            if (otherVoiceOnsets is not null && cell.Notes.Count == 0 && otherVoiceOnsets.Any(o => Math.Abs(o - start) < PositionEpsilon)) continue;   // a percussion rest never lands on the other voice's heads
            var centerX = centerOf?.Invoke(start) ?? x + (start + 0.5) * slotWidth;
            var notes = new List<StaffNotationNote>();
            var graceNotes = new List<StaffNotationNote>();
            var hasMainNote = cell.Notes.Any(candidate => !candidate.IsGraceNote);
            if (!isDrum) AddWrittenNotes(track, cell, clef, keyAlterations, keySignature, staffBottom, hasMainNote, notes, graceNotes);

            beats.Add(new StaffNotationBeat
            {
                CellIndex = i,
                CellIndices = new List<int> { i },
                Cell = cell,
                StartSlots = start,
                DurationSlots = duration,
                CenterX = centerX,
                Flags = FlagsFor(cell.DurationDenominator),
                Notes = notes,
                GraceNotes = graceNotes,
                IsDrum = isDrum,
                StaffTop = staffTop,
                DrumMap = isDrum && track is not null ? midi => TabForge.Services.DrumMaps.For(track, midi) : null,
                AutoStemInvert = voiceIndex == 1,
                TwoVoices = twoVoices,
                MinY = staffTop + MiddleLineOffset,
                MaxY = staffTop + MiddleLineOffset
            });
        }
        return beats;
    }

    /// <summary>The written notes of a cell on a pitched staff, split into main and grace notes.</summary>
    private static void AddWrittenNotes(TrackModel? track, TabCell cell, ClefInfo clef, int[] keyAlterations, int keySignature, double staffBottom, bool hasMainNote, List<StaffNotationNote> notes, List<StaffNotationNote> graceNotes)
    {
        var octaveTranspose = clef.OctaveShift;
        if (octaveTranspose == 0 && track?.Kind is TrackKind.Guitar or TrackKind.Bass) octaveTranspose = 12;
        foreach (var note in cell.Notes)
        {
            var fretted = (track?.Kind is TrackKind.Guitar or TrackKind.Bass) &&
                          note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
                ? track.PitchOf(note.StringIndex, note.Fret)
                : 0;
            // Artificial, tap, pinch (pick) and semi (slap) harmonics are written at the fretted note with a diamond head, as in
            // the reference; their stored pitch is the much higher sounding harmonic. A natural harmonic keeps its stored pitch.
            var soundingMidi = IsWrittenAtFret(note.Techniques) && fretted > 0
                ? fretted
                : note.MidiValue > 0 ? note.MidiValue : fretted;
            if (soundingMidi <= 0) continue;
            var writtenMidi = soundingMidi + octaveTranspose;
            var pitch = SpellPitch(writtenMidi, keyAlterations, keySignature);
            var y = staffBottom - (pitch.DiatonicIndex - clef.BottomDiatonicIndex) * (StaffGap / 2.0);
            (note.IsGraceNote && hasMainNote ? graceNotes : notes).Add(new StaffNotationNote
            {
                Source = note,
                WrittenMidi = writtenMidi,
                StaffStep = pitch.DiatonicIndex,
                Letter = pitch.Letter,
                Octave = pitch.Octave,
                Alteration = pitch.Alteration,
                Y = y
            });
        }
    }

    /// <summary>
    /// Folds cells that start within <see cref="PositionEpsilon"/> of each other into one beat (one stem) when their written
    /// shape is identical; anything else (a different duration, tuplet, beam mode, stem, rest, grace or tie) stays separate.
    /// The first cell's beat keeps its index; <see cref="StaffNotationBeat.CellIndices"/> maps every folded cell back to it.
    /// </summary>
    private static List<StaffNotationBeat> FoldSameOnsetCells(List<StaffNotationBeat> beats)
    {
        var ordered = beats.OrderBy(beat => beat.StartSlots).ThenBy(beat => beat.CellIndex).ToList();
        var folded = new List<StaffNotationBeat>(ordered.Count);
        foreach (var beat in ordered)
        {
            StaffNotationBeat? target = null;
            for (var i = folded.Count - 1; i >= 0 && Math.Abs(folded[i].StartSlots - beat.StartSlots) < PositionEpsilon; i--)
                if (SameFoldShape(folded[i], beat)) { target = folded[i]; break; }
            if (target is not null)
            {
                target.Notes.AddRange(beat.Notes);
                target.GraceNotes.AddRange(beat.GraceNotes);
                target.CellIndices.Add(beat.CellIndex);
                continue;
            }
            folded.Add(beat);
        }
        return folded;
    }

    /// <summary>True when two same-onset beats are musically identical apart from their notes, so one beat can hold both.</summary>
    private static bool SameFoldShape(StaffNotationBeat a, StaffNotationBeat b)
    {
        var first = a.Cell;
        var second = b.Cell;
        return first.DurationDenominator == second.DurationDenominator &&
               first.Dots == second.Dots &&
               first.Tuplet == second.Tuplet &&
               first.BeamMode == second.BeamMode &&
               first.BreakSecondaryBeamBefore == second.BreakSecondaryBeamBefore &&
               first.StemDirection == second.StemDirection &&
               first.IsRest == second.IsRest &&
               first.WrittenRest == second.WrittenRest &&
               first.IsGrace == second.IsGrace &&
               first.GraceBeforeBeat == second.GraceBeforeBeat &&
               first.IsTied == second.IsTied &&
               first.HasAnnotation == second.HasAnnotation &&
               first.Fermata == second.Fermata &&
               (a.GraceNotes.Count > 0) == (b.GraceNotes.Count > 0);
    }

    /// <summary>Claims the notation's own ink (heads, accidentals, stems, flags, beams, grace notes) so every mark stacks outside it.</summary>
    internal static void SeedSkyline(StaffNotationMeasureLayout layout)
    {
        var sky = layout.Skyline;
        foreach (var beat in layout.Beats)
        {
            if (beat.IsRest)
            {
                // The rest glyph is ink too (a stack of flags on a 32nd/64th rest reaches well below the staff middle): marks stack outside it.
                var duration = NormalizeDuration(beat.Cell.DurationDenominator);
                var restTop = layout.StaffTop + StaffGap * (duration <= 2 ? (duration <= 1 ? 1.0 : 1.5) : 1.0);
                var restBottom = duration <= 1 ? restTop + StaffGap * 0.5 : duration == 2 ? restTop + StaffGap * 0.5
                    : duration == 4 ? layout.StaffTop + StaffGap + 18
                    : layout.StaffTop + StaffGap * 1.1 + StaffGap * (1.5 + 0.9 * ((duration switch { 8 => 1, 16 => 2, 32 => 3, _ => 4 }) - 1));
                sky.Claim(beat.CenterX - 6.5, beat.CenterX + 6.5 + (beat.Cell.Dots > 0 ? 4 + beat.Cell.Dots * 4 : 0), restTop - 1, restBottom + 1);
                continue;
            }
            foreach (var c in GhostClusters(layout, beat))
                sky.Claim(c.L - HeadRadiusX - 13 - c.Pad, c.R + HeadRadiusX + 8 + c.Pad, c.InkY - c.Half - 1, c.InkY + c.Half + 1);
            foreach (var n in beat.Notes)
            {
                sky.Claim(n.X - HeadRadiusX - 2.2, n.X + HeadRadiusX + 2.2, n.Y - HeadRadiusY - 2.6, n.Y + HeadRadiusY + 2.6);   // the rotated (and outlined) head
                // Ledger lines run from the staff to the head: that column is ink too.
                var staffBottom = layout.StaffTop + 4 * StaffGap;
                if (n.Y > staffBottom + 1) sky.Claim(n.X - HeadRadiusX - 3.5, n.X + HeadRadiusX + 3.5, staffBottom, n.Y + 1);
                else if (n.Y < layout.StaffTop - 1) sky.Claim(n.X - HeadRadiusX - 3.5, n.X + HeadRadiusX + 3.5, n.Y - 1, layout.StaffTop);
                for (var d = 0; d < Math.Clamp(beat.Cell.Dots, 0, 2); d++)
                    sky.Claim(n.X + HeadRadiusX + 1.5 + d * 4, n.X + HeadRadiusX + 6 + d * 4, n.Y - 4.5, n.Y + 2);
                if (n.Accidental is not null)
                {
                    var ax = AccidentalX(layout, beat, n);
                    sky.Claim(ax - 4, ax + 4, n.Y - 8, n.Y + 8);
                }
            }
            if (beat.IsDrum)
                foreach (var y in DrumHeadYs(beat)) sky.Claim(beat.CenterX - 7, beat.CenterX + 7, y - 7, y + 7);
            if (beat.Notes.Count > 0 && beat.Cell.Notes.Any(n => n.Techniques.Any(t => t is "ArpeggioDown" or "ArpeggioUp" or "BrushDown" or "BrushUp")))
            {
                // The arpeggio / brush line and its arrowhead left of the chord (head reaches 5.7 px past the line's end) are ink too.
                var ax = beat.CenterX - 13 - (beat.Notes.Any(n => n.Accidental is not null) ? 8 : 0);
                var ay1 = beat.Notes.Min(n => n.Y) - 4;
                sky.Claim(ax - 3.5, ax + 3.5, ay1 - 6, Math.Max(beat.Notes.Max(n => n.Y) + 4, ay1 + 9) + 6);
            }
            if ((beat.Cell.Staccato || beat.Cell.Tenuto) && beat.Notes.Count > 0)
            {
                var below = !beat.HasStem || beat.StemUp;
                if (below) sky.Claim(beat.CenterX - 4, beat.CenterX + 4, beat.MaxY + 6, beat.MaxY + 9 + (beat.Cell.Staccato && beat.Cell.Tenuto ? 9 : 3));
                else sky.Claim(beat.CenterX - 4, beat.CenterX + 4, beat.MinY - 9 - (beat.Cell.Staccato && beat.Cell.Tenuto ? 9 : 3), beat.MinY - 6);
            }
            if (beat.HasStem)
            {
                var top = Math.Min(beat.StemStartY, beat.StemEndY);
                var bottom = Math.Max(beat.StemStartY, beat.StemEndY);
                sky.Claim(beat.StemX - 1, beat.StemX + 1, top, bottom);
                if (beat.Flags > 0 && beat.BeamGroupIndex < 0)
                    sky.Claim(beat.StemX - 0.5, beat.StemX + 8.5, beat.StemUp ? top : bottom - 15 - (beat.Flags - 1) * FlagGap, beat.StemUp ? top + 15 + (beat.Flags - 1) * FlagGap : bottom);
                if (beat.LowerStemTopY is { } lowerTop) sky.Claim(beat.CenterX - 6, beat.CenterX - 4, lowerTop, beat.LowerStemEndY);
            }
            if (beat.GraceNotes.Count > 0)
            {
                var gy = beat.GraceNotes.Min(g => g.Y);
                var gx = beat.CenterX - GraceOffset(beat, GhostRoom(layout.StaffTop, beat));   // where DrawGraceNotes puts the first grace head
                sky.Claim(gx - 9 * Math.Min(3, beat.GraceNotes.Count) - 4, gx + 7, gy - 18, beat.GraceNotes.Max(g => g.Y) + 4);
            }
        }
        foreach (var beam in layout.BeamSegments)
            sky.Claim(Math.Min(beam.X1, beam.X2) - 0.5, Math.Max(beam.X1, beam.X2) + 0.5,
                Math.Min(beam.Y1, beam.Y2) - BeamThickness, Math.Max(beam.Y1, beam.Y2) + BeamThickness);
        foreach (var tie in layout.Ties)
            if (!tie.IsStub) sky.Claim(tie.X1, tie.X2, Math.Min(tie.Y1, tie.Y2) - (tie.Above ? 21 : 0), Math.Max(tie.Y1, tie.Y2) + (tie.Above ? 0 : 21));
            else
            {
                sky.Claim(Math.Min(tie.X1, tie.X2), Math.Max(tie.X1, tie.X2), tie.Y1 - (tie.Above ? 16 : 0), tie.Y1 + (tie.Above ? 0 : 16));
            }
    }

    /// <summary>
    /// A mark of <paramref name="height"/> and <paramref name="x0"/>..<paramref name="x1"/> stacked outside the notation ink:
    /// above the staff (voice 1) or below it (voice 2), starting <paramref name="distance"/> from the staff edge.
    /// Returns the top of the placed box.
    /// </summary>
    internal static double PlaceMark(StaffNotationMeasureLayout layout, double x0, double x1, double height, double distance)
    {
        if (layout.IsSecondVoice) return layout.Skyline.PlaceBelow(x0, x1, height, layout.StaffTop + 4 * StaffGap + distance);
        return layout.Skyline.PlaceAbove(x0, x1, height, layout.StaffTop - distance);
    }

    internal static void AssignNoteheadsAndStems(List<StaffNotationBeat> beats, double staffTop, double staffBottom, bool isDrum)
    {
        var middleY = staffTop + MiddleLineOffset;
        foreach (var beat in beats)
        {
            if (isDrum) continue;   // percussion stems and beams: StaffDrumStems
            if (beat.Notes.Count == 0) continue;

            // Whole notes have no stems, but still need full notehead and pitch geometry.
            var naturalUp = beat.TwoVoices ? !beat.AutoStemInvert : (beat.Notes.Average(n => n.Y) > middleY) ^ beat.AutoStemInvert;
            beat.StemUp = beat.Cell.StemDirection switch
            {
                StemDirection.Up => true,
                StemDirection.Down => false,
                StemDirection.Invert => !naturalUp,
                _ => naturalUp
            };
            ArrangeChordHeads(beat);
            beat.MinY = beat.Notes.Min(n => n.Y);
            beat.MaxY = beat.Notes.Max(n => n.Y);
            if (beat.HasStem) SetStemGeometry(beat, beat.StemUp);
        }

        foreach (var beat in beats.Where(b => b.BeamGroupIndex >= 0))
        {
            var group = beats.Where(b => b.BeamGroupIndex == beat.BeamGroupIndex).ToList();
            if (group.Count == 0) continue;
            var meanY = group.SelectMany(b => b.Notes).Select(n => n.Y).DefaultIfEmpty(middleY).Average();
            var stemOverride = group.Select(b => b.Cell.StemDirection).FirstOrDefault(value => value != StemDirection.Auto);
            var naturalUp = group.All(b => b.IsDrum) || (group[0].TwoVoices ? !group[0].AutoStemInvert : (meanY > middleY) ^ group[0].AutoStemInvert);
            var up = stemOverride switch { StemDirection.Up => true, StemDirection.Down => false, StemDirection.Invert => !naturalUp, _ => naturalUp };
            foreach (var member in group)
            {
                member.StemUp = up;
                if (member.IsDrum)
                {
                    member.MinY = member.MaxY = middleY;
                    member.StemX = member.CenterX + 4;
                    member.StemStartY = middleY;
                    member.StemEndY = middleY - StemLength;
                }
                else
                {
                    ArrangeChordHeads(member);
                    SetStemGeometry(member, up);
                }
            }
        }
    }

    internal static void ArrangeChordHeads(StaffNotationBeat beat)
    {
        var ordered = beat.Notes.OrderBy(n => n.StaffStep).ThenBy(n => n.Source.StringIndex).ToList();
        var adjacentRun = 0;
        var previousStep = int.MinValue;
        foreach (var note in ordered)
        {
            if (previousStep == int.MinValue || note.StaffStep - previousStep > 1) adjacentRun = 0;
            else if (note.StaffStep - previousStep == 1) adjacentRun++;
            note.X = beat.CenterX;
            if (adjacentRun > 0)
                note.X += beat.StemUp
                    ? (adjacentRun % 2 == 1 ? -6.5 : 0)
                    : (adjacentRun % 2 == 1 ? 6.5 : 0);
            previousStep = note.StaffStep;
        }
    }

    internal static void SetStemGeometry(StaffNotationBeat beat, bool up)
    {
        if (beat.Notes.Count == 0) return;
        beat.MinY = beat.Notes.Min(n => n.Y);
        beat.MaxY = beat.Notes.Max(n => n.Y);
        var minX = beat.Notes.Min(n => n.X);
        var maxX = beat.Notes.Max(n => n.X);
        beat.StemUp = up;
        beat.StemX = up ? maxX + HeadRadiusX * 0.9 : minX - HeadRadiusX * 0.9;
        beat.StemStartY = up ? beat.MaxY : beat.MinY;
        // The stem reaches a full length past the note farthest in its direction (a wide chord's stem clears every head).
        beat.StemEndY = up ? beat.MinY - StemLength : beat.MaxY + StemLength;
    }

    internal readonly record struct MetricGroup(double Start, double End, double Width);
    internal readonly record struct PitchSpelling(int Letter, int Octave, int Alteration, int DiatonicIndex);

    internal readonly record struct ClefInfo(int BottomDiatonicIndex, int OctaveShift)
    {
        public static ClefInfo From(string? clef)
        {
            var value = (clef ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            var bottom = 4 * 7 + 2; // default treble: E4 on the bottom line
            if (value.Contains("bass", StringComparison.Ordinal)) bottom = 2 * 7 + 4;
            else if (value.Contains("alto", StringComparison.Ordinal)) bottom = 3 * 7 + 3;
            else if (value.Contains("tenor", StringComparison.Ordinal)) bottom = 3 * 7 + 1;
            else if (value.Length > 0 && value[0] is 'g' or 'f' or 'c')
            {
                var clefLine = value.Length > 1 && value[1] is >= '1' and <= '5'
                    ? value[1] - '0'
                    : 2; // G8 is the standard octave treble clef, not a line-eight clef.
                var reference = value[0] switch
                {
                    'g' => 4 * 7 + 4, // G4
                    'f' => 3 * 7 + 3, // F3
                    _ => 4 * 7         // C4
                };
                bottom = reference - 2 * (clefLine - 1);
            }
            var shift = value.Contains("8vb", StringComparison.Ordinal) ? 12
                : value.Contains("8va", StringComparison.Ordinal) ? -12
                : 0;
            return new ClefInfo(bottom, shift);
        }
    }
}
