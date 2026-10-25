using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the one derived repeat structure of a performed order (play counts, regions, endings) and the lookups on it, built once and shared by
//   RepeatPasses, RepeatFold, the timeline drawing and the hit test, so none of them recounts the bars.
// Does not own: how regions and endings are derived (RepeatRegions, RepeatEndings) or the performed order (SongExtent.Timing).
// Tests: TabForge.Avalonia.Tests.RepeatEndingFoldTests, RepeatMapTests.

/// <summary>The repeat structure of one performed order: how often each bar plays, the regions, the endings, and bar lookups by time.</summary>
public sealed class RepeatStructure
{
    private readonly PerformedBar[] _performed;
    private readonly Dictionary<int, double> _firstStartMs = new();
    private readonly Dictionary<int, int> _regionOfBar = new();

    private RepeatStructure(PerformedBar[] performed)
    {
        _performed = performed;
        Counts = PlayCounts.Of(performed);
        Regions = RepeatRegions.Of(Counts);
        Endings = RepeatEndings.Of(performed, Counts, Regions);
        foreach (var bar in performed) _firstStartMs.TryAdd(bar.Bar, bar.StartMs);
        for (var r = 0; r < Regions.Count; r++)
            for (var bar = Regions[r].StartBar; bar <= Regions[r].EndBar; bar++) _regionOfBar[bar] = r;
    }

    /// <summary>The map of <paramref name="performed"/> (an empty one for none).</summary>
    public static RepeatStructure Of(PerformedBar[]? performed) => new(performed ?? Array.Empty<PerformedBar>());

    /// <summary>Written bar to its number of performances.</summary>
    public IReadOnlyDictionary<int, int> Counts { get; }

    /// <summary>The repeat regions in bar order.</summary>
    public IReadOnlyList<RepeatRegion> Regions { get; }

    /// <summary>The ending of each volta bar, keyed by written bar.</summary>
    public IReadOnlyDictionary<int, RepeatEnding> Endings { get; }

    /// <summary>The index in <see cref="Regions"/> of the region holding <paramref name="bar"/> (a volta bar belongs to the region it ends), or -1.</summary>
    public int RegionIndexOf(int bar)
    {
        var anchor = Endings.TryGetValue(bar, out var ending) ? ending.RegionStartBar : bar;
        return _regionOfBar.GetValueOrDefault(anchor, -1);
    }

    /// <summary>The index in the performed order of the bar playing at <paramref name="sec"/> (song seconds), or -1 outside every bar.</summary>
    public int IndexAt(double sec)
    {
        var ms = sec * 1000.0;
        int low = 0, high = _performed.Length - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (ms < _performed[mid].StartMs) high = mid - 1;
            else if (ms >= _performed[mid].EndMs) low = mid + 1;
            else return mid;
        }
        return -1;
    }

    /// <summary>The written (first-performance) second of performed second <paramref name="sec"/>; an end maps from just inside the bar that ends there. Times outside the song stay.</summary>
    public double WrittenAt(double sec, bool end)
    {
        var index = IndexAt(end ? sec - RepeatPasses.OverlapEpsilonSec * 10 : sec);
        if (index < 0) return sec;
        var bar = _performed[index];
        return _firstStartMs[bar.Bar] / 1000.0 + (sec - bar.StartMs / 1000.0);
    }

    /// <summary>The written bar playing at <paramref name="sec"/>, or -1 outside every bar.</summary>
    public int BarAt(double sec)
    {
        var index = IndexAt(sec);
        return index < 0 ? -1 : _performed[index].Bar;
    }
}
