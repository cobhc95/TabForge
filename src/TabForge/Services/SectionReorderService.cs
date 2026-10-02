using System.Text.Json;
using TabForge.Models;

namespace TabForge.Services;

// Owns: moving sections to a new order.
// Does not own: the arrangement drawing and undo capture.
// Tests: TestSectionReorder, TestSectionLayout.
/// <summary>Atomically reorders complete section ranges across every track and remaps old bar indexes.</summary>
public static class SectionReorderService
{
    public sealed record SectionRemoval(int[] OldToNewBar, int ContinueAtBar);

    /// <summary>Inserts an independent copy of a section and returns the pre-insert to post-insert bar map.</summary>
    public static int[]? Insert(SongProject project, int at, IReadOnlyList<List<MeasureModel>> section,
        MarkerModel marker)
    {
        if (project.Tracks.Count == 0) return null;
        var barCount = project.Tracks.Max(track => track.Measures.Count);
        var insertCount = section.Select(track => track.Count).DefaultIfEmpty(0).Max();
        if (insertCount <= 0) return null;
        at = Math.Clamp(at, 0, barCount);

        for (var trackIndex = 0; trackIndex < project.Tracks.Count; trackIndex++)
        {
            var track = project.Tracks[trackIndex];
            while (track.Measures.Count < barCount)
                track.Measures.Add(new MeasureModel { Number = track.Measures.Count + 1 });
            var source = trackIndex < section.Count ? section[trackIndex] : null;
            var copies = Enumerable.Range(0, insertCount)
                .Select(index => source is not null && index < source.Count
                    ? CloneMeasure(source[index])
                    : new MeasureModel())
                .ToList();
            track.Measures.InsertRange(at, copies);
            Renumber(track);
        }

        foreach (var existing in project.Markers)
            if (existing.MeasureIndex >= at) existing.MeasureIndex += insertCount;
        project.Markers.Add(new MarkerModel
        {
            MeasureIndex = at,
            Title = marker.Title,
            ColorHex = marker.ColorHex,
            LockPosition = marker.LockPosition
        });
        project.Markers = project.Markers.OrderBy(existing => existing.MeasureIndex).ToList();

        return Enumerable.Range(0, barCount).Select(bar => bar < at ? bar : bar + insertCount).ToArray();
    }

    /// <summary>Removes a complete marker-defined section, retaining at least one score bar.</summary>
    public static SectionRemoval? Delete(SongProject project, MarkerModel marker)
    {
        var barCount = project.Tracks.Select(track => track.Measures.Count).DefaultIfEmpty(0).Max();
        var markers = project.Markers.OrderBy(existing => existing.MeasureIndex).ToList();
        var markerIndex = markers.FindIndex(existing => ReferenceEquals(existing, marker));
        if (markerIndex < 0 || barCount <= 1) return null;

        // Only the visible section goes: gap bars after a resized section stay, and freezing the other
        // lengths keeps the previous section from widening over them.
        var start = Math.Clamp(marker.MeasureIndex, 0, barCount);
        var end = SectionLayout.End(markers, markerIndex, barCount);
        var count = end - start;
        if (count <= 0 || count >= barCount) return null;
        SectionLayout.Freeze(project);

        foreach (var track in project.Tracks)
        {
            while (track.Measures.Count < barCount)
                track.Measures.Add(new MeasureModel { Number = track.Measures.Count + 1 });
            track.Measures.RemoveRange(start, count);
            Renumber(track);
        }

        project.Markers = markers
            .Where(existing => existing.MeasureIndex < start || existing.MeasureIndex >= end)
            .Select(existing =>
            {
                if (existing.MeasureIndex >= end) existing.MeasureIndex -= count;
                return existing;
            })
            .ToList();

        SectionLayout.Normalize(project);
        var mapping = Enumerable.Range(0, barCount)
            .Select(bar => bar < start ? bar : bar >= end ? bar - count : -1)
            .ToArray();
        return new SectionRemoval(mapping, start);
    }

