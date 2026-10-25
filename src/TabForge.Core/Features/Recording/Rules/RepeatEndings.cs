using System.Numerics;
using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: which bars are an alternate ending (volta) of a repeat region and the pass each one plays on. The performed order says where each bar is played
//   and PlaybackOrder's ending mask (carried on PerformedBar) says which bars are voltas, own or inherited from a longer bracket.
// Does not own: the performed order and the masks (PlaybackOrder), the regions (RepeatRegions) or the fold into slices (RepeatFold).
// Tests: TabForge.Avalonia.Tests.RepeatEndingFoldTests.

/// <summary>An ending bar: it plays on <see cref="Pass"/> (1-based) of the region starting at <see cref="RegionStartBar"/> and is skipped on the other passes.</summary>
public readonly record struct RepeatEnding(int Pass, int PassCount, int RegionStartBar);

/// <summary>
/// The ending bars of every repeat region: a bar played once that carries an alternate ending for exactly one pass and follows a repeat region
/// (directly, or after the bars of the same ending) is that pass's ending, however many bars the ending spans. Jumps (D.S., D.C., coda) never make an ending.
/// </summary>
public static class RepeatEndings
{
    /// <summary>The ending of each volta bar, keyed by written bar. Empty when the song has none.</summary>
    public static IReadOnlyDictionary<int, RepeatEnding> Of(PerformedBar[]? performed)
    {
        if (performed is null || performed.Length == 0) return new Dictionary<int, RepeatEnding>();
        var counts = PlayCounts.Of(performed);
        return Of(performed, counts, RepeatRegions.Of(counts));
    }

    /// <summary>The endings of an already counted performed order and its regions.</summary>
    public static IReadOnlyDictionary<int, RepeatEnding> Of(PerformedBar[] performed, IReadOnlyDictionary<int, int> counts, IReadOnlyList<RepeatRegion> regions)
    {
        var endings = new Dictionary<int, RepeatEnding>();
        bool IsVolta(PerformedBar bar) => BitOperations.PopCount((uint)bar.EndingMask) == 1 && counts[bar.Bar] == 1;
        for (var i = 1; i < performed.Length; i++)
        {
            if (!IsVolta(performed[i])) continue;
            var before = i - 1;
            while (before > 0 && IsVolta(performed[before])) before--;
            var region = regions.FirstOrDefault(r => performed[before].Bar >= r.StartBar && performed[before].Bar <= r.EndBar);
            if (region.PassCount == 0) continue;
            endings[performed[i].Bar] = new RepeatEnding(BitOperations.TrailingZeroCount((uint)performed[i].EndingMask) + 1, region.PassCount, region.StartBar);
        }
        return endings;
    }
}
