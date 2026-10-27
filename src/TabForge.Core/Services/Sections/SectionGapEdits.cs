using TabForge.Models;

namespace TabForge.Services;

// Owns: closing an empty run of bars between two sections (the section strip's "Close gap" and a section dragged left against its neighbour): finding the run, listing what is in it,
//   and the three ways to close it (remove the bars, turn them into a section, remove only the empty bars).
// Does not own: the bar and clip surgery (TimelineRangeEdit / BarRangeGaps), the prompt or the undo step (the caller).
// Tests: TabForge.Avalonia.Tests.SectionGapCloseTests.
/// <summary>How a gap that holds something is closed: remove its bars and what is in them, keep it as a section, or remove only the bars with nothing in them.</summary>
public enum GapCloseMode { RemoveAll, MakeSection, RemoveEmptyOnly }

/// <summary>What lies in a run of bars: notes and written rests per track (name, count) and the clips overlapping it ("track: clip").</summary>
public sealed record GapContents(IReadOnlyList<(string Track, int Notes)> Notes, IReadOnlyList<string> Clips)
{
    public bool IsEmpty => Notes.Count == 0 && Clips.Count == 0;
}

public static class SectionGapEdits
{
    /// <summary>The run of bars (first, last) around <paramref name="bar"/> that no section covers, ending where the next section starts; null when a section covers the bar or no section follows.</summary>
    public static (int First, int Last)? Find(SongProject project, int bar)
    {
        var sorted = SectionLayout.Sorted(project);
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (bar < 0 || bar >= barCount) return null;
        var first = 0;
        for (var i = 0; i < sorted.Count; i++)
        {
            var start = Math.Clamp(sorted[i].MeasureIndex, 0, barCount);
            var end = SectionLayout.End(sorted, i, barCount);
            if (start <= bar && bar < end) return null;
            if (end <= bar) first = Math.Max(first, end);
        }
        var next = sorted.FirstOrDefault(m => m.MeasureIndex > bar);
        return next is null || next.MeasureIndex > barCount ? null : (first, next.MeasureIndex - 1);
    }

    /// <summary>The notes, written rests and clips in bars first..last.</summary>
    public static GapContents Contents(SongProject project, int first, int last)
    {
        var notes = new List<(string, int)>();
        foreach (var track in project.Tracks)
        {
            var count = 0;
            for (var bar = first; bar <= last && bar < track.Measures.Count; bar++) count += NotesIn(track.Measures[bar]);
            if (count > 0) notes.Add((track.Name, count));
        }
        var clips = new List<string>();
        if (BarRangeGaps.Span(project, first, last) is { } span)
            foreach (var track in project.Tracks)
                clips.AddRange(track.AudioClips.Where(c => c.Overlaps(span.From, span.To)).Select(c => $"{track.Name}: {c.Name} (lane {c.Lane + 1})"));
        return new GapContents(notes, clips);
    }

    private static int NotesIn(MeasureModel measure) =>
        measure.Cells.Concat(measure.Voice2Cells).Sum(cell => cell.Notes.Count + (cell.WrittenRest && cell.Notes.Count == 0 ? 1 : 0));

    /// <summary>Closes bars first..last as one model edit (the caller wraps it in one undo step). Later sections, bars, clips and markers move back by the removed length.
    /// Changed is false when nothing happened; Refusal says why (a repeat cut in two).</summary>
    public static TimelineRangeResult Close(SongProject project, int first, int last, GapCloseMode mode)
    {
        if (first < 0 || last < first || last >= BarRangeEditor.MaxMeasures(project)) return new(false);
        if (mode == GapCloseMode.MakeSection)
        {
            if (SectionMenuEdits.StartingAt(project, first) is not null) return new(false);
            project.Markers.Add(new MarkerModel { MeasureIndex = first, Title = $"Bars {first + 1}–{last + 1}", LengthBars = last - first + 1 });
            return new(true);
        }
        if (mode == GapCloseMode.RemoveAll) return TimelineRangeEdit.Apply(project, first, last, TimelineDeleteMode.Remove, -1, false);
        var empty = Enumerable.Range(first, last - first + 1).Where(bar => Contents(project, bar, bar).IsEmpty).ToList();
        var changed = false;
        string? refusal = null;
        while (empty.Count > 0)   // contiguous runs, last one first, so the earlier bar numbers stay valid
        {
            var runEnd = empty[^1];
            var runStart = runEnd;
            while (empty.Contains(runStart - 1)) runStart--;
            empty.RemoveAll(bar => bar >= runStart);
            var one = TimelineRangeEdit.Apply(project, runStart, runEnd, TimelineDeleteMode.Remove, -1, false);
            changed |= one.Changed;
            refusal ??= one.Refusal;
        }
        return new(changed, refusal);
    }
}
