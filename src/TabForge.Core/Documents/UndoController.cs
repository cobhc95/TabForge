using TabForge.Models;

namespace TabForge.Documents;

/// <summary>One undo level: an immutable song state (see <see cref="ProjectState"/>), the timeline highlight and loop region that went with it (null: not recorded)
/// and the readable label of the action that made it.</summary>
public readonly record struct UndoSnapshot(ProjectState State, long Order = 0, string Label = "", BarHighlightSnapshot? Highlight = null, LoopRegionSnapshot? Loop = null,
    bool RestoresLoopRepeat = false, SelectionSnapshot? Selection = null)
{
    /// <summary>Content hash: equal songs give equal fingerprints (keys the playback bar mappings).</summary>
    public string Fingerprint => State.Fingerprint;
}

public readonly record struct UndoCapture(UndoSnapshot Snapshot, bool Stored);

// Owns: one document's undo history and its memory budget.
// Does not own: when edits are captured (DocumentEdits) and the song model.
// Tests: no dedicated test class.
/// <summary>
/// Owns one document's undo history and its memory budget. States share every unchanged bar with the state before them
/// so capturing and restoring cost what the edit changed rather than the whole song. UI thread only.
/// </summary>
public sealed class UndoController
{
    private readonly Stack<UndoSnapshot> _undo = new();
    private readonly Stack<UndoSnapshot> _redo = new();
    private readonly ProjectStateEncoder _encoder = new();
    private readonly int _maxLevels;
    private readonly long _maxBytes;
    private long _historyBytes;
    private long _order;
    // The state a gesture (a drag, a dialog-held edit) captured before its first change. MarkChanged commits it: only then is redo dropped.
    private bool _gestureOpen;
    private UndoSnapshot? _gestureBase;

    public UndoController() : this(UndoHistory.MaxLevels, UndoHistory.MaxBytes) { }

    internal UndoController(int maxLevels, long maxBytes)
    {
        if (maxLevels < 1) throw new ArgumentOutOfRangeException(nameof(maxLevels));
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxLevels = maxLevels;
        _maxBytes = maxBytes;
    }

    /// <summary>Reads the document's timeline highlight, which every snapshot carries so undo shows it again (null: snapshots carry none).</summary>
    internal Func<BarHighlightSnapshot>? HighlightSource { get; set; }

    /// <summary>Reads the document's loop region, which every snapshot carries so undo brings a cleared or moved loop back (null: snapshots carry none).</summary>
    internal Func<LoopRegionSnapshot>? LoopSource { get; set; }

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    /// <summary>The newest undo state; an edit that passes it as its earlier state joins that step instead of adding one.</summary>
    public UndoSnapshot? Latest => _undo.TryPeek(out var top) ? top : null;
    /// <summary>The newest redo state, or null.</summary>
    public UndoSnapshot? NextRedo => _redo.TryPeek(out var top) ? top : null;

    /// <summary>
    /// Bytes charged to the history: what each state stored first. The document's baseline state (its first capture) is not
    /// charged, so the history can additionally keep at most one copy of the song's bars.
    /// </summary>
    public long BytesHeld => _historyBytes;

    /// <summary>The current content of <paramref name="project"/> as a state, without adding it to the history.</summary>
    public UndoSnapshot Snapshot(SongProject project) => new(_encoder.Encode(project), Highlight: HighlightSource?.Invoke(), Loop: LoopSource?.Invoke());

    /// <summary>As <see cref="Snapshot(SongProject)"/> with the label the step would carry.</summary>
    public UndoSnapshot Snapshot(SongProject project, string label) => new(_encoder.Encode(project), 0, label, HighlightSource?.Invoke(), LoopSource?.Invoke());

    /// <summary>
    /// Rebuilds the song held by <paramref name="snapshot"/>. <paramref name="live"/> is the song it replaces (the one just passed to
    /// <see cref="Snapshot"/>): its unchanged bars are moved into the result instead of being decoded, so it must be discarded.
    /// </summary>
    public SongProject Restore(UndoSnapshot snapshot, SongProject? live = null) => _encoder.Restore(snapshot.State, live);

    public UndoTransaction BeginTransaction(SongProject project, string label = "Edit")
    {
        CancelGesture();   // a normal edit supersedes a gesture whose first change never came
        return new(this, Snapshot(project, label));
    }

    public UndoCapture Capture(SongProject project, string label = "Edit") => Commit(BeginTransaction(project, label));

    public UndoCapture Commit(UndoTransaction transaction)
    {
        Validate(transaction);
        transaction.Completed = true;
        var stored = PushDistinct(_undo, transaction.Snapshot);
        Clear(_redo);   // a committed edit is a real change, even when its before-state is already the top state
        Trim();
        return new UndoCapture(transaction.Snapshot, stored);
    }

