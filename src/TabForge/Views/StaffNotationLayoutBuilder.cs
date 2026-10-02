using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationArcs;

namespace TabForge.Views;

/// <summary>Resolves a measure's musical events into beats, beam groups, stems, accidentals and the mark skyline.</summary>
internal static class StaffNotationLayoutBuilder
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
        var beats = new List<StaffNotationBeat>();

        var inferredCursor = 0.0;
        var sourceCells = cellsOverride ?? measure.Cells;
        var voiceIndex = ReferenceEquals(sourceCells, measure.Voice2Cells) ? 1 : 0;
        var twoVoices = measure.Voice2Cells.Any(c => c.Notes.Count > 0);
        for (var i = 0; i < sourceCells.Count; i++)
        {
            var cell = sourceCells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation) continue;

            var rawStart = cell.RhythmicPosition ?? Math.Max(i, inferredCursor);
            var start = Math.Max(0, double.IsFinite(rawStart) ? rawStart : i);
            var duration = MusicTime.CellSlots(cell);
            inferredCursor = Math.Max(inferredCursor, start + duration);
            var centerX = centerOf?.Invoke(start) ?? x + (start + 0.5) * slotWidth;
            var notes = new List<StaffNotationNote>();
            var graceNotes = new List<StaffNotationNote>();
            var hasMainNote = cell.Notes.Any(candidate => !candidate.IsGraceNote);
            if (!isDrum)
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

            beats.Add(new StaffNotationBeat
            {
                CellIndex = i,
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

        beats.Sort((a, b) =>
        {
            var onset = a.StartSlots.CompareTo(b.StartSlots);
            return onset != 0 ? onset : a.CellIndex.CompareTo(b.CellIndex);
        });

        var metricGroups = BuildMetricGroups(numerator, denominator, slots);
        var beamGroups = BuildBeamGroups(beats, metricGroups);
        AssignBeamStemDirections(beamGroups, staffTop + MiddleLineOffset);
        AssignNoteheadsAndStems(beats, staffTop, staffBottom, isDrum);
        var beamSegments = BuildBeamSegments(beamGroups);
        var tuplets = BuildTupletGroups(beats, beamGroups);
        ResolveAccidentals(beats, keyAlterations);
        AssignAccidentalColumns(beats);
        var ties = BuildTies(track, measureIndex, slots, beats, voiceIndex, staffTop);
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
            TupletGroups = tuplets,
            Ties = ties,
            HopoSlurs = slurs,
            Slides = BuildSlideStrokes(track, measureIndex, voiceIndex, beats, staffTop),
            IsSecondVoice = voiceIndex == 1
        };
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
                    sky.Claim(beat.StemX - 0.5, beat.StemX + 8.5, beat.StemUp ? top : bottom - 15 - (beat.Flags - 1) * BeamGap, beat.StemUp ? top + 15 + (beat.Flags - 1) * BeamGap : bottom);
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
                var sign = tie.TowardLeft ? -1.0 : 1.0;
                var xa = tie.X1 + sign * 5; var xb = tie.X1 + sign * 13;
                sky.Claim(Math.Min(xa, xb), Math.Max(xa, xb), tie.Y1 - (tie.Above ? 18 : 0), tie.Y1 + (tie.Above ? 0 : 18));
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
            if (isDrum)
            {
                // Percussion staff: each sound sits at its mapped position (kick low, snare middle,
                // hi-hat/cymbals above); one up-stem joins the beat's notes.
                var ys = beat.Cell.Notes.Select(n => staffTop + (beat.DrumMap?.Invoke(n.MidiValue > 0 ? n.MidiValue : n.Fret).StaffStep ?? 4) * StaffGap / 2).ToList();
                beat.MinY = ys.Count > 0 ? ys.Min() : middleY;
                beat.MaxY = ys.Count > 0 ? ys.Max() : middleY;
                if (!beat.HasStem) continue;
                beat.StemUp = true;
                beat.StemX = beat.CenterX + 5;
                beat.StemStartY = beat.MaxY;
                beat.StemEndY = Math.Min(beat.MinY, middleY) - StemLength;
                var upperYs = ys.Where(y => y <= middleY).ToList();
                var lowerYs = ys.Where(y => y > middleY).ToList();
                if (upperYs.Count > 0 && lowerYs.Count > 0)
                {
                    // Two voices drum notation: cymbals up, drums down, so no stem runs the whole staff.
                    beat.StemStartY = upperYs.Max();
                    beat.StemEndY = upperYs.Min() - StemLength;
                    beat.LowerStemTopY = lowerYs.Min();
                    beat.LowerStemEndY = lowerYs.Max() + StemLength * 0.85;
                }
                continue;
            }
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

    internal static List<StaffNotationBeamGroup> BuildBeamGroups(
        IReadOnlyList<StaffNotationBeat> beats,
        IReadOnlyList<MetricGroup> metricGroups)
    {
        var result = new List<StaffNotationBeamGroup>();
        var pending = new List<StaffNotationBeat>();
        var pendingMetric = -1;

        void Finish()
        {
            if (pending.Count >= 2)
            {
                var metric = metricGroups[pendingMetric];
                var group = new StaffNotationBeamGroup
                {
                    Index = result.Count,
                    Beats = pending.ToArray(),
                    StemUp = false,
                    MetricStart = metric.Start,
                    MetricWidth = metric.Width,
                    MaxFlags = pending.Max(b => b.Flags)
                };
                result.Add(group);
                foreach (var beat in pending) beat.BeamGroupIndex = group.Index;
            }
            pending = new List<StaffNotationBeat>();
            pendingMetric = -1;
        }

        foreach (var beat in beats)
        {
            var metricIndex = FindMetricGroup(metricGroups, beat.StartSlots);
            var forcedAcrossMetric = pending.Count > 0 &&
                (beat.Cell.BeamMode == BeamMode.Force || pending[^1].Cell.BeamMode == BeamMode.Force);
            var atExplicitBreak = beat.Cell.BeamMode == BeamMode.Break;
            var continues = beat.IsBeamable && pending.Count > 0 && !atExplicitBreak &&
                           (metricIndex == pendingMetric || forcedAcrossMetric) &&
                           Math.Abs(pending[^1].StartSlots + pending[^1].DurationSlots - beat.StartSlots) < (beat.Cell.Tuplet.Numerator >= 2 && pending[^1].Cell.Tuplet.Numerator >= 2 ? TupletTickSlack : PositionEpsilon);
            if (!continues) Finish();
            if (beat.IsBeamable && metricIndex >= 0)
            {
                if (pending.Count == 0) pendingMetric = metricIndex;
                pending.Add(beat);
            }
            else Finish();
        }
        Finish();
        return result;
    }

    internal static void AssignBeamStemDirections(IReadOnlyList<StaffNotationBeamGroup> groups, double middleY)
    {
        foreach (var group in groups)
        {
            var notes = group.Beats.SelectMany(b => b.Notes).ToList();
            var stemOverride = group.Beats.Select(b => b.Cell.StemDirection).FirstOrDefault(value => value != StemDirection.Auto);
            var naturalUp = group.Beats.All(b => b.IsDrum) ||
                (group.Beats[0].TwoVoices ? !group.Beats[0].AutoStemInvert : notes.Count > 0 && ((notes.Average(n => n.Y) > middleY) ^ group.Beats[0].AutoStemInvert));
            var up = stemOverride switch { StemDirection.Up => true, StemDirection.Down => false, StemDirection.Invert => !naturalUp, _ => naturalUp };
            group.StemUp = up;
        }
    }

    internal static List<MetricGroup> BuildMetricGroups(int numerator, int denominator, int slots)
    {
        numerator = Math.Clamp(numerator, 1, 32);
        denominator = Math.Clamp(denominator, 1, 64);
        var unit = 16.0 / denominator;
        var pattern = new List<double>();
        if (denominator >= 8 && numerator >= 3 && numerator % 3 == 0)
        {
            pattern.Add(unit * 3); // compound pulse (6/8, 9/8, 6/16 ...)
        }
        else if (denominator == 8)
        {
            var beats = numerator switch
            {
                5 => new[] { 3, 2 },
                7 => new[] { 2, 2, 3 },
                4 => new[] { 2, 2 },
                8 => new[] { 3, 3, 2 },
                10 => new[] { 3, 3, 2, 2 },
                _ => Enumerable.Repeat(1, numerator).ToArray()
            };
            pattern.AddRange(beats.Select(n => n * unit));
        }
        else
        {
            pattern.AddRange(Enumerable.Repeat(unit, numerator));
        }

        var groups = new List<MetricGroup>();
        var at = 0.0;
        var patternIndex = 0;
        while (at < slots - PositionEpsilon && groups.Count < 64)
        {
            var width = pattern[patternIndex % pattern.Count];
            var end = Math.Min(slots, at + width);
            if (end <= at + PositionEpsilon) break;
            groups.Add(new MetricGroup(at, end, end - at));
            at = end;
            patternIndex++;
        }
        if (groups.Count == 0) groups.Add(new MetricGroup(0, Math.Max(1, slots), Math.Max(1, slots)));
        return groups;
    }

    internal static int FindMetricGroup(IReadOnlyList<MetricGroup> groups, double start)
    {
        for (var i = 0; i < groups.Count; i++)
            if (start >= groups[i].Start - PositionEpsilon && start < groups[i].End - PositionEpsilon / 2)
                return i;
        return -1;
    }

    internal static List<StaffNotationBeamSegment> BuildBeamSegments(IReadOnlyList<StaffNotationBeamGroup> groups)
    {
        var segments = new List<StaffNotationBeamSegment>();
        foreach (var group in groups)
        {
            var first = group.Beats[0];
            var last = group.Beats[^1];
            var firstNatural = NaturalStemEnd(first, group.StemUp);
            var lastNatural = NaturalStemEnd(last, group.StemUp);
            var dx = last.StemX - first.StemX;
            var slope = Math.Abs(dx) < 0.01 ? 0 : Math.Clamp((lastNatural - firstNatural) / dx, -0.22, 0.22);
            var intercept = group.Beats.Average(b => NaturalStemEnd(b, group.StemUp) - slope * (b.StemX - first.StemX));

            // Preserve a normal minimum stem length after limiting the slope.
            foreach (var beat in group.Beats)
            {
                var relativeX = beat.StemX - first.StemX;
                if (group.StemUp) intercept = Math.Min(intercept, beat.MinY - 18 - slope * relativeX);
                else intercept = Math.Max(intercept, beat.MaxY + 18 - slope * relativeX);
            }

            group.Slope = slope;
            group.Intercept = intercept;
            foreach (var beat in group.Beats) beat.StemEndY = group.BaseYAt(beat.StemX);

            segments.Add(new StaffNotationBeamSegment(group.Index, 1, first.CellIndex, last.CellIndex,
                first.StemX, group.BaseYAt(first.StemX), last.StemX, group.BaseYAt(last.StemX), false));

            for (var level = 2; level <= group.MaxFlags; level++)
            {
                    // The secondary beam fills the beat. Each additional flag level divides that
                    // beat once more (32nds group their third beam by half-beat, 64ths by eighth).
                    var subdivision = group.MetricWidth / Math.Pow(2, level - 2);
                var eligible = group.Beats.Where(b => b.Flags >= level).ToList();
                var cursor = 0;
                while (cursor < eligible.Count)
                {
                    var run = new List<StaffNotationBeat> { eligible[cursor] };
                    var band = SubdivisionIndex(eligible[cursor].StartSlots, group.MetricStart, subdivision);
                    var next = cursor + 1;
                    while (next < eligible.Count &&
                           SubdivisionIndex(eligible[next].StartSlots, group.MetricStart, subdivision) == band &&
                           !eligible[next].Cell.BreakSecondaryBeamBefore &&
                           Math.Abs(run[^1].StartSlots + run[^1].DurationSlots - eligible[next].StartSlots) < PositionEpsilon)
                    {
                        run.Add(eligible[next]);
                        next++;
                    }

                    var offset = (level - 1) * BeamGap * (group.StemUp ? 1 : -1);
                    if (run.Count >= 2)
                    {
                        var a = run[0];
                        var b = run[^1];
                        segments.Add(new StaffNotationBeamSegment(group.Index, level, a.CellIndex, b.CellIndex,
                            a.StemX, group.BaseYAt(a.StemX) + offset,
                            b.StemX, group.BaseYAt(b.StemX) + offset, false));
                    }
                    else
                    {
                        var only = run[0];
                        var position = group.Beats.ToList().IndexOf(only);
                        var partialRight = position < group.Beats.Count - 1;
                        var neighbor = partialRight ? group.Beats[position + 1] : group.Beats[position - 1];
                        var hook = Math.Clamp(Math.Abs(neighbor.StemX - only.StemX) * 0.42, 3.5, 7.0);
                        var x1 = only.StemX;
                        var x2 = only.StemX + (partialRight ? hook : -hook);
                        segments.Add(new StaffNotationBeamSegment(group.Index, level, only.CellIndex, only.CellIndex,
                            x1, group.BaseYAt(x1) + offset,
                            x2, group.BaseYAt(x2) + offset, true));
                    }
                    cursor = next;
                }
            }
        }
        return segments;
    }

    internal static int SubdivisionIndex(double start, double groupStart, double width)
        => width <= PositionEpsilon ? 0 : (int)Math.Floor((start - groupStart + PositionEpsilon) / width);

    internal static double NaturalStemEnd(StaffNotationBeat beat, bool up)
        => up ? beat.MinY - StemLength : beat.MaxY + StemLength; // measured from the farthest note of a chord

    internal static List<StaffNotationTupletGroup> BuildTupletGroups(
        IReadOnlyList<StaffNotationBeat> beats,
        IReadOnlyList<StaffNotationBeamGroup> beamGroups)
    {
        var result = new List<StaffNotationTupletGroup>();
        var run = new List<StaffNotationBeat>();
        (int Numerator, int Denominator) ratio = (0, 0);

        void Finish()
        {
            if (ratio.Numerator >= 2)
            {
                for (var offset = 0; offset + ratio.Numerator <= run.Count; offset += ratio.Numerator)
                {
                    var members = run.Skip(offset).Take(ratio.Numerator).ToArray();
                    var groupIndex = members[0].BeamGroupIndex;
                    var beamed = groupIndex >= 0 && members.All(b => b.BeamGroupIndex == groupIndex);
                    result.Add(new StaffNotationTupletGroup
                    {
                        Beats = members,
                        Numerator = ratio.Numerator,
                        BeamGroupIndex = beamed ? groupIndex : -1
                    });
                }
            }
            run = new List<StaffNotationBeat>();
            ratio = (0, 0);
        }

        foreach (var beat in beats)
        {
            var current = beat.Cell.Tuplet;
            var valid = current.Numerator >= 2 && (beat.Cell.Notes.Count > 0 || beat.IsRest);
            var contiguous = run.Count == 0 ||
                             (current == ratio && Math.Abs(run[^1].StartSlots + run[^1].DurationSlots - beat.StartSlots) < TupletTickSlack);
            if (!valid || !contiguous) Finish();
            if (valid)
            {
                if (run.Count == 0) ratio = current;
                run.Add(beat);
            }
            else Finish();
        }
        Finish();
        return result;
    }

    internal static void ResolveAccidentals(IReadOnlyList<StaffNotationBeat> beats, IReadOnlyList<int> keyAlterations)
    {
        var state = new Dictionary<(int Letter, int Octave), int>();
        foreach (var beat in beats)
        foreach (var note in beat.Notes.OrderBy(n => n.StaffStep))
        {
            var signatureAlteration = keyAlterations[note.Letter];
            var key = (note.Letter, note.Octave);
            var current = state.TryGetValue(key, out var prior) ? prior : signatureAlteration;
            if (!note.Source.Tied && !beat.Cell.IsTied && note.Alteration != current)
                note.Accidental = note.Alteration switch { > 0 => "♯", < 0 => "♭", _ => "♮" };
            state[key] = note.Alteration;
        }
    }

    internal static void AssignAccidentalColumns(IReadOnlyList<StaffNotationBeat> beats)
    {
        foreach (var beat in beats)
        {
            var placed = new List<(int Step, int Column)>();
            foreach (var note in beat.Notes.Where(n => n.Accidental is not null).OrderBy(n => n.StaffStep))
            {
                var column = 0;
                while (placed.Any(p => p.Column == column && Math.Abs(p.Step - note.StaffStep) <= 2)) column++;
                note.AccidentalColumn = column;
                placed.Add((note.StaffStep, column));
            }
        }
    }

    internal static int[] KeyAlterations(int keySignature)
    {
        var alterations = new int[7];
        var count = Math.Clamp(Math.Abs(keySignature), 0, 7);
        var order = keySignature >= 0 ? SharpOrder : FlatOrder;
        var value = keySignature >= 0 ? 1 : -1;
        for (var i = 0; i < count; i++) alterations[order[i]] = value;
        return alterations;
    }

    internal static PitchSpelling SpellPitch(int midi, IReadOnlyList<int> keyAlterations, int keySignature)
    {
        var midiOctave = midi / 12 - 1;
        PitchSpelling? best = null;
        var bestScore = double.MaxValue;
        for (var octave = midiOctave - 1; octave <= midiOctave + 1; octave++)
        for (var letter = 0; letter < 7; letter++)
        for (var alteration = -1; alteration <= 1; alteration++)
        {
            var naturalMidi = 12 * (octave + 1) + NaturalPitchClasses[letter];
            if (naturalMidi + alteration != midi) continue;
            var expected = keyAlterations[letter];
            var score = alteration == expected ? 0.0 : 10 + Math.Abs(alteration - expected);
            score += Math.Abs(alteration) * 0.1;
            if (keySignature > 0 && alteration < 0 || keySignature < 0 && alteration > 0) score += 0.25;
            if (keySignature == 0 && alteration < 0) score += 0.2; // neutral keys default to sharp spelling
            if (score < bestScore)
            {
                bestScore = score;
                best = new PitchSpelling(letter, octave, alteration, octave * 7 + letter);
            }
        }
        return best ?? new PitchSpelling(0, midiOctave, 0, midiOctave * 7);
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
