namespace TabForge.Models;

// Owns: one document's selection state (the white selection: score bar range with its origin and scope, selected clips and track rows, the focus target),
//   its snapshot kept with each undo step, the enums naming who changed it and which tracks it covers, and the loop region (SelectionModel.Loop.cs).
// Does not own: the views that apply selection changes, or the undo steps that hold the snapshots (SongDocument, DocumentEdits).
// Tests: SelectionModelTests.

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

/// <summary>What keyboard edit commands act on. Exactly one at a time; the last thing the user clicked sets it. The members are the keys of <c>EditRouting</c>'s table.
/// <see cref="Loop"/> = the loop region on the bar-number ruler (the bar commands act on its bars over every track).</summary>
public enum FocusTarget { None, ScoreSelection, Loop, Clips, TrackRow, BarHighlight }

/// <summary>Which tracks a bar selection covers: the editor's track (made in the score) or every track (a timeline drag).</summary>
public enum SelectionScope
{
    ThisTrack,
    AllTracks,
}

/// <summary>
/// The one selection state shared by the score editor and the arrangement timeline. The white selection (what edits act on): the score's
/// bar range (with optional beat-cell bounds), the selected clips and the track rows. The loop region is separate state (SelectionModel.Loop.cs):
/// only the ruler and a released score selection set it. Both views observe <see cref="Changed"/> and mirror it; every change is written here first.
/// </summary>
/// <remarks>
/// Re-entrancy: setting a value equal to the current one is a no-op, so a view echoing a change back
/// (applying it raises its own "selection changed") ends there. A different value set while observers are
/// being notified is stored and announced once more after the current round, never recursively; at most
/// <see cref="MaxRounds"/> rounds run, so two views that disagree can never ping-pong forever.
/// </remarks>
public sealed partial class SelectionModel
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

    /// <summary>
    /// Tracks the range covers. A score selection (origin <see cref="SelectionOrigin.Editor"/>) is <see cref="SelectionScope.ThisTrack"/>, a timeline
    /// selection (<see cref="SelectionOrigin.Timeline"/>) is <see cref="SelectionScope.AllTracks"/>; other origins keep the current scope unless one is
    /// given. A track switch clears a ThisTrack range and keeps an AllTracks one (S6).
    /// </summary>
    public SelectionScope Scope { get; private set; } = SelectionScope.AllTracks;

    /// <summary>The one track the range covers, or -1 when it covers every track.</summary>
    public int ScopeTrack => Scope == SelectionScope.AllTracks ? -1 : TrackIndex;

    /// <summary>Raised after every real change, with the origin of the (last) change.</summary>
    public event EventHandler<SelectionOrigin>? Changed;

    /// <summary>True while <see cref="Changed"/> observers run.</summary>
    public bool IsNotifying => _notifying;
    /// <summary>How many times <see cref="Changed"/> was raised (tests, diagnostics).</summary>
    public int ChangeCount { get; private set; }

    private readonly List<TimelineClipSelection> _clips = new();
    private readonly List<Guid> _rows = new();
    private bool _notifying;
    private bool _pending;
    private SelectionOrigin _pendingOrigin;

    public (int Start, int End)? BarRange => HasRange ? (StartBar, EndBar) : null;

    public bool Contains(int bar) => HasRange && bar >= StartBar && bar <= EndBar;

    /// <summary>Selects bars <paramref name="start"/>…<paramref name="end"/> (any order) on <paramref name="track"/>.</summary>
    public bool SetRange(int track, int start, int end, SelectionOrigin origin, int startCell = 0, int endCell = -1) =>
        SetRange(track, start, end, origin, startCell, endCell, null);

    /// <summary>As above with an explicit scope (null = from the origin).</summary>
    public bool SetRange(int track, int start, int end, SelectionOrigin origin, int startCell, int endCell, SelectionScope? scope)
    {
        var newScope = scope ?? origin switch
        {
            SelectionOrigin.Editor => SelectionScope.ThisTrack,
            SelectionOrigin.Timeline => SelectionScope.AllTracks,
            _ => Scope,
        };
        if (start < 0 && end < 0) return Clear(origin);
        if (end < start) { (start, end) = (end, start); (startCell, endCell) = (0, -1); }
        start = Math.Max(0, start);
        end = Math.Max(start, end);
        startCell = Math.Max(0, startCell);
        if (endCell < -1) endCell = -1;
        return Apply(track < 0 ? TrackIndex : track, true, start, end, startCell, endCell, origin, newScope);
    }

    public bool Clear(SelectionOrigin origin) => Apply(TrackIndex, false, -1, -1, 0, -1, origin);

    /// <summary>What keyboard edit commands act on now (set by the last click); see EditRouting.</summary>
    public FocusTarget Focus { get; private set; } = FocusTarget.None;

    /// <summary>The selected clips (click or Ctrl+click on the timeline), each with its track.</summary>
    public IReadOnlyList<TimelineClipSelection> Clips => _clips;

    /// <summary>The selected track rows (header multi-select), by track id.</summary>
    public IReadOnlyList<Guid> Rows => _rows;

    /// <summary>The track list's multi-selection over <see cref="Rows"/> (click, Shift range, Ctrl toggle; the tracks an action covers).</summary>
    public TrackSelection Tracks { get; }

    public SelectionModel() => Tracks = new TrackSelection(this);

    public int ClipCount => _clips.Count;
    public int TrackRowCount => _rows.Count;

    /// <summary>A bar range made in the score: the notes of one track (ThisTrack).</summary>
    public bool HasScoreSelection => HasRange && Scope == SelectionScope.ThisTrack;


    /// <summary>Runs <paramref name="edits"/> and announces their changes once, with the origin of the last one (a click that moves the track, the range and the focus is one change for the views).</summary>
    public void Batch(Action edits)
    {
        if (_notifying) { edits(); return; }
        _notifying = true;
        _pending = false;
        try { edits(); }
        finally { _notifying = false; }
        if (_pending) Notify(_pendingOrigin);
    }

    /// <summary>Sets the focus; raises <see cref="Changed"/> (with <paramref name="origin"/>) when it differs.</summary>
    public bool SetFocus(FocusTarget focus, SelectionOrigin origin = SelectionOrigin.Timeline)
    {
        if (!SetFocusQuietly(focus)) return false;
        Notify(origin);
        return true;
    }

    /// <summary>Replaces the selected clips; raises <see cref="Changed"/> (with <paramref name="origin"/>) when the set differs.</summary>
    public void SetClips(IEnumerable<TimelineClipSelection> clips, SelectionOrigin origin = SelectionOrigin.Timeline)
    {
        if (SetClipsQuietly(clips)) Notify(origin);
    }

    /// <summary>Replaces the selected track rows; raises <see cref="Changed"/> (with <paramref name="origin"/>) when the set differs.</summary>
    public void SetRows(IEnumerable<Guid> trackIds, SelectionOrigin origin = SelectionOrigin.Timeline)
    {
        if (SetRowsQuietly(trackIds)) Notify(origin);
    }

    private bool SetFocusQuietly(FocusTarget focus)
    {
        if (focus == Focus) return false;
        Focus = focus;
        return true;
    }

    private bool SetClipsQuietly(IEnumerable<TimelineClipSelection> clips)
    {
        var next = clips.ToList();
        if (next.Count == _clips.Count && next.Zip(_clips).All(p => p.First.Track.Id == p.Second.Track.Id && p.First.Clip.Id == p.Second.Clip.Id && ReferenceEquals(p.First.Clip, p.Second.Clip))) return false;
        _clips.Clear();
        _clips.AddRange(next);
        return true;
    }

    private bool SetRowsQuietly(IEnumerable<Guid> trackIds)
    {
        var next = trackIds.ToList();
        if (next.SequenceEqual(_rows)) return false;
        _rows.Clear();
        _rows.AddRange(next);
        return true;
    }

    /// <summary>What the selection is now, as ids that survive the song being replaced (stored with an undo step, S9).</summary>
    public SelectionSnapshot Snapshot() => new(TrackIndex, Scope, HasRange, StartBar, EndBar, StartCell, EndCell, Focus,
        _clips.Select(clip => (clip.Track.Id, clip.Clip.Id)).ToArray(), _rows.ToArray(), Tracks.Anchor);

    /// <summary>
    /// Shows <paramref name="snapshot"/> again in <paramref name="project"/> (undo or redo, S9): the range is kept inside <paramref name="barCount"/> bars
    /// and <paramref name="project"/>'s tracks, clips and rows the song no longer has are dropped, and a focus whose selection is gone becomes none.
    /// </summary>
    public void Restore(SelectionSnapshot snapshot, SongProject project, int barCount, SelectionOrigin origin = SelectionOrigin.Document)
    {
        // Clips, rows and focus are written quietly and announced once, so a restore that leaves the range as it was (a re-selected clip) still repaints the views.
        var changed = RebindQuietly(project, snapshot.Clips, snapshot.Rows);
        Tracks.SetAnchor(snapshot.Anchor, project);
        changed |= SetFocusQuietly(snapshot.Focus);
        var before = ChangeCount;
        Apply(snapshot.TrackIndex, snapshot.HasRange, snapshot.StartBar, snapshot.EndBar, snapshot.StartCell, snapshot.EndCell, origin, snapshot.Scope);
        ClampTo(barCount, project.Tracks.Count, origin);
        changed |= DropFocusIfEmpty();
        if (changed && ChangeCount == before) Notify(origin);
    }

    /// <summary>The song was replaced: the selected clips and rows are looked up again by id in <paramref name="project"/>, and the ones it no longer has are dropped.</summary>
    public void Rebind(SongProject project)
    {
        Tracks.SetAnchor(Tracks.Anchor, project);
        if (RebindQuietly(project, Snapshot().Clips, _rows.ToArray())) Notify(SelectionOrigin.Document);
    }

    private bool RebindQuietly(SongProject project, IReadOnlyList<(Guid Track, string Clip)> clips, IReadOnlyList<Guid> rows)
    {
        var resolved = clips.Select(id => TimelineClipSelection.Find(project, id.Track, id.Clip));
        var changed = SetClipsQuietly(resolved.OfType<TimelineClipSelection>());
        changed |= SetRowsQuietly(rows.Where(id => project.Tracks.Any(t => t.Id == id)));
        return DropFocusIfEmpty() | changed;
    }

    private bool DropFocusIfEmpty()
    {
        if (Focus == FocusTarget.Clips && _clips.Count == 0 || Focus == FocusTarget.TrackRow && _rows.Count == 0 ||
            Focus == FocusTarget.ScoreSelection && !HasScoreSelection || Focus == FocusTarget.Loop && !HasLoop)
        {
            Focus = FocusTarget.None;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The active track changed. A score (ThisTrack) range is cleared, never moved to the other track (S6); a timeline (AllTracks) range stays,
    /// with whole bars because cell bounds belong to one track's grid.
    /// </summary>
    public bool SetTrack(int track, SelectionOrigin origin = SelectionOrigin.Track)
    {
        if (track == TrackIndex) return false;
        return HasRange && Scope == SelectionScope.AllTracks
            ? Apply(track, true, StartBar, EndBar, 0, -1, origin)
            : Apply(track, false, -1, -1, 0, -1, origin);
    }

    /// <summary>
    /// Escape (S7), the white part: clears the next thing in order: clips, then the score selection, then track rows. The loop region is never touched
    /// (the Escape after the last white one clears it, see <see cref="EscapeNext"/>). Returns false when nothing white was left to clear. With the track
    /// list focused (the last click was a header) its selected rows go first.
    /// </summary>
    public bool ClearNext(SelectionOrigin origin = SelectionOrigin.Command)
    {
        if (Focus == FocusTarget.TrackRow && _rows.Count > 0) { _rows.Clear(); DropFocusIf(FocusTarget.TrackRow); Notify(SelectionOrigin.Timeline); return true; }
        if (_clips.Count > 0) { _clips.Clear(); DropFocusIf(FocusTarget.Clips); Notify(SelectionOrigin.Timeline); return true; }
        if (HasRange)
        {
            DropFocusIf(FocusTarget.ScoreSelection);
            return Clear(origin);
        }
        if (_rows.Count > 0) { _rows.Clear(); DropFocusIf(FocusTarget.TrackRow); Notify(SelectionOrigin.Timeline); return true; }
        return false;
    }

    private void DropFocusIf(FocusTarget target)
    {
        if (Focus == target) Focus = FocusTarget.None;
    }

    /// <summary>
    /// Bars were inserted, removed or reordered: <paramref name="oldToNew"/> maps each old bar to its new index
    /// (-1 = removed). The range follows its bars; a range whose bars are all gone is cleared.
    /// </summary>
    public bool Remap(int[] oldToNew, int barCount, SelectionOrigin origin = SelectionOrigin.Document)
    {
        var loopMoved = RemapLoop(oldToNew, barCount, origin);
        if (!HasRange) return loopMoved;
        var mapped = new List<int>();
        for (var bar = StartBar; bar <= EndBar && bar < oldToNew.Length; bar++)
            if (oldToNew[bar] >= 0) mapped.Add(oldToNew[bar]);
        if (mapped.Count == 0 || barCount <= 0) return Clear(origin) | loopMoved;
        var start = Math.Clamp(mapped.Min(), 0, barCount - 1);
        var end = Math.Clamp(mapped.Max(), start, barCount - 1);
        var keepCells = start == oldToNew.ElementAtOrDefault(StartBar) && end == oldToNew.ElementAtOrDefault(EndBar);
        return Apply(TrackIndex, true, start, end, keepCells ? StartCell : 0, keepCells ? EndCell : -1, origin) | loopMoved;
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
        ClampLoopTo(barCount, origin);
        if (!HasRange || barCount <= 0 || StartBar >= barCount) return Apply(track, false, -1, -1, 0, -1, origin);
        var end = Math.Min(EndBar, barCount - 1);
        return Apply(track, true, StartBar, end, StartCell, end == EndBar ? EndCell : -1, origin);
    }

    private bool Apply(int track, bool hasRange, int start, int end, int startCell, int endCell, SelectionOrigin origin, SelectionScope? scope = null)
    {
        if (!hasRange) { start = end = -1; startCell = 0; endCell = -1; }
        var newScope = scope ?? Scope;
        if (track == TrackIndex && hasRange == HasRange && start == StartBar && end == EndBar &&
            startCell == StartCell && endCell == EndCell && newScope == Scope)
            return false;
        Scope = newScope;
        TrackIndex = track;
        HasRange = hasRange;
        StartBar = start;
        EndBar = end;
        StartCell = startCell;
        EndCell = endCell;
        Notify(origin);
        return true;
    }

    // Announces a change to the observers: never recursively (a change made while they run is announced once more after the round).
    private void Notify(SelectionOrigin origin)
    {
        if (_notifying)
        {
            _pending = true;
            _pendingOrigin = origin;
            return;
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
    }
}

/// <summary>A selection stored with an undo step: the range, the focus, the clips and track rows by id and the track rows' Shift-range anchor.</summary>
public sealed record SelectionSnapshot(int TrackIndex, SelectionScope Scope, bool HasRange, int StartBar, int EndBar, int StartCell, int EndCell,
    FocusTarget Focus, IReadOnlyList<(Guid Track, string Clip)> Clips, IReadOnlyList<Guid> Rows, Guid? Anchor = null);
