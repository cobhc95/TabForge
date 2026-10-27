using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: what Copy and Delete/Cut do to the cells of the timeline bar highlight (tab tracks x written bars): the tab-only snapshot, clearing the notes of each highlighted tab track
//   (the bars stay) and the number of cells.
// Does not own: the highlight (BarHighlight), the undo step and the question window (the caller), the paste (RangePaste, which the highlight paste reuses from the highlight's top-left cell).
// Tests: TabForge.Avalonia.Tests.TimelineBarHighlightEditTests.
public static class HighlightEdits
{
    /// <summary>The tab tracks inside the highlight, in track order (a track with only clips has no cells).</summary>
    public static IReadOnlyList<int> TabTracks(SongProject project, BarHighlight highlight) =>
        highlight.Has ? Enumerable.Range(highlight.FirstTrack, Math.Max(0, Math.Min(highlight.LastTrack, project.Tracks.Count - 1) - highlight.FirstTrack + 1)).Where(i => project.Tracks[i].HasNotation).ToList() : Array.Empty<int>();

    /// <summary>The number of highlighted cells: one per tab track and bar.</summary>
    public static int CellCount(SongProject project, BarHighlight highlight, bool includeExtras = true) =>
        TabTracks(project, highlight).Count * highlight.BarCount + (includeExtras ? highlight.ExtraCount : 0);

    /// <summary>The snapshot of the highlight's rectangle (the clipboard holds one rectangle: single Ctrl-added cells are not in it, so a Cut leaves them): the notes of the tab tracks, no clips. Throws <see cref="InvalidDataException"/> (user-facing) when there is nothing to copy.</summary>
    public static RangeSnapshot Capture(SongProject project, BarHighlight highlight) =>
        RangeCapture.Capture(project, highlight.FirstBar, highlight.LastBar, highlight.FirstTrack, highlight.LastTrack, tabsOnly: true);

    /// <summary>Empties the highlighted cells (every bar stays in place). Pure model code: the caller wraps it in one undo step. True when a note went.</summary>
    public static bool Clear(SongProject project, BarHighlight highlight, bool fillRests, bool includeExtras = true)
    {
        var changed = false;
        foreach (var track in TabTracks(project, highlight))
            changed |= TimelineRangeEdit.Apply(project, highlight.FirstBar, highlight.LastBar, TimelineDeleteMode.LeaveEmpty, track, fillRests).Changed;
        if (includeExtras) foreach (var (track, bar) in highlight.Extras)
            if (track < project.Tracks.Count && project.Tracks[track].HasNotation) changed |= TimelineRangeEdit.Apply(project, bar, bar, TimelineDeleteMode.LeaveEmpty, track, fillRests).Changed;
        return changed;
    }
}
