using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

// Owns: the bar-structure edits of a whole song: delete a bar, repeat a bar range (model steps on a SongProject), and each of those plus insert
//   (BarInsert) and duplicate as one logical edit of an explicit document (one undo step through IEditTarget.Run).
// Does not own: the range work shared with the timeline (BarRangeEditor), the undo capture (the Documents implementation), dialogs, cursor and status text (the hosts).
// Tests: no dedicated test class.
/// <summary>Insert, delete, duplicate and repeat bars on every track; no state, no toolkit.</summary>
public static class BarStructureEdits
{
    public sealed record BarInserted(int At);

    /// <summary>Deletes bar <paramref name="bar"/> from every track (<paramref name="allTracks"/>: the gap closes as Range.Remove closes it, so sections and clips follow)
    /// or from track <paramref name="trackIndex"/> only; the last bar stays. False when nothing was removed.</summary>
    public static bool DeleteBar(SongProject project, int bar, int trackIndex, bool allTracks)
    {
        if (bar < 0) return false;
        if (allTracks) return bar < BarRangeEditor.MaxMeasures(project) && BarRangeGaps.Remove(project, bar, bar, -1) is not null;

        if (trackIndex < 0 || trackIndex >= project.Tracks.Count) return false;
        var selectedTrack = project.Tracks[trackIndex];
        if (selectedTrack.Measures.Count <= 1 || bar >= selectedTrack.Measures.Count) return false;
        selectedTrack.Measures.RemoveAt(bar);
        BarRangeEditor.Renumber(selectedTrack);
        return true;
    }

    /// <summary>Inserts <paramref name="times"/> copies of bars <paramref name="startBar"/>..<paramref name="endBar"/> right after the range on every track (through BarInsert, so
    /// sections, ties and clips follow; a copy is never a section start or the pickup bar). Returns the bars added (0: a bad range or count, or the song would get too long).</summary>
    public static int RepeatRange(SongProject project, int startBar, int endBar, int times)
    {
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (startBar < 0 || startBar >= barCount || endBar < startBar || times < 1) return 0;
        endBar = Math.Min(endBar, barCount - 1);
        var length = endBar - startBar + 1;
        if (barCount + length * times > InputLimits.MaxMeasuresPerTrack) return 0;
        var copies = BarRangeEditor.Capture(project, startBar, endBar)
            .Select(track => Enumerable.Range(0, times).SelectMany(_ => track.Select(ProjectService.CloneMeasure)).ToList()).ToList();
        foreach (var bar in copies.SelectMany(track => track)) { bar.SectionName = ""; bar.Anacrusis = false; }
        return BarInsert.Bars(project, endBar + 1, copies, fillRests: false) is null ? 0 : length * times;
    }

    // ---- document edits: each is one logical edit of an explicit document (one IEditTarget.Run): one undo transaction, one dirty change, one timeline invalidation ----

    /// <summary>One empty bar before bar <paramref name="at"/> on every track as one edit (BarInsert.Empty); with <paramref name="fillRests"/> it starts as one whole-bar rest.</summary>
    public static EditOutcome<BarInserted> InsertBar(IEditTarget document, int at, bool fillRests = false) => InsertBars(document, at, 1, fillRests);

    /// <summary>Inserts <paramref name="count"/> empty bars in front of bar <paramref name="at"/> on every track as one edit (one undo step) through the one shared insert
    /// (BarInsert.Empty: same index on every track, sections, ties and clips follow); the value is the first new bar, null when nothing was inserted.</summary>
    public static EditOutcome<BarInserted> InsertBars(IEditTarget document, int at, int count, bool fillRests = false) =>
        document.Run(project =>
        {
            at = Math.Clamp(at, 0, BarRangeEditor.MaxMeasures(project));
            return BarInsert.Empty(project, at, count, fillRests) is null ? null : new BarInserted(at);
        });

    /// <summary>Delete as one edit; the song keeps the bars its clips need.</summary>
    public static EditOutcome DeleteBar(IEditTarget document, int bar, int trackIndex, bool allTracks) =>
        document.Run(project => { var removed = DeleteBar(project, bar, trackIndex, allTracks); if (removed) SongExtent.EnsureCoversClips(project); return removed; });

    /// <summary>Copies bars <paramref name="first"/>..<paramref name="last"/> to right after themselves in every track; the value is the old-to-new bar mapping (null: the song would get too long).</summary>
    public static EditOutcome<int[]> DuplicateBars(IEditTarget document, int first, int last) =>
        document.Run<int[]>(project => BarRangeEditor.Duplicate(project, first, last));

    /// <summary>Repeat as one edit; <paramref name="times"/> is clamped to 1..16. False when the range or count was refused.</summary>
    public static EditOutcome RepeatRange(IEditTarget document, int first, int last, int times) =>
        document.Run(project => RepeatRange(project, first, last, Math.Clamp(times, 1, 16)) > 0);
}
