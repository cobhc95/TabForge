using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Documents;

/// <summary>
/// Owns: what <see cref="DocumentEdits"/> needs of one open document (song, undo stack, the editor memory tied to song states, the clean baseline).
/// Does not own: playback, files or windows; the app's document class implements this and keeps those.
/// Tests: UndoRulesTests.
/// </summary>
public interface IEditableDocument : IEditTarget
{
    new SongProject Project { get; set; }
    UndoController Undo { get; }
    int[] TuningShift { get; }
    List<(int Start, int End)> SkipRanges { get; }
    /// <summary>Playback keeps the bar mapping of <paramref name="snapshot"/> so a song edited while it plays continues at the same bar.</summary>
    void RememberBarMapping(UndoSnapshot snapshot);
    /// <summary>The replaced song's timeline song id moves to the restored song.</summary>
    void CarrySongId(SongProject from, SongProject to);
    void RememberTuningShift(ProjectState state);
    void RememberTuningShift(ProjectState state, int[] shift);
    void RestoreTuningShift(ProjectState state);
    void RememberSkipRanges(ProjectState state, (int Start, int End)[] ranges);
    void RememberSkipRangesForReturn(ProjectState current, ProjectState target);
    void RestoreSkipRanges(ProjectState state);
    bool IsCleanContent(UndoSnapshot snapshot);
    /// <summary>The document's one selection (S1): kept when the song is replaced and restored by undo and redo.</summary>
    SelectionModel Selection { get; }
    /// <summary>The document's timeline bar highlight; its moves are undo steps (<see cref="DocumentEdits.RecordHighlight"/>) and an undo or redo shows the one that went with the restored state.</summary>
    BarHighlight Highlight { get; }
    /// <summary>Keeps <paramref name="selection"/> with the undo step <paramref name="state"/> (the selection to show when that state is restored).</summary>
    void RememberSelection(ProjectState state, SelectionSnapshot selection);
    /// <summary>Shows the selection kept with <paramref name="state"/> (nothing kept: the current one, kept inside the restored song).</summary>
    void RestoreSelection(ProjectState state);
    /// <summary>Raised when the document's current song marks its timeline; keeps firing for the next song after <see cref="Project"/> is replaced.</summary>
    event Action<SongProject>? TimelineMarked;
    /// <summary>Raised once after <see cref="Project"/> was replaced (undo, redo, open): the old song, then the new one.</summary>
    event Action<SongProject, SongProject>? ProjectChanged;
    /// <summary>Raised once after an undo or redo finished restoring the document (song, tuning shift, skipped areas, dirty mark).</summary>
    event Action? Restored;
    /// <summary>Raises <see cref="Restored"/>; <see cref="DocumentEdits.Undo"/> and <see cref="DocumentEdits.Redo"/> call it after the final state.</summary>
    void NotifyRestored();

    EditOutcome IEditTarget.Run(Func<SongProject, bool> mutate, bool invalidatesTimeline, string label)
    {
        var result = DocumentEdits.Run(this, mutate, invalidatesTimeline: invalidatesTimeline, label: label);
        return new EditOutcome(result.Changed);
    }

    EditOutcome<T> IEditTarget.Run<T>(Func<SongProject, T?> mutate, bool invalidatesTimeline, string label) where T : class
    {
        var result = DocumentEdits.Run(this, mutate, invalidatesTimeline: invalidatesTimeline, label: label);
        return new EditOutcome<T>(result.Changed, result.Value);
    }
}
