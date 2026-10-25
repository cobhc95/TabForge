using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>
/// The text of the Song stats window: the heading (title and file name) and the counts line.
/// Owns: counting tracks, bars, notes and sections, and formatting both lines.
/// Does not own: the window's layout (SongStatsWindow) or the score.
/// Tests: no dedicated test class.
/// </summary>
public static class SongStatsPresenter
{
    public static string Heading(SongProject project, string? path) => string.IsNullOrWhiteSpace(path)
        ? $"{project.Title}{TitleMarkers.UnsavedNote}"
        : $"{project.Title} — {Path.GetFileName(path)}";

    public static string CountsLine(SongProject project)
    {
        var notes = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
        var bars = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        return $"Tracks {project.Tracks.Count}   Bars {bars}   Notes {notes}   Sections {project.Markers.Count}";
    }
}
