using TabForge.Models;

namespace TabForge.Documents;

// Owns: the contract of what an open document keeps beside its undo stack, per undo state: the tuning shift, the skipped areas and the selection shown
//   with each state, playback's bar mapping, the song identity a replaced song carries, and the clean (saved) content check; and the fixed order in
//   which an undo or redo remembers and restores them (RememberForReturn before the song is replaced, RestoreSide after it).
// Does not own: the undo stacks (UndoController), the edit pipeline that calls it (DocumentEdits) or the tables themselves (SongDocument implements it).
// Tests: UndoSideStateCharacterizationTests.

/// <summary>The side state an edit leaves behind, taken just before the edit runs.</summary>
public readonly record struct UndoSideState(SelectionSnapshot? Selection, int[]? TuningShift, (int Start, int End)[]? SkipRanges);

/// <summary>What <see cref="DocumentEdits"/> remembers and restores beside the song for one open document. UI thread only.</summary>
public interface IUndoMemory
{
    /// <summary>The side state as it is now (taken before an edit runs).</summary>
    UndoSideState Capture();

    /// <summary>An edit stored <paramref name="stored"/>: playback keeps its bar mapping, and the state before the edit (<paramref name="before"/>) is kept with it
    /// (the selection, the tuning shift, and the skipped areas when the edit moved them).</summary>
    void RememberEdit(UndoSnapshot stored, UndoSideState before);

    /// <summary>A gesture checkpoint stored <paramref name="stored"/>: playback keeps its bar mapping.</summary>
    void RememberCheckpoint(UndoSnapshot stored);

    /// <summary>Undo or redo is about to leave <paramref name="current"/> for <paramref name="target"/>: the bar mapping, tuning shift, selection and (when the
    /// target carries skipped areas) the skipped areas of the current state are kept for the way back.</summary>
    void RememberForReturn(UndoSnapshot current, ProjectState target);

    /// <summary>The replaced song's timeline song id moves to the restored song.</summary>
    void CarrySongId(SongProject from, SongProject to);

    /// <summary>True when the restored <paramref name="snapshot"/> is exactly the saved content (undoing back to it clears the unsaved mark).</summary>
    bool IsCleanContent(UndoSnapshot snapshot);

    /// <summary>After the song of <paramref name="target"/> is back: its tuning shift, skipped areas and selection come back, in that order.</summary>
    void RestoreSide(ProjectState target);
}

/// <summary>A document that keeps nothing beside its undo stack (the score editor's preview).</summary>
public sealed class NoUndoMemory : IUndoMemory
{
    public static readonly NoUndoMemory Instance = new();
    private NoUndoMemory() { }
    public UndoSideState Capture() => default;
    public void RememberEdit(UndoSnapshot stored, UndoSideState before) { }
    public void RememberCheckpoint(UndoSnapshot stored) { }
    public void RememberForReturn(UndoSnapshot current, ProjectState target) { }
    public void CarrySongId(SongProject from, SongProject to) { }
    public bool IsCleanContent(UndoSnapshot snapshot) => false;
    public void RestoreSide(ProjectState target) { }
}
