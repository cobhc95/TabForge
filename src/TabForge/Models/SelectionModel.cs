namespace TabForge.Models;

/// <summary>Who changed the shared selection. A view skips applying a change it made itself.</summary>
public enum SelectionOrigin
{
    /// <summary>The score/tab editor (mouse, Shift+arrows, Ctrl+A, Esc inside the editor).</summary>
    Editor,
    /// <summary>The arrangement timeline (range drag, plain click on empty space).</summary>
    Timeline,
    /// <summary>A menu or keyboard command in the window (area paste/move/delete, loop section, Esc).</summary>
    Command,
    /// <summary>The active track changed (track list, timeline lane click).</summary>
    Track,
    /// <summary>A tab/song switch, undo/redo or a structural edit that moved or removed bars.</summary>
    Document,
}

/// <summary>
/// The one selection state shared by the score editor and the arrangement timeline: the selected bar range
/// (with optional beat-cell bounds from the score), the track it belongs to, or no range. Both views observe
/// <see cref="Changed"/> and mirror it; every selection change in either view is written here first. The
/// selected range is also the timeline's "selected area" (the loop area), so it has no copy of its own.
/// </summary>
/// <remarks>
/// Re-entrancy: setting a value equal to the current one is a no-op, so a view echoing a change back
/// (applying it raises its own "selection changed") ends there. A different value set while observers are
/// being notified is stored and announced once more after the current round, never recursively; at most
/// <see cref="MaxRounds"/> rounds run, so two views that disagree can never ping-pong forever.
/// </remarks>
public sealed class SelectionModel
{
    public const int MaxRounds = 4;

    /// <summary>Track the range was made on (the active track). -1 before any track exists.</summary>
    public int TrackIndex { get; private set; } = -1;
    public bool HasRange { get; private set; }
    public int StartBar { get; private set; } = -1;
    public int EndBar { get; private set; } = -1;
    /// <summary>First score cell inside <see cref="StartBar"/> (0 = bar start).</summary>
    public int StartCell { get; private set; }
    /// <summary>Last score cell inside <see cref="EndBar"/>; -1 = the whole last bar.</summary>
    public int EndCell { get; private set; } = -1;

    /// <summary>Raised after every real change, with the origin of the (last) change.</summary>
    public event EventHandler<SelectionOrigin>? Changed;

    /// <summary>True while <see cref="Changed"/> observers run.</summary>
    public bool IsNotifying => _notifying;
    /// <summary>How many times <see cref="Changed"/> was raised (tests, diagnostics).</summary>
    public int ChangeCount { get; private set; }

    private bool _notifying;
    private bool _pending;
    private SelectionOrigin _pendingOrigin;

    public (int Start, int End)? BarRange => HasRange ? (StartBar, EndBar) : null;

    public bool Contains(int bar) => HasRange && bar >= StartBar && bar <= EndBar;

    /// <summary>Selects bars <paramref name="start"/>…<paramref name="end"/> (any order) on <paramref name="track"/>.</summary>
    public bool SetRange(int track, int start, int end, SelectionOrigin origin, int startCell = 0, int endCell = -1)
    {
        if (start < 0 && end < 0) return Clear(origin);
        if (end < start) { (start, end) = (end, start); (startCell, endCell) = (0, -1); }
        start = Math.Max(0, start);
        end = Math.Max(start, end);
        startCell = Math.Max(0, startCell);
        if (endCell < -1) endCell = -1;
        return Apply(track < 0 ? TrackIndex : track, true, start, end, startCell, endCell, origin);
    }

    public bool Clear(SelectionOrigin origin) => Apply(TrackIndex, false, -1, -1, 0, -1, origin);

    /// <summary>
    /// The active track changed. The bar range stays (bars are shared by every track) but cell bounds belong
    /// to one track's grid, so a range moved to another track covers its bars whole.
    /// </summary>
    public bool SetTrack(int track, SelectionOrigin origin = SelectionOrigin.Track)
    {
        if (track == TrackIndex) return false;
        return HasRange
            ? Apply(track, true, StartBar, EndBar, 0, -1, origin)
            : Apply(track, false, -1, -1, 0, -1, origin);
    }

    /// <summary>
    /// Bars were inserted, removed or reordered: <paramref name="oldToNew"/> maps each old bar to its new index
    /// (-1 = removed). The range follows its bars; a range whose bars are all gone is cleared.
    /// </summary>
    public bool Remap(int[] oldToNew, int barCount, SelectionOrigin origin = SelectionOrigin.Document)
    {
        if (!HasRange) return false;
        var mapped = new List<int>();
        for (var bar = StartBar; bar <= EndBar && bar < oldToNew.Length; bar++)
            if (oldToNew[bar] >= 0) mapped.Add(oldToNew[bar]);
        if (mapped.Count == 0 || barCount <= 0) return Clear(origin);
        var start = Math.Clamp(mapped.Min(), 0, barCount - 1);
        var end = Math.Clamp(mapped.Max(), start, barCount - 1);
        var keepCells = start == oldToNew.ElementAtOrDefault(StartBar) && end == oldToNew.ElementAtOrDefault(EndBar);
        return Apply(TrackIndex, true, start, end, keepCells ? StartCell : 0, keepCells ? EndCell : -1, origin);
    }

    /// <summary>Old→new bar map for <paramref name="count"/> bars inserted before bar <paramref name="at"/>.</summary>
    public static int[] InsertMap(int oldBarCount, int at, int count = 1) =>
        Enumerable.Range(0, Math.Max(0, oldBarCount)).Select(bar => bar < at ? bar : bar + count).ToArray();

    /// <summary>Old→new bar map for bars <paramref name="start"/>…<paramref name="end"/> removed (-1 = removed).</summary>
    public static int[] RemoveMap(int oldBarCount, int start, int end) =>
        Enumerable.Range(0, Math.Max(0, oldBarCount))
            .Select(bar => bar < start ? bar : bar <= end ? -1 : bar - (end - start + 1)).ToArray();

    /// <summary>Keeps the range inside a song of <paramref name="barCount"/> bars (undo/redo, loads); clears it when empty.</summary>
    public bool ClampTo(int barCount, int trackCount, SelectionOrigin origin = SelectionOrigin.Document)
    {
        var track = trackCount <= 0 ? -1 : Math.Clamp(TrackIndex, 0, trackCount - 1);
        if (!HasRange || barCount <= 0 || StartBar >= barCount) return Apply(track, false, -1, -1, 0, -1, origin);
        var end = Math.Min(EndBar, barCount - 1);
        return Apply(track, true, StartBar, end, StartCell, end == EndBar ? EndCell : -1, origin);
    }

    private bool Apply(int track, bool hasRange, int start, int end, int startCell, int endCell, SelectionOrigin origin)
    {
        if (!hasRange) { start = end = -1; startCell = 0; endCell = -1; }
        if (track == TrackIndex && hasRange == HasRange && start == StartBar && end == EndBar &&
            startCell == StartCell && endCell == EndCell)
            return false;
        TrackIndex = track;
        HasRange = hasRange;
        StartBar = start;
        EndBar = end;
        StartCell = startCell;
        EndCell = endCell;
        if (_notifying)
        {
            // Never notify recursively: announce once more after the current round.
            _pending = true;
            _pendingOrigin = origin;
            return true;
        }
        _notifying = true;
        try
        {
            var rounds = 0;
            var current = origin;
            do
            {
                _pending = false;
                ChangeCount++;
                Changed?.Invoke(this, current);
                current = _pendingOrigin;
            } while (_pending && ++rounds < MaxRounds);
            _pending = false;
        }
        finally { _notifying = false; }
        return true;
    }
}
