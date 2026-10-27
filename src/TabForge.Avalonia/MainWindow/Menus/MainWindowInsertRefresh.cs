using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: the one post-edit refresh every Insert action ends in (a track, bars, a section, a clip, a file): the arrangement re-reads the song rows at once
//   (track list, lanes, section strip, ruler width), a new track is selected and scrolled into view, and the score lays out again.
// Does not own: the edits themselves (MainWindowInsertMenu, MainWindowTrackMenu, MainWindowEdit), the clip post-edit path (MainWindowClipRefresh: lane compaction and the engine sync).
// Tests: TabForge.Avalonia.Tests.InsertMenuTests.
/// <summary>The shared "an insert landed" refresh. UI thread only.</summary>
internal static class MainWindowInsertRefresh
{
    /// <summary>An insert changed <paramref name="song"/>: reload the arrangement rows, show <paramref name="newTrack"/> (when one was added), and reflow the score.</summary>
    public static void Landed(MainWindowDocumentPanes panes, ScorePreviewView score, SongDocument song, TrackModel? newTrack = null)
    {
        if (panes.Arrangement is { } arrangement && ReferenceEquals(arrangement.Document, song))
        {
            arrangement.ReloadRows(song.Project);
            if (newTrack is not null) arrangement.ShowTrack(newTrack);
            arrangement.Timeline.InvalidateVisual();
        }
        score.Refresh();
    }
}
