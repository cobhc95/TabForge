using System.IO;
using System.Runtime.CompilerServices;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>How the arrangement timeline places a Bars clip.</summary>
public enum TimelinePasteKind
{
    /// <summary>Replace the bars from the target bar on (this track only), appending bars at the end when needed.</summary>
    OverwriteThisTrack,
    /// <summary>Replace the bars from the target bar on, every track the clip maps onto.</summary>
    OverwriteAllTracks,
    /// <summary>Insert the bars before the target bar on all tracks (tracks the clip does not map onto get empty bars).</summary>
    InsertBars
}

/// <param name="Changed">False when nothing was pasted (the project is untouched).</param>
/// <param name="OldToNewBar">Structural inserts only: old-to-new bar map for playback, selection and section remapping.</param>
public sealed record TimelinePasteResult(bool Changed, int BarsPasted, int[]? OldToNewBar, string Message);

// Owns: the arrangement timeline's use of the one score clipboard (bar, area and section copies).
// Does not own: the clipboard format and capture (ClipboardService).
// Tests: TestTimelineClipsShareClipboard, TestTimelineSectionCopiesAsBars.
/// <summary>
/// The arrangement timeline's side of the one score clipboard (<see cref="ClipboardService"/>, design chunk C7): bar, area and
/// section copies are Bars <see cref="ScoreClip"/>s, so a copy in the timeline pastes in the score editor and the reverse.
/// Copy is final. <see cref="PasteBars"/> is the ADAPTER for the editor's paste command path: C4's
/// <c>EditCommands.PasteClip</c>/<c>PasteBars</c> replaces its body (see the TODO on it); the callers do not change.
/// </summary>
public static class TimelineClips
{
    private static readonly ConditionalWeakTable<SongProject, object> SongIds = new();

    /// <summary>Runtime-only id of a loaded song (clip.SourceSongId): tracks map index to index only within the same song.</summary>
    public static string SongId(SongProject project) => (string)SongIds.GetValue(project, _ => Guid.NewGuid().ToString("N"));

    /// <summary>Keeps the song's clipboard identity when undo / redo rebuilds the song object, so a clip copied before the undo still maps track to track.</summary>
    public static void CarrySongId(SongProject from, SongProject to)
    {
        if (!ReferenceEquals(from, to)) SongIds.AddOrUpdate(to, SongId(from));
    }

    // ---- Copy ----

    /// <summary>One bar of the given track, or of every track.</summary>
    public static ScoreClip CopyBar(SongProject project, int bar, int trackIndex, bool allTracks) =>
        Capture(project, bar, bar, allTracks ? null : trackIndex);

    /// <summary>Bars <paramref name="start"/>..<paramref name="end"/> of every track (the selected area), or of track <paramref name="onlyTrack"/> alone.</summary>
    public static ScoreClip CopyArea(SongProject project, int start, int end, int onlyTrack = -1) => Capture(project, start, end, onlyTrack < 0 ? null : onlyTrack);

    /// <summary>The bars of a section (<paramref name="end"/> exclusive, as <see cref="SectionLayout.TryGetBounds"/> returns it); the section's title and colour stay with the caller.</summary>
    public static ScoreClip CopySection(SongProject project, int start, int end) => Capture(project, start, end - 1, null);

    /// <summary>Throws <see cref="InvalidDataException"/> (user-facing) when there is nothing to copy.</summary>
    private static ScoreClip Capture(SongProject project, int first, int last, int? onlyTrack)
    {
        if (last < first) (first, last) = (last, first);
        var tracks = new List<int>();
        for (var t = 0; t < project.Tracks.Count; t++)
            if ((onlyTrack is null || onlyTrack == t) && first >= 0 && project.Tracks[t].Measures.Count > last) tracks.Add(t);
        if (tracks.Count == 0) throw new InvalidDataException("Nothing to copy.");
        return ClipboardService.CaptureBars(project, tracks, first, last, SongId(project));
    }

    // ---- What the timeline can paste ----

    /// <summary>The timeline pastes whole bars only (a Beats clip belongs to the score cursor).</summary>
    public static bool CanPasteOnTimeline(ScoreClip? clip) => clip is { Kind: ScoreClipKind.Bars } && clip.BarCount > 0;

    /// <summary>
    /// The clip as the text the score editor's current paste reads (a one-track project snapshot of the first clip track).
    /// Bridge until the editor's Ctrl+V reads <see cref="ClipboardService"/> directly (chunk C5).
    /// </summary>
    public static string ToEditorText(ScoreClip clip)
    {
        var source = clip.Tracks[0];
        return ProjectService.Snapshot(new SongProject
        {
            FormatVersion = 2,
            Tracks = new List<TrackModel> { new() { Measures = source.Bars.Select(ProjectService.CloneMeasure).ToList() } }
        });
    }

    // ---- Paste ----

