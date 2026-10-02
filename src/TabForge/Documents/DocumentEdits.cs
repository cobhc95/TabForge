using TabForge.Models;

namespace TabForge.Documents;

public readonly record struct EditResult(bool Changed, UndoCapture Capture);

public readonly record struct EditResult<T>(bool Changed, T? Value, UndoCapture Capture) where T : class;

/// <summary>
/// The model half of every logical edit of one open document, for an explicit <see cref="DocumentSession"/>. A window, a keyboard shortcut, a menu
/// item, a toolbar button and a context menu all reach the model through here, so one logical edit is always exactly: one undo transaction
/// (nothing is stored when the edit changed nothing), one dirty change and one timeline invalidation (<see cref="SongProject.MarkTimelineChanged"/>),
/// whichever entry point started it. Nothing here knows about windows, the displayed tab or a "current" document; callers refresh the view afterwards
/// and must not mark the project changed again.
/// </summary>
public static class DocumentEdits
{
    /// <summary>
    /// Runs <paramref name="mutate"/> on the document's song. <paramref name="mutate"/> returns whether it changed anything: false cancels the undo
    /// transaction and leaves the document untouched. <paramref name="before"/> is an undo state taken earlier (a drag started with the model unchanged);
    /// the edit then stores that state instead of taking a new one. <paramref name="invalidatesTimeline"/> is false for edits that cannot change timing
    /// (mixer levels, bus switches, colours, list settings): they are still one undo step and one dirty change, and playback timing caches are not rebuilt.
    /// </summary>
    public static EditResult Run(DocumentSession document, Func<SongProject, bool> mutate, UndoSnapshot? before = null, bool invalidatesTimeline = true)
    {
        var result = Run<object>(document, project => mutate(project) ? ChangedMarker : null, before, invalidatesTimeline);
        return new EditResult(result.Changed, result.Capture);
    }

    private static readonly object ChangedMarker = new();

    /// <summary>As <see cref="Run(DocumentSession, Func{SongProject, bool}, UndoSnapshot?)"/>; <paramref name="mutate"/> returns what the edit produced (a bar mapping, a result record), or null when nothing changed.</summary>
    public static EditResult<T> Run<T>(DocumentSession document, Func<SongProject, T?> mutate, UndoSnapshot? before = null, bool invalidatesTimeline = true) where T : class
    {
        var project = document.Project;
        using var timeline = project.BeginTimelineBatch();   // a model step that marks the timeline itself (bar grid) and this edit's own mark are one invalidation
        var transaction = before is null ? document.Undo.BeginTransaction(project) : null;
        var shiftBefore = (int[])document.TuningShift.Clone();
        var skipBefore = document.SkipRanges.ToArray();
        T? value;
        try { value = mutate(project); }
        catch
        {
            if (transaction is not null) document.Undo.Cancel(transaction);
            throw;
        }
        if (value is null)
        {
            if (transaction is not null) document.Undo.Cancel(transaction);
            return new EditResult<T>(false, null, default);
        }
        var capture = transaction is not null ? document.Undo.Commit(transaction) : document.Undo.Capture(before!.Value);
        if (capture.Stored)
        {
            document.Playback.RememberBarMapping(capture.Snapshot);
            document.RememberTuningShift(capture.Snapshot.State, shiftBefore);
            if (!skipBefore.AsSpan().SequenceEqual(document.SkipRanges.ToArray())) document.RememberSkipRanges(capture.Snapshot.State, skipBefore);   // only an edit that moved them
        }
        MarkChanged(document, invalidatesTimeline);
        return new EditResult<T>(true, value, capture);
    }

    /// <summary>
    /// Stores the document's current state as the undo step of a change that follows (a drag, a dialog the user may cancel, a gesture whose model
    /// changes arrive over several events). Nothing is marked changed here: the change that follows marks it. The returned capture is what
    /// <see cref="UndoController.Discard"/> takes when the change is cancelled.
    /// </summary>
    public static UndoCapture Checkpoint(DocumentSession document)
    {
        var capture = document.Undo.Capture(document.Project);
        if (capture.Stored) document.Playback.RememberBarMapping(capture.Snapshot);
        return capture;
    }

    /// <summary>The dirty change and timeline invalidation of a change whose undo step was recorded separately (a committed transaction, a <see cref="Checkpoint"/>).</summary>
    public static void MarkChanged(DocumentSession document, bool invalidatesTimeline = true)
    {
        document.Project.IsDirty = true;
        if (invalidatesTimeline) document.Project.MarkTimelineChanged();
    }

    /// <summary>Undo one step. Returns the restored state (the view then refreshes), or null when there is nothing to undo.</summary>
    public static UndoSnapshot? Undo(DocumentSession document)
    {
        if (!document.Undo.CanUndo) return null;
        var current = document.Undo.Snapshot(document.Project);
        if (!document.Undo.TryUndo(current, out var target)) return null;
        document.Playback.RememberBarMapping(current);
        document.RememberTuningShift(current.State);
        document.RememberSkipRangesForReturn(current.State, target.State);
        Restore(document, target);
        document.RestoreTuningShift(target.State);
        document.RestoreSkipRanges(target.State);
        return target;
    }

    /// <summary>Redo one step; see <see cref="Undo"/>.</summary>
    public static UndoSnapshot? Redo(DocumentSession document)
    {
        if (!document.Undo.CanRedo) return null;
        var current = document.Undo.Snapshot(document.Project);
        if (!document.Undo.TryRedo(current, out var target)) return null;
        document.Playback.RememberBarMapping(current);
        document.RememberTuningShift(current.State);
        document.RememberSkipRangesForReturn(current.State, target.State);
        Restore(document, target);
        document.RestoreTuningShift(target.State);
        document.RestoreSkipRanges(target.State);
        return target;
    }

    private static void Restore(DocumentSession document, UndoSnapshot snapshot)
    {
        // Unchanged bars move over from the song being replaced; only the bars the undo changes are rebuilt.
        document.Project = document.Undo.Restore(snapshot, document.Project);
        MarkChanged(document);   // undo / redo may restore in place
        // Undoing back to the saved state clears the "*": the exact content check runs here only.
        if (document.IsCleanContent(snapshot)) document.Project.IsDirty = false;
    }
}
