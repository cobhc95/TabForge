using TabForge.Models;

namespace TabForge.Documents;

/// <summary>One undo level: an immutable song state (see <see cref="ProjectState"/>).</summary>
public readonly record struct UndoSnapshot(ProjectState State, long Order = 0)
{
    /// <summary>Content hash: equal songs give equal fingerprints (keys the playback bar mappings).</summary>
    public string Fingerprint => State.Fingerprint;
}

public readonly record struct UndoCapture(UndoSnapshot Snapshot, bool Stored);

/// <summary>
/// Owns one document's undo history and its memory budget. States share every unchanged bar with the state before them
/// (Audit 3 M-06), so capturing and restoring cost what the edit changed rather than the whole song. UI thread only.
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

    public UndoController() : this(UndoHistory.MaxLevels, UndoHistory.MaxBytes) { }

    internal UndoController(int maxLevels, long maxBytes)
    {
        if (maxLevels < 1) throw new ArgumentOutOfRangeException(nameof(maxLevels));
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxLevels = maxLevels;
        _maxBytes = maxBytes;
    }

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Bytes charged to the history: what each state stored first. The document's baseline state (its first capture) is not
    /// charged, so the history can additionally keep at most one copy of the song's bars.
    /// </summary>
    public long BytesHeld => _historyBytes;

    /// <summary>The current content of <paramref name="project"/> as a state, without adding it to the history.</summary>
    public UndoSnapshot Snapshot(SongProject project) => new(_encoder.Encode(project));

    /// <summary>
    /// Rebuilds the song held by <paramref name="snapshot"/>. <paramref name="live"/> is the song it replaces (the one just passed to
    /// <see cref="Snapshot"/>): its unchanged bars are moved into the result instead of being decoded, so it must be discarded.
    /// </summary>
    public SongProject Restore(UndoSnapshot snapshot, SongProject? live = null) => _encoder.Restore(snapshot.State, live);

    public UndoTransaction BeginTransaction(SongProject project) => new(this, Snapshot(project));

    public UndoCapture Capture(SongProject project) => Commit(BeginTransaction(project));

    public UndoCapture Commit(UndoTransaction transaction)
    {
        Validate(transaction);
        transaction.Completed = true;
        var stored = PushDistinct(_undo, transaction.Snapshot);
        if (stored) Clear(_redo);
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
        var stored = PushDistinct(_undo, snapshot);
        if (stored) Clear(_redo);
        Trim();
        return new UndoCapture(snapshot, stored);
    }

    public bool Discard(UndoCapture capture)
    {
        if (!capture.Stored || !_undo.TryPeek(out var top) ||
            !ReferenceEquals(top.State, capture.Snapshot.State)) return false;
        _undo.Pop();
        _historyBytes -= top.State.OwnBytes;
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
    }

    private bool PushDistinct(Stack<UndoSnapshot> stack, UndoSnapshot snapshot)
    {
        if (snapshot.State.OwnBytes > _maxBytes) return false;
        if (stack.TryPeek(out var top) && top.State.ContentEquals(snapshot.State)) return false;

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
