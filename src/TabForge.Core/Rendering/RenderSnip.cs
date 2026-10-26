using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Rendering;

// Owns: "Selected bars" in the Render dialog: a copy of the song holding only the white selection (selected cells on the selected tracks, selected clips),
//   everything else silent at its normal timing, and the range that covers the selection.
// Does not own: the render run (RenderJob) or the selection state (SelectionModel).
// Tests: RenderSnipTests.

/// <summary>The song copy to render and which copied track stands for each original.</summary>
public sealed record RenderSnipResult(SongProject Project, IReadOnlyDictionary<TrackModel, TrackModel> Map);

/// <summary>Renders the white selection alone: the song is copied, never changed.</summary>
public static class RenderSnip
{
    /// <summary>True when the selection holds bars/cells or clips.</summary>
    public static bool Any(SelectionSnapshot? selection) => selection is { } s && (s.HasRange || s.Clips.Count > 0);

    /// <summary>The tracks whose cells the bar range keeps: the selected track rows, else every track for a timeline range or the range's own track for a score range.</summary>
    public static IReadOnlyList<TrackModel> RangeTracks(SongProject project, SelectionSnapshot selection)
    {
        if (!selection.HasRange) return [];
        if (selection.Rows.Count > 0) return project.Tracks.Where(t => selection.Rows.Contains(t.Id)).ToList();
        if (selection.Scope == SelectionScope.AllTracks) return project.Tracks.ToList();
        return selection.TrackIndex >= 0 && selection.TrackIndex < project.Tracks.Count ? [project.Tracks[selection.TrackIndex]] : [];
    }

    private static List<TimelineClipSelection> ClipsIn(SongProject project, SelectionSnapshot selection) =>
        selection.Clips.Select(c => TimelineClipSelection.Find(project, c.Track, c.Clip)).OfType<TimelineClipSelection>().ToList();

    /// <summary>How many tracks the snip plays, and the clip count.</summary>
    public static (int Tracks, int Clips) Counts(SongProject project, SelectionSnapshot selection)
    {
        var clips = ClipsIn(project, selection);
        return (RangeTracks(project, selection).Select(t => t.Id).Union(clips.Select(c => c.Track.Id)).Count(), clips.Count);
    }

    /// <summary>The copy: bars outside the range are empty on every track, tracks outside the selection are empty and muted, only the selected clips stay.</summary>
    public static RenderSnipResult Build(SongProject project, SelectionSnapshot selection)
    {
        var rangeTracks = RangeTracks(project, selection);
        var clips = ClipsIn(project, selection);
        var map = new Dictionary<TrackModel, TrackModel>();
        var tracks = new List<TrackModel>();
        foreach (var track in project.Tracks)
        {
            var copy = track.ShallowCopy();
            var keepNotes = rangeTracks.Contains(track);
            var keepClips = clips.Where(c => c.Track == track).Select(c => c.Clip.Id).ToHashSet();
            copy.Measures = track.Measures.Select((m, bar) => Snipped(m, keepNotes ? selection : null, bar)).ToList();
            copy.AudioClips = track.AudioClips.Where(c => keepClips.Contains(c.Id)).ToList();
            copy.Solo = false;
            if (!keepNotes && keepClips.Count == 0) copy.Mute = true;
            map[track] = copy;
            tracks.Add(copy);
        }
        return new RenderSnipResult(project.WithTracks(tracks), map);
    }

    private static MeasureModel Snipped(MeasureModel measure, SelectionSnapshot? keep, int bar)
    {
        var copy = measure.Clone();
        for (var i = 0; i < copy.Cells.Count; i++)
            if (keep is null || !CellKept(keep, bar, i)) copy.Cells[i] = new TabCell();
        for (var i = 0; i < copy.Voice2Cells.Count; i++)
            if (keep is null || !CellKept(keep, bar, i)) copy.Voice2Cells[i] = new TabCell();
        return copy;
    }

    private static bool CellKept(SelectionSnapshot s, int bar, int cell) =>
        bar >= s.StartBar && bar <= s.EndBar && (bar != s.StartBar || cell >= s.StartCell) && (bar != s.EndBar || s.EndCell < 0 || cell <= s.EndCell);

    /// <summary>The song time covering the selected cells and clips (earliest start to latest end), in milliseconds; null when nothing is selected.</summary>
    public static (double StartMs, double EndMs)? Range(ScoreTimeline timeline, SongProject project, SelectionSnapshot selection)
    {
        double? start = null, end = null;
        if (selection.HasRange)
        {
            var (s, e) = RenderSpecBuilder.Bounds(timeline, RenderBounds.TimeSelection, selection.StartBar, selection.StartCell, selection.EndBar, selection.EndCell, 0, 0);
            start = s; end = e;
        }
        foreach (var c in ClipsIn(project, selection))
        {
            start = Math.Min(start ?? double.MaxValue, c.Clip.StartSec * 1000);
            end = Math.Max(end ?? 0, c.Clip.EndSec * 1000);
        }
        return start is null || end is null ? null : (start.Value, Math.Max(start.Value, end.Value));
    }
}
