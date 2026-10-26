using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

public readonly record struct EditResult(bool Changed, UndoCapture Capture);

public readonly record struct EditResult<T>(bool Changed, T? Value, UndoCapture Capture) where T : class;

// Owns: the one model-side wrapper every logical edit of an open document goes through: the undo transaction, the dirty mark and the timeline invalidation.
// Does not own: the edit rules inside each caller's body, the undo stacks (UndoController), or any window, tab or current-document lookup.
// Tests: no dedicated test class.

/// <summary>
/// The model half of every logical edit of one open document, for an explicit <see cref="IEditableDocument"/>. A window, a keyboard shortcut, a menu
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
    public static EditResult Run(IEditableDocument document, Func<SongProject, bool> mutate, UndoSnapshot? before = null, bool invalidatesTimeline = true, string label = "Edit")
    {
        var result = Run<object>(document, project => mutate(project) ? ChangedMarker : null, before, invalidatesTimeline, label);
        return new EditResult(result.Changed, result.Capture);
    }

    private static readonly object ChangedMarker = new();

    /// <summary>As <see cref="Run(IEditableDocument, Func{SongProject, bool}, UndoSnapshot?)"/>; <paramref name="mutate"/> returns what the edit produced (a bar mapping, a result record), or null when nothing changed.</summary>
    public static EditResult<T> Run<T>(IEditableDocument document, Func<SongProject, T?> mutate, UndoSnapshot? before = null, bool invalidatesTimeline = true, string label = "Edit") where T : class
    {
        var project = document.Project;
        using var timeline = project.BeginTimelineBatch();   // a model step that marks the timeline itself (bar grid) and this edit's own mark are one invalidation
        var transaction = before is null ? document.Undo.BeginTransaction(project, label) : null;
        var shiftBefore = (int[])document.TuningShift.Clone();
        var skipBefore = document.SkipRanges.ToArray();
        var selectionBefore = document.Selection.Snapshot();
        var trackIds = document.Highlight.Has ? project.Tracks.Select(track => track.Id).ToArray() : null;
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
        var capture = transaction is not null ? document.Undo.Commit(transaction) : document.Undo.Capture(before!.Value with { Label = label });
        if (capture.Stored)
        {
            document.RememberBarMapping(capture.Snapshot);
            document.RememberSelection(capture.Snapshot.State, selectionBefore);
            document.RememberTuningShift(capture.Snapshot.State, shiftBefore);
            if (!skipBefore.AsSpan().SequenceEqual(document.SkipRanges.ToArray())) document.RememberSkipRanges(capture.Snapshot.State, skipBefore);   // only an edit that moved them
        }
        FollowHighlight(document, trackIds, value as int[]);
        MarkChanged(document, invalidatesTimeline);
        return new EditResult<T>(true, value, capture);
    }

    // The highlight keeps its tracks by Id and its bars through the edit's bar map (null: no bar moved), so a track delete or reorder and a bar insert or delete carry it along.
    private static void FollowHighlight(IEditableDocument document, Guid[]? trackIds, int[]? barMap)
    {
        if (trackIds is null) return;
        var tracks = document.Project.Tracks;
        var trackMap = trackIds.Select(id => tracks.FindIndex(track => track.Id == id)).ToArray();
        document.Highlight.Remap(trackMap.Select((now, was) => now == was).All(same => same) ? null : trackMap, barMap);
    }

    /// <summary>
    /// Captures the document's current state as the undo step of a change that follows (a drag, a dialog the user may cancel, a gesture whose model
    /// changes arrive over several events). It is stored only when the change marks the song, so a gesture that changes nothing leaves no step and
    /// the redo stack intact. Nothing is marked changed here: the change that follows marks it. The returned capture is what
    /// <see cref="UndoController.Discard"/> takes when the change is cancelled.
    /// </summary>
    public static UndoCapture Checkpoint(IEditableDocument document, string label = "Edit")
    {
        var capture = document.Undo.BeginGesture(document.Project, label);
        if (capture.Stored) document.RememberBarMapping(capture.Snapshot);
        return capture;
    }

    /// <summary>The dirty change and timeline invalidation of a change whose undo step was recorded separately (a committed transaction, a <see cref="Checkpoint"/>).</summary>
    public static void MarkChanged(IEditableDocument document, bool invalidatesTimeline = true)
    {
        // A change inside a checkpoint gesture is the edit that really happened: only now does its redo stack go.
        document.Undo.CommitGesture();
        document.Project.IsDirty = true;
        document.Highlight.ClampTo(document.Project.Tracks.Count, BarRangeEditor.MaxMeasures(document.Project));   // an edit may have removed the bars or tracks it covered
        if (invalidatesTimeline) document.Project.MarkTimelineChanged();
    }

    /// <summary>
    /// A highlight move as one undo step: the song stays as it is and the step returns to <paramref name="before"/>, the highlight as it was. Does nothing when the highlight did not change.
    /// The song is not marked changed (a highlight move is not an edit of the file).
    /// </summary>
    public static void RecordHighlight(IEditableDocument document, BarHighlightSnapshot before, string label = "Move highlight")
    {
        if (before == document.Highlight.Snapshot()) return;
        var sel = document.Highlight.PendingSelection; document.Highlight.PendingSelection = null;   // a score selection this move cleared comes back in the same step
        document.Undo.Capture(document.Undo.Snapshot(document.Project, label) with { Highlight = before, Selection = sel });
    }

    /// <summary>One undo step for a selection that replaced others: whichever of <paramref name="highlight"/>, <paramref name="loop"/>, <paramref name="selection"/> is given comes back with Ctrl+Z (the Loop button stays).</summary>
    public static void RecordSwap(IEditableDocument document, string label, BarHighlightSnapshot? highlight, LoopRegionSnapshot? loop, SelectionSnapshot? selection)
    {
        if (highlight == document.Highlight.Snapshot()) highlight = null;
        if (loop == document.Selection.LoopSnapshot()) loop = null;
        if (highlight is null && loop is null && selection is null) return;
        document.Undo.Capture(document.Undo.Snapshot(document.Project, label) with { Highlight = highlight, Loop = loop, Selection = selection });
    }

    /// <summary>
    /// A loop region change (clear, ruler drag or adjust) as one undo step: the song stays as it is and the step returns to <paramref name="before"/>.
    /// Does nothing when the loop did not change; the song is not marked changed (the loop is not saved in the file).
    /// </summary>
    /// <param name="withRepeat">The step also gives back the Loop button (the Esc loop clear); other steps restore the region and keep the button as it is.</param>
    public static void RecordLoop(IEditableDocument document, LoopRegionSnapshot before, string label = "Loop", bool withRepeat = false)
    {
        if (before == document.Selection.LoopSnapshot()) return;
        document.Undo.Capture(document.Undo.Snapshot(document.Project, label) with { Loop = before, RestoresLoopRepeat = withRepeat });
    }

    /// <summary>Escape (S7): clears the white selection first, then the loop region (Loop off) as one undo step.</summary>
    public static EscapeResult Escape(IEditableDocument document)
    {
        var before = document.Selection.LoopSnapshot();
        var result = document.Selection.EscapeNext();
        if (result == EscapeResult.Loop) RecordLoop(document, before, "Clear loop");
        return result;
    }

    /// <summary>Runs one loop change (<paramref name="change"/>) as one undo step; true when the loop changed.</summary>
    public static bool EditLoop(IEditableDocument document, Func<SelectionModel, bool> change, string label = "Loop", bool withRepeat = false)
    {
        var before = document.Selection.LoopSnapshot();
        if (!change(document.Selection)) return false;
        RecordLoop(document, before, label, withRepeat);
        return true;
    }

    /// <summary>The fingerprint of the document's song as it is now (what an undo step's snapshot of it would carry).</summary>
    public static string Fingerprint(IEditableDocument document) => document.Undo.Snapshot(document.Project).Fingerprint;

    /// <summary>The song's state as it is now, as an immutable state the autosave's worker can rebuild a copy from (the UI thread's cheap capture).</summary>
    public static ProjectState CurrentState(IEditableDocument document) => document.Undo.Snapshot(document.Project).State;

    /// <summary>Undo one step. Returns the restored state (the view then refreshes), or null when there is nothing to undo.</summary>
    public static UndoSnapshot? Undo(IEditableDocument document)
    {
        if (!document.Undo.CanUndo) return null;
        var current = document.Undo.Snapshot(document.Project) with { RestoresLoopRepeat = document.Undo.Latest?.RestoresLoopRepeat == true };   // its redo gives the button back too
        if (!document.Undo.TryUndo(current, out var target)) return null;
        document.RememberBarMapping(current);
        document.RememberTuningShift(current.State);
        document.RememberSelection(current.State, document.Selection.Snapshot());
        document.RememberSkipRangesForReturn(current.State, target.State);
        Restore(document, current, target);
        document.RestoreTuningShift(target.State);
        document.RestoreSkipRanges(target.State);
        document.RestoreSelection(target.State);
        document.NotifyRestored();
        return target;
    }

    /// <summary>Redo one step; see <see cref="Undo"/>.</summary>
    public static UndoSnapshot? Redo(IEditableDocument document)
    {
        if (!document.Undo.CanRedo) return null;
        var current = document.Undo.Snapshot(document.Project) with { RestoresLoopRepeat = document.Undo.NextRedo?.RestoresLoopRepeat == true };
        if (!document.Undo.TryRedo(current, out var target)) return null;
        document.RememberBarMapping(current);
        document.RememberTuningShift(current.State);
        document.RememberSelection(current.State, document.Selection.Snapshot());
        document.RememberSkipRangesForReturn(current.State, target.State);
        Restore(document, current, target);
        document.RestoreTuningShift(target.State);
        document.RestoreSkipRanges(target.State);
        document.RestoreSelection(target.State);
        document.NotifyRestored();
        return target;
    }

    private static void Restore(IEditableDocument document, UndoSnapshot current, UndoSnapshot snapshot)
    {
        document.Undo.CancelGesture();   // an undo/redo is not a gesture commit: its own redo stack must survive
        // A step that only moved the highlight leaves the song as it is.
        if (!snapshot.State.ContentEquals(current.State))
        {
            // Unchanged bars move over from the song being replaced; only the bars the undo changes are rebuilt.
            var replaced = document.Project;
            document.Project = document.Undo.Restore(snapshot, replaced);
            document.CarrySongId(replaced, document.Project);
            MarkChanged(document);   // undo / redo may restore in place
            // Undoing back to the saved state clears the "*": the exact content check runs here only.
            if (document.IsCleanContent(snapshot)) document.Project.IsDirty = false;
        }
        if (snapshot.Highlight is { } highlight) document.Highlight.Restore(highlight, document.Project.Tracks.Count, BarRangeEditor.MaxMeasures(document.Project));
        if (snapshot.Selection is { } sel) document.Selection.Restore(sel, document.Project, BarRangeEditor.MaxMeasures(document.Project));
        // Undo gives back the loop region; the Loop button stays as the user left it, except for the step that cleared both (Esc).
        if (snapshot.Loop is { } loop)
            document.Selection.RestoreLoop(snapshot.RestoresLoopRepeat ? loop : loop with { Repeat = document.Selection.LoopRepeat }, BarRangeEditor.MaxMeasures(document.Project));
    }
}
