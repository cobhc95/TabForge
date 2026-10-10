using TabForge.Models;

namespace TabForge.Services;

// Owns: whole-score bar-range edits across all tracks: copy, remove, insert and move.
// Does not own: undo capture and the arrangement selection.
// Tests: CheckBarRangeEditor, TestDuplicateBarAllTracks.
/// <summary>
/// Whole-score bar-range edits (all tracks at once) used by the arrangement's selected area:
/// copy, remove, insert and move. Each structural edit returns an old-to-new bar map (-1 = removed)
/// for playback remapping. Pure model code: no UI, no undo (the caller wraps it in a transaction).
/// </summary>
public static class BarRangeEditor
{
    public static int MaxMeasures(SongProject project) =>
        project.Tracks.Count == 0 ? 0 : project.Tracks.Max(track => track.Measures.Count);

    public static void Renumber(TrackModel track)
    {
        for (var index = 0; index < track.Measures.Count; index++) track.Measures[index].Number = index + 1;
    }

    /// <summary>Deep copies of bars [start, end] per track (missing bars become empty bars).</summary>
    public static List<List<MeasureModel>> Capture(SongProject project, int start, int end) =>
        project.Tracks.Select(track => Enumerable.Range(start, end - start + 1)
            .Select(bar => bar < track.Measures.Count ? ProjectService.CloneMeasure(track.Measures[bar]) : new MeasureModel())
            .ToList()).ToList();

    /// <summary>Removes bars [start, end] from every track, always keeping at least one bar.</summary>
    public static int[]? Remove(SongProject project, int start, int end)
    {
        var barCount = MaxMeasures(project);
        var count = Math.Min(end - start + 1, barCount - 1);
        if (count <= 0) return null;
        end = start + count - 1;
        foreach (var track in project.Tracks)
        {
            if (start < track.Measures.Count) track.Measures.RemoveRange(start, Math.Min(count, track.Measures.Count - start));
            if (track.Measures.Count == 0) track.Measures.Add(new MeasureModel());
            Renumber(track);
        }
        project.Markers.RemoveAll(m => m.MeasureIndex > start && m.MeasureIndex <= end);
        foreach (var m in project.Markers)
        {
            // A resized section loses the removed bars that fell inside it.
            if (m.LengthBars is int length)
            {
                var overlap = Math.Min(end + 1, m.MeasureIndex + length) - Math.Max(start, m.MeasureIndex);
                if (overlap > 0) m.LengthBars = Math.Max(1, length - overlap);
            }
            if (m.MeasureIndex > end) m.MeasureIndex -= count;
        }
        return Enumerable.Range(0, barCount).Select(b => b < start ? b : b > end ? b - count : -1).ToArray();
    }

    /// <summary>Inserts clones of <paramref name="clip"/> (per track) before bar <paramref name="at"/>.</summary>
    public static int[] Insert(SongProject project, int at, IReadOnlyList<List<MeasureModel>> clip)
    {
        var barCount = MaxMeasures(project);
        var count = clip.Select(t => t.Count).DefaultIfEmpty(0).Max();
        at = Math.Clamp(at, 0, barCount);
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var track = project.Tracks[t];
            while (track.Measures.Count < barCount) track.Measures.Add(new MeasureModel());
            var source = t < clip.Count ? clip[t] : null;
            track.Measures.InsertRange(at, Enumerable.Range(0, count)
                .Select(i => source is not null && i < source.Count ? ProjectService.CloneMeasure(source[i]) : new MeasureModel()));
            Renumber(track);
        }
        foreach (var m in project.Markers)
        {
            // Bars inserted strictly inside a resized section widen it.
            if (m.LengthBars is int length && at > m.MeasureIndex && at < m.MeasureIndex + length) m.LengthBars = length + count;
            if (m.MeasureIndex >= at && at > 0) m.MeasureIndex += count;
        }
        return Enumerable.Range(0, barCount).Select(b => b < at ? b : b + count).ToArray();
    }

    /// <summary>
    /// Duplicate bar: copies bars [start, end] of EVERY track and inserts the copies right after <paramref name="end"/>
    /// (bars are shared master bars, so all tracks stay aligned: insert / delete bar work the same way). A track that has
    /// no bar there gets an empty one. The copy carries everything the bar has (notes, time and key signature, repeats,
    /// tempo, ...) except the section label, so the same label is not shown twice. Sections after the copy move with their bars;
    /// a copy made inside a resized section widens it. Returns the old-to-new bar map, or null when there is nothing to copy
    /// or the song would get too long. The caller wraps the call in one undo step.
    /// </summary>
    public static int[]? Duplicate(SongProject project, int start, int end)
    {
        var barCount = MaxMeasures(project);
        if (project.Tracks.Count == 0 || start < 0 || start >= barCount) return null;
        end = Math.Clamp(end, start, barCount - 1);
        if (barCount + (end - start + 1) > InputLimits.MaxMeasuresPerTrack) return null;
        var clip = Capture(project, start, end);
        foreach (var track in clip)
            foreach (var bar in track) { bar.SectionName = ""; bar.Anacrusis = false; }   // a copy is never the song's pickup bar
        return Insert(project, end + 1, clip);
    }

    /// <summary>
    /// Moves bars [start, end] so they land before <paramref name="insertBefore"/> (an index in the
    /// original numbering). Returns the new first bar and the old-to-new map, or null when the target
    /// is inside the range itself or the range cannot be removed.
    /// </summary>
    public static (int At, int[] Map)? Move(SongProject project, int start, int end, int insertBefore)
    {
        if (insertBefore < 0 || (insertBefore >= start && insertBefore <= end + 1)) return null;
        var count = end - start + 1;
        var clip = Capture(project, start, end);
        var barCount = MaxMeasures(project);
        if (Remove(project, start, end) is null) return null;
        var at = insertBefore > end ? insertBefore - count : insertBefore;
        Insert(project, at, clip);
        var map = Enumerable.Range(0, barCount).Select(b =>
        {
            if (b >= start && b <= end) return at + (b - start);
            var d = b < start ? b : b - count;
            return d >= at ? d + count : d;
        }).ToArray();
        return (at, map);
    }
}
