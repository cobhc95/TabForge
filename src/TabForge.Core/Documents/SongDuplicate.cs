using TabForge.Services;

namespace TabForge.Documents;

// Owns: making the unsaved copy of an open song for Tab.Duplicate and the tab menu's Duplicate row: the project copied through its snapshot,
//   the title suffixed " copy", the media scope and the view state (cursor, track, notation, zoom, paper) carried over.
// Does not own: placing the copy among the tabs (MainWindowTabs), the undo history and the save baseline (a new song starts unsaved),
//   and the approvals of the original's media (a new song takes its own approvals).
// Tests: no dedicated test class.
/// <summary>Makes an unsaved copy of a song. UI thread only.</summary>
public static class SongDuplicate
{
    /// <summary>The title suffix of a copy; a song with no title becomes <see cref="UntitledCopyTitle"/>.</summary>
    public const string CopySuffix = " copy";
    public const string UntitledCopyTitle = "Untitled copy";

    /// <summary>A new unsaved song with the same score and view state as <paramref name="source"/>.</summary>
    public static SongDocument Copy(SongDocument source, Func<AudioSettings?> audioSettings)
    {
        var project = ProjectService.CloneProject(source.Project);
        project.Title = string.IsNullOrWhiteSpace(project.Title) ? UntitledCopyTitle : project.Title + CopySuffix;
        project.IsDirty = true;
        var copy = new SongDocument(audioSettings) { Project = project };
        copy.Media.SetSourceDirectory(source.Media.BaseDirectory);   // its relative media still resolves where the original's did
        copy.CursorBar = source.CursorBar;
        copy.CursorCell = source.CursorCell;
        copy.CursorString = source.CursorString;
        copy.TrackIndex = source.TrackIndex;
        copy.Notation = source.Notation;
        copy.ZoomFactor = source.ZoomFactor;
        copy.ContinuousScoreView = source.ContinuousScoreView;
        copy.HorizontalScoreView = source.HorizontalScoreView;
        // A copy of a song on the default paper stays on the default paper, so it still follows a theme change.
        if (source.PaperIsDefault) copy.ApplyDefaultPaper(source.DarkPaper); else copy.DarkPaper = source.DarkPaper;
        copy.DurationDenominator = source.DurationDenominator;
        return copy;
    }
}