    /// <param name="from">Section ordinal after sorting markers by measure.</param>
    /// <param name="insertBefore">Insertion boundary ordinal in the original sorted list (0..count).</param>
    /// <returns>Mapping from each pre-move bar index to its new bar index, or null for an invalid move.</returns>
    /// <remarks>
    /// The score is a sequence of units: the bars before the first section, then for every section its
    /// visible bars followed by its gap bars (only after a resized section). Only the moved section's
    /// visible bars travel; gaps keep their place in the sequence (they are left alone, not carried).
    /// </remarks>
    public static int[]? Move(SongProject project, int from, int insertBefore)
    {
        var barCount = project.Tracks.Select(track => track.Measures.Count).DefaultIfEmpty(0).Max();
        var markers = project.Markers.OrderBy(marker => marker.MeasureIndex).ToList();
        if (markers.Count == 0 || from < 0 || from >= markers.Count || markers[from].LockPosition ||
            insertBefore < 0 || insertBefore > markers.Count)
            return null;

        var starts = markers.Select(marker => Math.Clamp(marker.MeasureIndex, 0, barCount)).ToArray();
        var ends = Enumerable.Range(0, markers.Count).Select(index => SectionLayout.End(markers, index, barCount)).ToArray();
        if (ends[from] <= starts[from]) return null;

        var sectionOrder = Enumerable.Range(0, markers.Count).ToList();
        sectionOrder.RemoveAt(from);
        var target = insertBefore > from ? insertBefore - 1 : insertBefore;
        target = Math.Clamp(target, 0, sectionOrder.Count);
        if (target == from) return null;

        // Units: (start, length, section ordinal or -1 for prefix/gap).
        var units = new List<(int Start, int Length, int Section)> { (0, starts[0], -1) };
        for (var ordinal = 0; ordinal < markers.Count; ordinal++)
        {
            units.Add((starts[ordinal], ends[ordinal] - starts[ordinal], ordinal));
            var next = ordinal + 1 < markers.Count ? starts[ordinal + 1] : barCount;
            if (next > ends[ordinal]) units.Add((ends[ordinal], next - ends[ordinal], -1));
        }
        var moved = units.Single(unit => unit.Section == from);
        units.Remove(moved);
        var anchor = target < sectionOrder.Count ? units.FindIndex(unit => unit.Section == sectionOrder[target]) : units.Count;
        units.Insert(anchor, moved);

        var oldToNew = new int[barCount];
        var newStarts = new Dictionary<int, int>();
        var cursor = 0;
        foreach (var unit in units)
        {
            if (unit.Section >= 0) newStarts[unit.Section] = cursor;
            for (var local = 0; local < unit.Length; local++) oldToNew[unit.Start + local] = cursor + local;
            cursor += unit.Length;
        }

        // A position lock pins the section's actual bar boundary, not merely its drag handle.
        // Reject another move if it would push a locked section along the timeline.
        for (var ordinal = 0; ordinal < markers.Count; ordinal++)
            if (markers[ordinal].LockPosition && newStarts[ordinal] != starts[ordinal])
                return null;

        SectionLayout.Freeze(project);
        foreach (var track in project.Tracks)
        {
            var original = track.Measures.ToList();
            while (original.Count < barCount)
                original.Add(new MeasureModel { Number = original.Count + 1 });
            var reordered = new List<MeasureModel>(original.Count);
            foreach (var unit in units)
                for (var bar = unit.Start; bar < unit.Start + unit.Length; bar++) reordered.Add(original[bar]);
            track.Measures = reordered;
            for (var bar = 0; bar < track.Measures.Count; bar++) track.Measures[bar].Number = bar + 1;
        }

        project.Markers.Clear();
        foreach (var ordinal in units.Where(unit => unit.Section >= 0).Select(unit => unit.Section))
        {
            markers[ordinal].MeasureIndex = newStarts[ordinal];
            project.Markers.Add(markers[ordinal]);
        }
        SectionLayout.Normalize(project);
        return oldToNew;
    }
    /// <summary>Composes a timeline-to-document bar remap with a subsequent document reorder.</summary>
    public static int[] ComposeBarRemap(IReadOnlyList<int> prior, IReadOnlyList<int> move)
    {
        var composed = new int[prior.Count];
        for (var index = 0; index < prior.Count; index++)
        {
            var current = prior[index];
            composed[index] = current >= 0 && current < move.Count ? move[current] : current;
        }
        return composed;
    }

    /// <summary>Composes a structural bar mapping and assigns stable timeline ids to inserted bars.</summary>
    public static int[] ComposeBarRemapWithInsertions(IReadOnlyList<int> prior, IReadOnlyList<int> move,
        int currentBarCount)
    {
        var composed = ComposeBarRemap(prior, move).ToList();
        var occupied = composed.Where(bar => bar >= 0).ToHashSet();
        for (var bar = 0; bar < currentBarCount; bar++)
            if (occupied.Add(bar)) composed.Add(bar);
        return composed.ToArray();
    }

    private static MeasureModel CloneMeasure(MeasureModel measure) => ProjectService.CloneMeasure(measure);

    private static void Renumber(TrackModel track) => BarRangeEditor.Renumber(track);

    /// <summary>
    /// Converts a saved baseline-to-document mapping to a newly compiled baseline. The current map
    /// describes old-baseline → new-baseline; its inverse composes each saved old-baseline → target map.
    /// </summary>
    public static int[] RebaseBarRemap(IReadOnlyList<int> oldToNewBaseline,
        IReadOnlyList<int> oldToTarget, int newBaselineBarCount)
    {
        var inverse = Enumerable.Repeat(-1, Math.Max(0, newBaselineBarCount)).ToArray();
        for (var source = 0; source < oldToNewBaseline.Count; source++)
        {
            var destination = oldToNewBaseline[source];
            if (destination >= 0 && destination < inverse.Length) inverse[destination] = source;
        }
        var rebased = new int[inverse.Length];
        for (var destination = 0; destination < inverse.Length; destination++)
        {
            var oldSource = inverse[destination];
            rebased[destination] = oldSource >= 0 && oldSource < oldToTarget.Count
                ? oldToTarget[oldSource]
                : destination;
        }
        return rebased;
    }
}