    public void Cancel(UndoTransaction transaction)
    {
        Validate(transaction);
        transaction.Completed = true;
    }

    public UndoCapture Capture(UndoSnapshot snapshot)
    {
        CancelGesture();   // a completed edit supersedes a gesture whose first change never came
        var stored = PushDistinct(_undo, snapshot);
        Clear(_redo);      // the edit changed the song, even when its before-state is already the top state
        Trim();
        return new UndoCapture(snapshot, stored);
    }

    /// <summary>
    /// Holds the state a gesture starts from without adding a step or dropping redo: a gesture that changes nothing leaves both untouched.
    /// <see cref="CommitGesture"/> stores it and drops redo when the gesture's first real change marks the song.
    /// </summary>
    public UndoCapture BeginGesture(SongProject project, string label = "Edit")
    {
        _gestureBase ??= Snapshot(project, label);
        _gestureOpen = true;
        return new UndoCapture(_gestureBase.Value, false);
    }

    /// <summary>The gesture made a real change (the caller is marking the song changed): store its one step, so redo goes.</summary>
    public void CommitGesture()
    {
        if (!_gestureOpen) return;
        _gestureOpen = false;
        if (_gestureBase is not { } snapshot) return;
        _gestureBase = null;
        PushDistinct(_undo, snapshot);
        Clear(_redo);
        Trim();
    }

    /// <summary>The gesture ended without a change (a cancelled dialog): no step is stored and the redo stack stays as it was.</summary>
    public void CancelGesture()
    {
        _gestureOpen = false;
        _gestureBase = null;
    }

    public bool Discard(UndoCapture capture)
    {
        if (_gestureBase is { } pending && ReferenceEquals(pending.State, capture.Snapshot.State))
        {
            _gestureBase = null;
            _gestureOpen = false;
            return true;
        }
        if (!capture.Stored || !_undo.TryPeek(out var top) ||
            !ReferenceEquals(top.State, capture.Snapshot.State)) return false;
        _undo.Pop();
        _historyBytes -= top.State.OwnBytes;
        _gestureOpen = false;
        _gestureBase = null;
        Trim();
        return true;
    }

    public bool TryUndo(UndoSnapshot current, out UndoSnapshot target)
    {
        if (!_undo.TryPop(out target)) return false;
        _historyBytes -= target.State.OwnBytes;
        PushDistinct(_redo, current);
        Trim();
        return true;
    }

    public bool TryRedo(UndoSnapshot current, out UndoSnapshot target)
    {
        if (!_redo.TryPop(out target)) return false;
        _historyBytes -= target.State.OwnBytes;
        PushDistinct(_undo, current);
        Trim();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _historyBytes = 0;
        _gestureOpen = false;
        _gestureBase = null;
    }

    private bool PushDistinct(Stack<UndoSnapshot> stack, UndoSnapshot snapshot)
    {
        if (snapshot.State.OwnBytes > _maxBytes) return false;
        if (stack.TryPeek(out var top) && top.State.ContentEquals(snapshot.State) && top.Highlight == snapshot.Highlight && top.Loop == snapshot.Loop) return false;

        snapshot = snapshot with { Order = ++_order };
        stack.Push(snapshot);
        _historyBytes += snapshot.State.OwnBytes;
        return true;
    }

    private void Clear(Stack<UndoSnapshot> stack)
    {
        foreach (var snapshot in stack) _historyBytes -= snapshot.State.OwnBytes;
        stack.Clear();
    }

    private void Trim()
    {
        while (_historyBytes > _maxBytes || _undo.Count + _redo.Count > _maxLevels)
        {
            var undoOldest = _undo.Count == 0 ? long.MaxValue : _undo.Last().Order;
            var redoOldest = _redo.Count == 0 ? long.MaxValue : _redo.Last().Order;
            if (undoOldest == long.MaxValue && redoOldest == long.MaxValue) break;
            RemoveOldest(undoOldest <= redoOldest ? _undo : _redo);
        }
    }

    private void RemoveOldest(Stack<UndoSnapshot> stack)
    {
        var newestFirst = stack.ToArray();
        _historyBytes -= newestFirst[^1].State.OwnBytes;
        stack.Clear();
        for (var index = newestFirst.Length - 2; index >= 0; index--) stack.Push(newestFirst[index]);
    }

    private void Validate(UndoTransaction transaction)
    {
        if (!ReferenceEquals(transaction.Owner, this) || transaction.Completed)
            throw new InvalidOperationException("Undo transaction is no longer active.");
    }

    public sealed class UndoTransaction
    {
        internal UndoTransaction(UndoController owner, UndoSnapshot snapshot)
        {
            Owner = owner;
            Snapshot = snapshot;
        }

        internal UndoController Owner { get; }
        internal UndoSnapshot Snapshot { get; }
        internal bool Completed { get; set; }
    }
}
