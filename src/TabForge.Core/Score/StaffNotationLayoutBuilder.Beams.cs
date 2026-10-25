using System.Globalization;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationArcs;

namespace TabForge.Views;

// Owns: the beam groups, metric groups, beam segments and tuplet groups of the staff layout (partial of StaffNotationLayoutBuilder).
// Tests: no dedicated test class.

internal static partial class StaffNotationLayoutBuilder
{
    internal static List<StaffNotationBeamGroup> BuildBeamGroups(
        IReadOnlyList<StaffNotationBeat> beats,
        IReadOnlyList<MetricGroup> metricGroups, int firstIndex = 0)
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
                    Index = firstIndex + result.Count,
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
            // A touched onset (within PositionEpsilon; imported tuplets keep their own tick slack) continues the group.
            var tolerance = pending.Count > 0 && beat.Cell.Tuplet.Numerator >= 2 && pending[^1].Cell.Tuplet.Numerator >= 2
                ? TupletTickSlack
                : PositionEpsilon;
            var continues = beat.IsBeamable && pending.Count > 0 && !atExplicitBreak &&
                           (metricIndex == pendingMetric || forcedAcrossMetric) &&
                           Math.Abs(pending[^1].StartSlots + pending[^1].DurationSlots - beat.StartSlots) <= tolerance;
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

        // The pattern repeats until it covers `slots`; the cap bounds a corrupt bar (32 nominal beats plus 64 overflow beats).
        var groups = new List<MetricGroup>();
        var at = 0.0;
        var patternIndex = 0;
        while (at < slots - PositionEpsilon && groups.Count < 96)
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
            FitBeamLine(group);

            // A segment runs along the centre of its beam; the stem ends on the outer edge of the first beam.
            var inward = group.StemUp ? 1 : -1;
            var half = BeamThickness / 2 * inward;
            segments.Add(new StaffNotationBeamSegment(group.Index, 1, first.CellIndex, last.CellIndex,
                first.StemX, group.BaseYAt(first.StemX) + half, last.StemX, group.BaseYAt(last.StemX) + half, false));

            AddSecondaryBeams(group, segments, inward, half);
        }
        return segments;
    }

    /// <summary>Fits the beam line of a group: the slope and intercept follow the natural stem ends, and every stem keeps a normal minimum length after the slope is limited.</summary>
    private static void FitBeamLine(StaffNotationBeamGroup group)
    {
        var first = group.Beats[0];
        var last = group.Beats[^1];
        var stemLength = BeamedStemLength(group.MaxFlags);
        var minStem = MinBeamedStemLength(group.MaxFlags);
        var firstNatural = NaturalStemEnd(first, group.StemUp, stemLength);
        var lastNatural = NaturalStemEnd(last, group.StemUp, stemLength);
        var dx = last.StemX - first.StemX;
        var slope = Math.Abs(dx) < 0.01 ? 0 : Math.Clamp((lastNatural - firstNatural) / dx, -0.22, 0.22);
        var intercept = group.Beats.Average(b => NaturalStemEnd(b, group.StemUp, stemLength) - slope * (b.StemX - first.StemX));

        // Preserve a normal minimum stem length after limiting the slope.
        foreach (var beat in group.Beats)
        {
            var relativeX = beat.StemX - first.StemX;
            if (group.StemUp) intercept = Math.Min(intercept, beat.MinY - minStem - slope * relativeX);
            else intercept = Math.Max(intercept, beat.MaxY + minStem - slope * relativeX);
        }

        group.Slope = slope;
        group.Intercept = intercept;
        foreach (var beat in group.Beats) beat.StemEndY = group.BaseYAt(beat.StemX);
    }

    /// <summary>The secondary beams (levels 2 and up): every level runs unbroken across the beat; a run ends where a note has fewer flags, at an explicit break or at a gap in time. A lone note gets a hook.</summary>
    private static void AddSecondaryBeams(StaffNotationBeamGroup group, List<StaffNotationBeamSegment> segments, int inward, double half)
    {
        for (var level = 2; level <= group.MaxFlags; level++)
        {
            // Every beam level runs unbroken across the beat; a run ends only where a note has fewer flags,
            // at an explicit break or at a gap in time.
            var subdivision = group.MetricWidth;
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
                       Math.Abs(run[^1].StartSlots + run[^1].DurationSlots - eligible[next].StartSlots) <= PositionEpsilon)
                {
                    run.Add(eligible[next]);
                    next++;
                }

                var offset = (level - 1) * BeamGap * inward + half;
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

    internal static int SubdivisionIndex(double start, double groupStart, double width)
        => width <= PositionEpsilon ? 0 : (int)Math.Floor((start - groupStart + PositionEpsilon) / width);

    internal static double NaturalStemEnd(StaffNotationBeat beat, bool up, double stemLength)
        => up ? beat.MinY - stemLength : beat.MaxY + stemLength; // measured from the farthest note of a chord

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
                // Full groups of the tuplet's count; each beat of the remainder (an unfinished tuplet) gets its own number, as in the reference (session 04 step 23).
                var full = run.Count - run.Count % ratio.Numerator;
                for (var offset = 0; offset < run.Count; offset += offset < full ? ratio.Numerator : 1)
                {
                    var members = run.Skip(offset).Take(offset < full ? ratio.Numerator : 1).ToArray();
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
}
