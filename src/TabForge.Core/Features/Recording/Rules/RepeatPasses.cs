using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the score-repeat math of one recording: cutting a recorded [start, length) span (song/performed seconds, the engine's
//   contiguous file) into its source-bar segments — ordinary spans and one segment per play of each repeated range — and the
//   single mapping of a repeat part number k to the performed occurrence k of its bars, which the repeat view fold (RepeatFold) is built on.
// Does not own: the transport-loop lap rule (RecordingPasses), the fold into drawn slices (RepeatFold) or any drawing.
// Tests: TabForge.Avalonia.Tests.RepeatPassesTests, RepeatViewTests.

/// <summary>Which span of a repeat take a segment holds: a played-once span or one play of a repeated bar range.</summary>
public enum RepeatSegmentKind
{
    Ordinary,
    RepeatPart,
}

/// <summary>
/// One file-order piece of a recording that spans score repeats: where it sits on the song timeline
/// (<see cref="SongStartSec"/>, an occurrence time on the performed clock), where it sits in the recorded file
/// (<see cref="FileOffsetSec"/>) and how long it is. A repeated range contributes one segment per play, numbered
/// 1..N in <see cref="Pass"/>; ordinary spans carry <see cref="Pass"/> 0.
/// </summary>
public readonly record struct RepeatSegment(double SongStartSec, double FileOffsetSec, double LengthSec, RepeatSegmentKind Kind, int Pass, int SourceBar, int SourceBars);

/// <summary>
/// A recording through score repeats (a plain repeat, voltas, D.S./D.C., nested repeats): every further play of a bar
/// range is the next part number. Ordinary bars (played once in the whole performed order) merge into ordinary spans;
/// bars played more than once group per play into repeat parts. A recording that starts inside a repeat opens with a
/// partial first part.
/// </summary>
public static class RepeatPasses
{
    /// <summary>Overlaps and gaps at or below this (seconds) are float dust, not segments or breaks. (<see cref="RepeatFold.PassOverlapMinSec"/> is the separate, audible minimum of a covered pass.)</summary>
    public const double OverlapEpsilonSec = 1e-9;

    /// <summary>
    /// The recording's source-bar segments in file order: ordinary spans and one segment per play of each repeated range.
    /// <paramref name="startSec"/> and <paramref name="lengthSec"/> are song seconds on the performed clock (the engine's
    /// contiguous file runs from file 0 at <paramref name="startSec"/>). Parts are stamped at their performed occurrence
    /// times, so parts of different plays share their source bars' x span on the timeline but play at their own time.
    /// A bar played once between two passes (an alternate ending) is an ordinary span for a recording; the view fold sets
    /// <paramref name="endingsOnTheirPass"/> so it becomes a part of the one pass it plays on.
    /// </summary>
    public static IReadOnlyList<RepeatSegment> Segments(PerformedBar[]? performed, double startSec, double lengthSec, bool endingsOnTheirPass = false, RepeatStructure? map = null)
    {
        var endSec = startSec + lengthSec;
        if (performed is null || performed.Length == 0 || lengthSec <= OverlapEpsilonSec)
            return new[] { new RepeatSegment(startSec, 0, Math.Max(0, lengthSec), RepeatSegmentKind.Ordinary, 0, -1, 0) };

        map ??= RepeatStructure.Of(performed);
        var counts = map.Counts;
        var endings = endingsOnTheirPass ? map.Endings : new Dictionary<int, RepeatEnding>();
        var occurrence = new Dictionary<int, int>();
        var passes = new int[performed.Length];
        for (var i = 0; i < performed.Length; i++)
        {
            occurrence.TryGetValue(performed[i].Bar, out var before);
            passes[i] = endings.TryGetValue(performed[i].Bar, out var ending) ? ending.Pass : before + 1;
            occurrence[performed[i].Bar] = before + 1;
        }

        var segments = new List<RepeatSegment>();
        for (var i = 0; i < performed.Length; i++)
        {
            var bar = performed[i];
            var from = Math.Max(startSec, bar.StartMs / 1000.0);
            var to = Math.Min(endSec, bar.EndMs / 1000.0);
            if (to - from <= OverlapEpsilonSec) continue;
            var repeated = counts[bar.Bar] > 1 || endings.ContainsKey(bar.Bar);
            var hasLast = segments.Count > 0;
            var last = hasLast ? segments[^1] : default;
            if (repeated && hasLast && last.Kind == RepeatSegmentKind.RepeatPart && last.Pass == passes[i] && last.SourceBar + last.SourceBars == bar.Bar)
            {
                segments[^1] = last with { LengthSec = last.LengthSec + (to - from), SourceBars = last.SourceBars + 1 };
            }
            else if (!repeated && hasLast && last.Kind == RepeatSegmentKind.Ordinary)
            {
                segments[^1] = last with { LengthSec = last.LengthSec + (to - from), SourceBars = last.SourceBars + 1 };
            }
            else if (repeated)
            {
                segments.Add(new RepeatSegment(from, from - startSec, to - from, RepeatSegmentKind.RepeatPart, passes[i], bar.Bar, 1));
            }
            else
            {
                segments.Add(new RepeatSegment(from, from - startSec, to - from, RepeatSegmentKind.Ordinary, 0, bar.Bar, 1));
            }
        }

        if (segments.Count == 0)
            return new[] { new RepeatSegment(startSec, 0, Math.Max(0, lengthSec), RepeatSegmentKind.Ordinary, 0, -1, 0) };
        // A recording that starts before the first performed bar, or runs past the song end, keeps its file: the silence
        // outside the performed order lands as an ordinary span.
        var first = segments[0];
        if (first.SongStartSec - startSec > OverlapEpsilonSec)
            segments.Insert(0, new RepeatSegment(startSec, 0, first.SongStartSec - startSec, RepeatSegmentKind.Ordinary, 0, -1, 0));
        var tail = segments[^1];
        var coveredEnd = tail.SongStartSec + tail.LengthSec;
        if (tail.Kind == RepeatSegmentKind.Ordinary && endSec - coveredEnd > OverlapEpsilonSec)
            segments[^1] = tail with { LengthSec = tail.LengthSec + (endSec - coveredEnd) };
        else if (endSec - coveredEnd > OverlapEpsilonSec)
            segments.Add(new RepeatSegment(coveredEnd, coveredEnd - startSec, endSec - coveredEnd, RepeatSegmentKind.Ordinary, 0, -1, 0));
        return segments;
    }

    /// <summary>True when the recording overlaps at least one repeated bar (a second play exists inside its span).</summary>
    public static bool HasRepeatSpan(PerformedBar[]? performed, double startSec, double lengthSec, RepeatStructure? map = null)
    {
        if (performed is null || performed.Length == 0 || lengthSec <= OverlapEpsilonSec) return false;
        var endSec = startSec + lengthSec;
        var counts = (map ?? RepeatStructure.Of(performed)).Counts;
        foreach (var bar in performed)
        {
            if (counts[bar.Bar] <= 1) continue;
            var from = Math.Max(startSec, bar.StartMs / 1000.0);
            var to = Math.Min(endSec, bar.EndMs / 1000.0);
            if (to - from > OverlapEpsilonSec) return true;
        }
        return false;
    }

    /// <summary>
    /// The single mapping every repeat take shares: the performed start (song seconds) of the 1-based <paramref name="pass"/>-th
    /// performance of source <paramref name="bar"/>. NaN when that play does not exist. Playback plays part k at this time;
    /// landing stamps part k here (an occurrence time, not the source anchor).
    /// </summary>
    public static double PerformedStartForPass(PerformedBar[]? performed, int bar, int pass)
    {
        if (performed is null || pass < 1) return double.NaN;
        var play = 0;
        foreach (var entry in performed)
        {
            if (entry.Bar != bar) continue;
            if (++play == pass) return entry.StartMs / 1000.0;
        }
        return double.NaN;
    }

    /// <summary>
    /// The performed span (song seconds) of the 1-based <paramref name="pass"/>-th play of <paramref name="barCount"/> bars from
    /// <paramref name="firstBar"/>: the occurrence start and the play's own length (voltas differ per play). NaN/0 when missing.
    /// </summary>
    public static (double StartSec, double LengthSec) PerformedRangeForPass(PerformedBar[]? performed, int firstBar, int barCount, int pass)
    {
        if (performed is null || pass < 1 || barCount < 1) return (double.NaN, 0);
        var play = 0;
        for (var i = 0; i < performed.Length; i++)
        {
            if (performed[i].Bar != firstBar) continue;
            if (++play != pass) continue;
            var end = performed[i].EndMs;
            for (var k = 1; k < barCount && i + k < performed.Length; k++) end = Math.Max(end, performed[i + k].EndMs);
            var start = performed[i].StartMs / 1000.0;
            return (start, Math.Max(0, end / 1000.0 - start));
        }
        return (double.NaN, 0);
    }
}
