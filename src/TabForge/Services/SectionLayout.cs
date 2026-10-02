using TabForge.Models;

namespace TabForge.Services;

// Owns: the one rule for where a section starts and ends.
// Does not own: the timeline drawing and section edits.
// Tests: TestSectionLayout.
/// <summary>
/// The one rule for where a section starts and ends. A section runs from its marker to the next marker,
/// unless it was resized (<see cref="MarkerModel.LengthBars"/>) to stop earlier; the bars after it up to
/// the next marker are then unsectioned "gap" bars. Timeline drawing, copy/delete/move/loop, the playing
/// section and edge resizing all use this, so what the coloured block shows is what every action covers.
/// </summary>
public static class SectionLayout
{
    public static List<MarkerModel> Sorted(SongProject project) =>
        project.Markers.OrderBy(marker => marker.MeasureIndex).ToList();

    /// <summary>Exclusive end bar of <paramref name="sorted"/>[<paramref name="index"/>].</summary>
    public static int End(IReadOnlyList<MarkerModel> sorted, int index, int barCount)
    {
        var start = Math.Clamp(sorted[index].MeasureIndex, 0, barCount);
        var next = index + 1 < sorted.Count ? Math.Clamp(sorted[index + 1].MeasureIndex, start, barCount) : barCount;
        return sorted[index].LengthBars is int length ? Math.Clamp(start + length, start, next) : next;
    }

    public static bool TryGetBounds(SongProject project, MarkerModel marker, out int start, out int end)
    {
        var sorted = Sorted(project);
        var index = sorted.FindIndex(existing => ReferenceEquals(existing, marker));
        var barCount = BarRangeEditor.MaxMeasures(project);
        start = end = 0;
        if (index < 0 || barCount <= 0) return false;
        start = Math.Clamp(marker.MeasureIndex, 0, barCount);
        end = End(sorted, index, barCount);
        return end > start;
    }

    /// <summary>The section containing <paramref name="bar"/>, or null for bars before the first section or in a gap.</summary>
    public static MarkerModel? At(SongProject project, int bar)
    {
        var sorted = Sorted(project);
        var barCount = BarRangeEditor.MaxMeasures(project);
        var index = sorted.FindLastIndex(marker => marker.MeasureIndex <= bar);
        return index >= 0 && bar < End(sorted, index, barCount) ? sorted[index] : null;
    }

    /// <summary>
    /// Makes every section's length explicit, so structural edits (delete/insert/move) cannot silently
    /// widen a neighbour over gap bars. Pair with <see cref="Normalize"/> afterwards.
    /// </summary>
    public static void Freeze(SongProject project)
    {
        var sorted = Sorted(project);
        var barCount = BarRangeEditor.MaxMeasures(project);
        for (var i = 0; i < sorted.Count; i++)
            sorted[i].LengthBars = Math.Max(1, End(sorted, i, barCount) - Math.Clamp(sorted[i].MeasureIndex, 0, barCount));
    }

    /// <summary>
    /// Bars where a section may start when moved as a block (plain drag): it keeps its length and may only
    /// slide through free (unsectioned) bars, never over its neighbours. Null when it cannot move at all.
    /// </summary>
    public static (int Min, int Max)? MoveRange(SongProject project, MarkerModel marker)
    {
        var barCount = BarRangeEditor.MaxMeasures(project);
        var sorted = Sorted(project);
        var index = sorted.IndexOf(marker);
        if (index < 0 || barCount <= 0 || marker.LockPosition) return null;
        var start = Math.Clamp(marker.MeasureIndex, 0, barCount);
        var length = Math.Max(1, End(sorted, index, barCount) - start);
        var min = index > 0 ? End(sorted, index - 1, barCount) : 0;
        var max = (index + 1 < sorted.Count ? sorted[index + 1].MeasureIndex : barCount) - length;
        return max >= min && (min < start || max > start) ? (min, max) : null;
    }

    /// <summary>
    /// Moves a section as a block (plain drag): same length, only into free bars, bars themselves untouched.
    /// The space it leaves becomes a gap; neighbours keep their own length (they do not grow into it).
    /// </summary>
    public static bool MoveMarker(SongProject project, MarkerModel marker, int bar)
    {
        if (MoveRange(project, marker) is not var (min, max)) return false;
        var target = Math.Clamp(bar, min, max);
        if (target == marker.MeasureIndex) return false;
        Freeze(project); // explicit lengths: nobody widens into the vacated bars
        marker.MeasureIndex = target;
        Normalize(project);
        return true;
    }

    /// <summary>Drops lengths that just reach the next section (or the end), keeping saved files minimal.</summary>
    public static void Normalize(SongProject project)
    {
        var sorted = Sorted(project);
        var barCount = BarRangeEditor.MaxMeasures(project);
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].LengthBars is not int length) continue;
            var next = i + 1 < sorted.Count ? sorted[i + 1].MeasureIndex : barCount;
            if (sorted[i].MeasureIndex + length >= next) sorted[i].LengthBars = null;
        }
    }

    /// <summary>
    /// Moves one edge of a section to a bar boundary. Right edge: growing fills gap bars, then pushes the
    /// following sections along (keeping their lengths); shrinking leaves gap bars. Left edge: growing trims
    /// the previous section (never below one bar); shrinking leaves gap bars; the right edge stays put.
    /// </summary>
    public static void ResizeEdge(SongProject project, MarkerModel marker, bool rightEdge, int boundary)
    {
        var barCount = BarRangeEditor.MaxMeasures(project);
        var sorted = Sorted(project);
        var index = sorted.IndexOf(marker);
        if (index < 0 || barCount <= 0) return;
        if (rightEdge)
        {
            var end = Math.Clamp(boundary, marker.MeasureIndex + 1, barCount);
            for (var i = index + 1; i < sorted.Count; i++)
            {
                var previousEnd = i == index + 1 ? end : End(sorted, i - 1, barCount);
                if (sorted[i].MeasureIndex >= previousEnd) break;
                // Pushed along whole: its length (explicit or implied) is kept.
                var length = Math.Max(1, End(sorted, i, barCount) - sorted[i].MeasureIndex);
                if (sorted[i].LengthBars is null && i + 1 < sorted.Count) sorted[i].LengthBars = length;
                sorted[i].MeasureIndex = Math.Min(barCount - 1, previousEnd);
            }
            var nextStart = index + 1 < sorted.Count ? sorted[index + 1].MeasureIndex : barCount;
            marker.LengthBars = end >= nextStart ? null : end - marker.MeasureIndex;
        }
        else
        {
            var myEnd = End(sorted, index, barCount);
            var start = Math.Clamp(boundary, 0, myEnd - 1);
            if (index > 0)
            {
                var previous = sorted[index - 1];
                var previousEnd = End(sorted, index - 1, barCount);
                var previousStop = Math.Max(previous.MeasureIndex + 1, Math.Min(previousEnd, start));
                previous.LengthBars = previousStop - previous.MeasureIndex;
                start = Math.Max(start, previousStop);
            }
            if (marker.LengthBars is int ownLength) marker.LengthBars = Math.Max(1, marker.MeasureIndex + ownLength - start);
            marker.MeasureIndex = start;
        }
        Normalize(project);
    }
}
