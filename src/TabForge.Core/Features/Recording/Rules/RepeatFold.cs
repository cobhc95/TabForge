using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the repeat view's one fold: cutting a clip's performed (playback) span into slices at every repeat pass, each slice with the written
//   (first-performance) seconds the timeline draws it at, its part index and the part count of its repeat, so the clip drawing, the MIDI
//   drawing, the live take layer and the hit test all fold the same way. Drawing only: a clip stays one piece in performed time.
// Does not own: the pass mapping (RepeatPasses), the drawing (TimelineAudioClips, TimelineLiveTakeLayer) or any clip edit.
// Tests: TabForge.Avalonia.Tests.RepeatViewTests.

/// <summary>
/// One drawn slice of a clip: the performed span (<see cref="StartSec"/>..<see cref="EndSec"/>, what plays) drawn at the written span
/// (<see cref="WrittenStartSec"/>..<see cref="WrittenEndSec"/>, the first performance of the same bars). <see cref="PartIndex"/> is the
/// 0-based pass of a repeated range inside <see cref="PartCount"/> parts; a span played once is part 0 of 1 (full lane height).
/// </summary>
public readonly record struct RepeatSlice(double StartSec, double EndSec, double WrittenStartSec, double WrittenEndSec, int PartIndex, int PartCount, int TagPass = 0)
{
    // TagPass: 2 or more on an unfolded slice that is the clip's only play of a repeat (pass k of that repeat): the timeline tags its box "pass k". 0 = no tag.
    /// <summary>True when this slice is one play of a repeated range (drawn as a stacked part).</summary>
    public bool IsPart => PartCount > 1;
}

/// <summary>The repeat view fold: where a clip's performed span lands on the written bars, one slice per pass of each repeat it crosses.</summary>
public static class RepeatFold
{
    /// <summary>
    /// The slices of a clip that plays from <paramref name="clipPerformedStart"/> to <paramref name="clipPerformedEnd"/> (song seconds, the
    /// performed clock). One whole slice when the clip crosses no repeated bar; otherwise ordinary spans plus one slice per play of each
    /// repeated range (part k of N, N the bar's number of plays), in time order. A clip that covers only some plays yields only those slices.
    /// </summary>
    public static IReadOnlyList<RepeatSlice> Slices(double clipPerformedStart, double clipPerformedEnd, PerformedBar[]? performed, RepeatStructure? map = null)
    {
        var length = clipPerformedEnd - clipPerformedStart;
        var whole = new[] { new RepeatSlice(clipPerformedStart, clipPerformedEnd, clipPerformedStart, clipPerformedEnd, 0, 1) };
        if (performed is null || performed.Length == 0 || length <= RepeatPasses.OverlapEpsilonSec) return whole;
        map ??= RepeatStructure.Of(performed);
        if (!RepeatPasses.HasRepeatSpan(performed, clipPerformedStart, length, map)) return whole;
        var slices = new List<RepeatSlice>();
        foreach (var segment in RepeatPasses.Segments(performed, clipPerformedStart, length, endingsOnTheirPass: true, map: map))
        {
            if (segment.LengthSec <= RepeatPasses.OverlapEpsilonSec) continue;
            var from = segment.SongStartSec;
            var to = from + segment.LengthSec;
            var written = (map.WrittenAt(from, end: false), map.WrittenAt(to, end: true));
            var parts = map.Endings.TryGetValue(segment.SourceBar, out var ending) ? ending.PassCount : map.Counts.GetValueOrDefault(segment.SourceBar);
            if (segment.Kind == RepeatSegmentKind.RepeatPart && parts > 1)
                slices.Add(new RepeatSlice(from, to, written.Item1, written.Item2, Math.Clamp(segment.Pass - 1, 0, parts - 1), parts));
            else
                slices.Add(new RepeatSlice(from, to, written.Item1, written.Item2, 0, 1));
        }
        return slices.Count == 0 ? whole : slices;
    }

    /// <summary>Overlaps shorter than this (seconds, one millisecond) do not count as covering a pass.</summary>
    public const double PassOverlapMinSec = 0.001;

    /// <summary>
    /// The one fold decision (drawing, hit test, drag preview and the live box all use it): <see cref="Slices"/>, but a repeat region the clip
    /// covers in only ONE pass is not folded: its stretch becomes an ordinary slice (part 0 of 1, full lane height, tagged with its pass when
    /// that is 2 or more) merged with the ordinary stretches next to it. A region covered in 2+ passes keeps its parts exactly as <see cref="Slices"/>.
    /// </summary>
    public static IReadOnlyList<RepeatSlice> FoldedSlices(double clipPerformedStart, double clipPerformedEnd, PerformedBar[]? performed, RepeatStructure? map = null)
    {
        map ??= RepeatStructure.Of(performed);
        var slices = Slices(clipPerformedStart, clipPerformedEnd, performed, map);
        var anyPart = false;
        foreach (var slice in slices) anyPart |= slice.IsPart;
        if (!anyPart || performed is null) return slices;
        var regionOf = new int[slices.Count];
        var passes = new Dictionary<int, HashSet<int>>();
        for (var i = 0; i < slices.Count; i++)
        {
            regionOf[i] = slices[i].IsPart ? map.RegionIndexOf(map.BarAt((slices[i].StartSec + slices[i].EndSec) / 2)) : -1;
            if (regionOf[i] < 0 || slices[i].EndSec - slices[i].StartSec < PassOverlapMinSec) continue;
            if (!passes.TryGetValue(regionOf[i], out var seen)) passes[regionOf[i]] = seen = new HashSet<int>();
            seen.Add(slices[i].PartIndex);
        }
        var result = new List<RepeatSlice>(slices.Count);
        var changed = false;
        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            if (regionOf[i] >= 0 && (!passes.TryGetValue(regionOf[i], out var covered) || covered.Count < 2))
            {
                slice = slice with { PartIndex = 0, PartCount = 1, TagPass = slice.PartIndex >= 1 && slice.EndSec - slice.StartSec >= PassOverlapMinSec ? slice.PartIndex + 1 : 0 };
                changed = true;
            }
            // Only same-pass stretches merge, so a "pass k" tag (and its click-seek pass) never spreads over an ordinary tail.
            if (!slice.IsPart && result.Count > 0 && !result[^1].IsPart && result[^1].TagPass == slice.TagPass && Math.Abs(result[^1].EndSec - slice.StartSec) < RepeatPasses.OverlapEpsilonSec)
                result[^1] = result[^1] with { EndSec = slice.EndSec, WrittenEndSec = slice.WrittenEndSec };
            else result.Add(slice);
        }
        return changed ? result : slices;
    }
}
