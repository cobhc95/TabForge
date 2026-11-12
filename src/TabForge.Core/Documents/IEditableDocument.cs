using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Documents;

/// <summary>
/// Owns: what <see cref="DocumentEdits"/> needs of one open document: the song, the undo stack, the side state kept per undo state (<see cref="Memory"/>),
/// the selection and highlight, and the change events.
/// Does not own: playback, files or windows; the app's document class implements this and keeps those. The IEditTarget.Run pair is implemented by each
/// document as a call to DocumentEdits.Run (the interface does not call back into DocumentEdits).
/// Tests: UndoRulesTests, UndoSideStateCharacterizationTests.
/// </summary>
public interface IEditableDocument : IEditTarget
{
    new SongProject Project { get; set; }
    UndoController Undo { get; }
    /// <summary>What undo and redo keep and restore beside the song (tuning shift, skipped areas, selection per state, bar mapping, clean check).</summary>
    IUndoMemory Memory { get; }
    /// <summary>The document's one selection (S1): kept when the song is replaced and restored by undo and redo.</summary>
    SelectionModel Selection { get; }
    /// <summary>The document's timeline bar highlight; its moves are undo steps (<see cref="DocumentEdits.RecordHighlight"/>) and an undo or redo shows the one that went with the restored state.</summary>
    BarHighlight Highlight { get; }
    /// <summary>Raised when the document's current song marks its timeline; keeps firing for the next song after <see cref="Project"/> is replaced.</summary>
    event Action<SongProject>? TimelineMarked;
    /// <summary>Raised once after <see cref="Project"/> was replaced (undo, redo, open): the old song, then the new one.</summary>
    event Action<SongProject, SongProject>? ProjectChanged;
    /// <summary>Raised once after an undo or redo finished restoring the document (song, tuning shift, skipped areas, dirty mark).</summary>
    event Action? Restored;
    /// <summary>Raises <see cref="Restored"/>; <see cref="DocumentEdits.Undo"/> and <see cref="DocumentEdits.Redo"/> call it after the final state.</summary>
    void NotifyRestored();
}