    /// <summary>
    /// Project track index to clip track index (-1 = none): i to i when the clip came from this song, otherwise clip track k
    /// lands on track <paramref name="anchorTrack"/> + k (design 3.5).
    /// </summary>
    public static int[] MapTracks(ScoreClip clip, SongProject project, int anchorTrack)
    {
        var map = Enumerable.Repeat(-1, project.Tracks.Count).ToArray();
        if (clip.SourceSongId.Length > 0 && clip.SourceSongId == SongId(project) && clip.SourceTrackCount == project.Tracks.Count)
        {
            for (var k = 0; k < clip.Tracks.Count; k++)
            {
                var t = clip.Tracks[k].SourceTrackIndex;
                if (t >= 0 && t < map.Length && map[t] < 0) map[t] = k;
            }
            if (map.Any(k => k >= 0)) return map;
        }
        anchorTrack = Math.Clamp(anchorTrack, 0, Math.Max(0, project.Tracks.Count - 1));
        for (var k = 0; k < clip.Tracks.Count && anchorTrack + k < map.Length; k++) map[anchorTrack + k] = k;
        return map;
    }

    /// <summary>
    /// Per project track, clones of the clip's bars re-fretted for that track (<see cref="NoteMapper"/>; unmapped or refused tracks
    /// get an empty list). The shape <see cref="BarRangeEditor.Insert"/> and the section insert take. Also the section-paste source.
    /// </summary>
    public static List<List<MeasureModel>> BarsPerTrack(ScoreClip clip, SongProject project, int anchorTrack, out string note)
    {
        var map = MapTracks(clip, project, anchorTrack);
        return PerTrack(clip, project, map, out note);
    }

    private static List<List<MeasureModel>> PerTrack(ScoreClip clip, SongProject project, int[] map, out string note)
    {
        var notes = new List<string>();
        var result = new List<List<MeasureModel>>(project.Tracks.Count);
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var bars = map[t] >= 0 ? MapBars(clip.Tracks[map[t]], project.Tracks[t], notes) : null;
            result.Add(bars ?? new List<MeasureModel>());
        }
        note = string.Join("; ", notes.Distinct());
        return result;
    }

    /// <summary>Clones of the clip track's bars for <paramref name="target"/>, or null when the instruments cannot be mapped (pitched and drums, refused).</summary>
    private static List<MeasureModel>? MapBars(ScoreClipTrack source, TrackModel target, List<string> notes)
    {
        var from = InstrumentLayout.Of(source.ToTrackModel());
        var to = InstrumentLayout.Of(target);
        var bars = source.Bars.Select(ProjectService.CloneMeasure).ToList();
        if (from.SameLayout(to)) return bars;
        foreach (var bar in bars)
        {
            foreach (var voice in new[] { 0, 1 })
            {
                var cells = voice == 0 ? bar.Cells : bar.Voice2Cells;
                if (cells.Count == 0) continue;
                var mapped = NoteMapper.Map(from, to, cells);
                if (mapped.Report.Kind == NoteMapKind.Refused || mapped.Beats.Count != cells.Count)
                {
                    notes.Add($"{source.Name}: not pasted into {target.Name}");
                    return null;
                }
                var summary = mapped.Report.Summary();
                if (summary.Length > 0) notes.Add(summary);
                cells.Clear();
                cells.AddRange(mapped.Beats);
            }
        }
        return bars;
    }

    /// <summary>
    /// Places the Bars clip on the project through <see cref="EditCommands"/> (NoteMapper + BarGrid, the paste questions through
    /// <paramref name="asker"/> and the remembered answers in <paramref name="settings"/>; null = recommended answers, nothing stored).
    /// Model only: the caller wraps it in one undo transaction and refreshes the UI; on <c>Changed == false</c> the project is untouched.
    /// <see cref="TimelinePasteResult.OldToNewBar"/> is set whenever bars were inserted (also when Q3 was answered "Insert").
    /// <see cref="TimelinePasteResult.Message"/> is the full status text.
    /// </summary>
    public static TimelinePasteResult PasteBars(SongProject project, ScoreClip clip, int atBar, int anchorTrack, TimelinePasteKind kind,
        EditingSettings? settings = null, IPasteQuestionAsker? asker = null)
    {
        if (!CanPasteOnTimeline(clip)) return new(false, 0, null, "The timeline pastes whole bars only; paste beats in the score.");
        if (project.Tracks.Count == 0 || atBar < 0) return new(false, 0, null, "Nothing to paste into.");
        var count = clip.BarCount;

        var map = MapTracks(clip, project, anchorTrack);
        if (kind == TimelinePasteKind.OverwriteThisTrack)
        {
            // One track: the clip track that came from this track in this song, else the first.
            if (anchorTrack < 0 || anchorTrack >= project.Tracks.Count) return new(false, 0, null, "Select a track to paste into.");
            var own = map[anchorTrack] >= 0 ? map[anchorTrack] : 0;
            map = Enumerable.Repeat(-1, project.Tracks.Count).ToArray();
            map[anchorTrack] = own;
        }

        // The editor's paste path (C4): overwrite kinds ask Q3 when the target bars have notes; the area paste is an insert.
        var outcome = EditCommands.PasteBars(project, clip, atBar, map,
            kind == TimelinePasteKind.InsertBars ? BarsOntoNotesAnswer.InsertBefore : null,
            settings ?? new EditingSettings(), asker ?? new RecommendedPasteAnswers());
        if (!outcome.Changed) return new(false, 0, null, outcome.Status);
        return new(true, count, outcome.BarMap, outcome.Status);
    }
}
