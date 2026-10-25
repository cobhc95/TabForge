using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the repeat regions a song derives from its performed bar order (contiguous written ranges played the same number of times, more than once).
// Does not own: the pass mapping (RepeatPasses), the endings (RepeatEndings), the performed order (SongExtent.Timing) or any drawing.
// Tests: TabForge.Avalonia.Tests.RepeatMapTests.

/// <summary>A repeat region on the written bars, both ends inclusive, played <see cref="PassCount"/> times.</summary>
public readonly record struct RepeatRegion(int StartBar, int EndBar, int PassCount);

/// <summary>
/// The derived repeat regions: maximal contiguous written ranges whose bars are all performed the same number of times, at least twice.
/// A nested repeat is its own region (the inner bars are played more often than the outer ones); the alternate endings are not part of a
/// region (they are played once or on some passes only, see <see cref="RepeatEndings"/>).
/// </summary>
public static class RepeatRegions
{
    /// <summary>One region per maximal contiguous written range with one play count of at least 2, in ascending order.</summary>
    public static IReadOnlyList<RepeatRegion> Of(PerformedBar[]? performed) =>
        performed is null || performed.Length == 0 ? Array.Empty<RepeatRegion>() : Of(PlayCounts.Of(performed));

    /// <summary>The regions of an already counted performed order (see <see cref="PlayCounts"/>).</summary>
    public static IReadOnlyList<RepeatRegion> Of(IReadOnlyDictionary<int, int> counts)
    {
        var repeated = new List<int>();
        foreach (var (bar, count) in counts)
            if (count > 1) repeated.Add(bar);
        repeated.Sort();
        var regions = new List<RepeatRegion>();
        foreach (var bar in repeated)
        {
            if (regions.Count > 0 && bar == regions[^1].EndBar + 1 && counts[bar] == regions[^1].PassCount)
                regions[^1] = regions[^1] with { EndBar = bar };
            else regions.Add(new RepeatRegion(bar, bar, counts[bar]));
        }
        return regions;
    }
}

/// <summary>The one count of how often each written bar is performed.</summary>
public static class PlayCounts
{
    /// <summary>Written bar to its number of performances.</summary>
    public static Dictionary<int, int> Of(PerformedBar[] performed)
    {
        var counts = new Dictionary<int, int>();
        foreach (var bar in performed) counts[bar.Bar] = counts.TryGetValue(bar.Bar, out var seen) ? seen + 1 : 1;
        return counts;
    }
}
