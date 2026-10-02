using TabForge.Playback;

namespace TabForge.Documents;

/// <summary>Playback engine and live playhead state owned by a single open document.</summary>
public sealed class DocumentPlaybackState : IDisposable
{
    private readonly object _pendingGate = new();
    private readonly SynchronizationContext? _ownerContext = SynchronizationContext.Current;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private PlaybackPosition? _pendingPosition;
    private bool _finishedPending;
    private bool _disposed;

    public DocumentPlaybackState() : this(new Audio.RoutedMidiOutput(new SharedMidiOutput(), Audio.AudioEngineClient.Instance)) { }

    private DocumentPlaybackState(Audio.RoutedMidiOutput output) : this(new PlaybackEngine(output)) => Routing = output;

    /// <summary>Sends plug-in tracks to the audio engine (null for test engines built without routing).</summary>
    public Audio.RoutedMidiOutput? Routing { get; }

    public DocumentPlaybackState(PlaybackEngine engine)
    {
        Engine = engine;
        Engine.TimelineChanged += OnEngineTimelineChanged;
        Engine.TimelineRevised += OnEngineTimelineRevised;
    }

    public PlaybackEngine Engine { get; }

    /// <summary>
    /// Song time for this document's audio clips (its playhead stream, mapped to performed song seconds). It belongs to the document, not to
    /// the window showing it: the engine's position callback reports into it, so a tab that moves to another window keeps its clock and
    /// the old window is not kept alive by that callback.
    /// </summary>
    public Audio.SongClock Clock { get; } = new(Audio.AudioEngineClient.Instance);

    public ScoreTimeline? Timeline { get; set; }
    public double PlayheadMs { get; set; }
    public int PlayheadBar { get; set; } = -1;
    public int PlayheadCell { get; set; }
    public double PlayheadFraction { get; set; }
    public bool IsPlayingVisual { get; set; }
    public int[]? PlaybackBarRemap { get; set; }
    public Dictionary<string, int[]> PlaybackBarMappingsBySnapshot { get; } = new(StringComparer.Ordinal);

    // ---- bar remap: which playing bar each score bar is, while edits move bars during playback (document state; the window only refreshes its views from it) ----

    /// <summary>A section moved while playing: composes the remap and follows the playhead bar. False when this document is not playing visually (nothing changed).</summary>
    public bool ApplySectionMove(int[] oldToNewBar)
    {
        if (!IsPlayingVisual) return false;
        var currentBar = PlayheadBar;
        PlaybackBarRemap = Services.SectionReorderService.ComposeBarRemap(PlaybackBarRemap ?? Enumerable.Range(0, oldToNewBar.Length).ToArray(), oldToNewBar);
        if (currentBar >= 0 && currentBar < oldToNewBar.Length) PlayheadBar = oldToNewBar[currentBar];
        return true;
    }

    /// <summary>Bars were inserted, deleted or moved while playing (<paramref name="barCount"/> bars now): composes the remap, follows the playhead bar.</summary>
    public bool ApplyStructureEdit(int[] oldToNewBar, int barCount)
    {
        if (!IsPlayingVisual) return false;
        var currentBar = PlayheadBar;
        PlaybackBarRemap = Services.SectionReorderService.ComposeBarRemapWithInsertions(PlaybackBarRemap ?? Enumerable.Range(0, oldToNewBar.Length).ToArray(), oldToNewBar, barCount);
        if (currentBar >= 0 && currentBar < oldToNewBar.Length && oldToNewBar[currentBar] >= 0) PlayheadBar = oldToNewBar[currentBar];
        return true;
    }

    /// <summary>The outcome of <see cref="RestoreBarMapping"/>: the remap now in force, the bar playback was in before, and whether the playhead bar moved.</summary>
    public sealed record RestoredBarMapping(int[] Remap, int PriorLiveBar, bool PlayheadMoved);

    /// <summary>
    /// Undo / redo while playing: the remap that belonged to the restored state (or the current one) becomes current, and the playhead bar follows it.
    /// <paramref name="engineBar"/> is the bar the engine is in now. Null when this document is not playing visually.
    /// </summary>
    public RestoredBarMapping? RestoreBarMapping(UndoSnapshot snapshot, int barCount, int engineBar)
    {
        if (!IsPlayingVisual) return null;
        var previous = PlaybackBarRemap ?? Enumerable.Range(0, barCount).ToArray();
        var restored = PlaybackBarMappingsBySnapshot.TryGetValue(snapshot.Fingerprint, out var saved) ? saved.ToArray() : previous.ToArray();
        var priorLiveBar = PlayheadBar;
        var sourceBar = engineBar;
        if (sourceBar < 0 || sourceBar >= restored.Length) sourceBar = Array.IndexOf(previous, PlayheadBar);
        if (sourceBar >= restored.Length)
        {
            var previousLength = restored.Length;
            Array.Resize(ref restored, sourceBar + 1);
            Array.Fill(restored, -1, previousLength, restored.Length - previousLength);
        }
        PlaybackBarRemap = restored;
        var moved = false;
        if (sourceBar >= 0 && sourceBar < restored.Length)
        {
            PlayheadBar = restored[sourceBar] >= 0 ? restored[sourceBar] : Math.Clamp(priorLiveBar, 0, Math.Max(0, barCount - 1));
            moved = true;
        }
        RememberBarMapping(snapshot);
        return new RestoredBarMapping(restored, Math.Clamp(priorLiveBar, 0, Math.Max(0, barCount - 1)), moved);
    }

    /// <summary>
    /// The engine compiled a new current-score timeline: saved edit-state mappings are re-based onto it and the live remap becomes the identity.
    /// Not playing visually: everything is cleared. Returns whether this document is playing visually.
    /// </summary>
    public bool RebaseBarMappings(int barCount, Func<UndoSnapshot> currentState)
    {
        if (!IsPlayingVisual)
        {
            PlaybackBarRemap = null;
            PlaybackBarMappingsBySnapshot.Clear();
            return false;
        }
        var previous = PlaybackBarRemap ?? Enumerable.Range(0, barCount).ToArray();
        foreach (var entry in PlaybackBarMappingsBySnapshot.ToArray())
            PlaybackBarMappingsBySnapshot[entry.Key] = Services.SectionReorderService.RebaseBarRemap(previous, entry.Value, barCount);
        PlaybackBarRemap = Enumerable.Range(0, barCount).ToArray();
        RememberBarMapping(currentState());
        return true;
    }

    /// <summary>While this document plays, remembers which playback bar each edit state maps to (undo / redo re-find the bar the player was in).</summary>
    public void RememberBarMapping(UndoSnapshot snapshot)
    {
        if (!IsPlayingVisual || PlaybackBarRemap is null) return;
        PlaybackBarMappingsBySnapshot[snapshot.Fingerprint] = PlaybackBarRemap.ToArray();
    }

    public event Action<ScoreTimeline>? TimelineChanged;
    public event Action<ScoreTimeline>? TimelineRevised;

    public void ReportPosition(PlaybackPosition position)
    {
        lock (_pendingGate)
        {
            _pendingPosition = new PlaybackPosition
            {
                Bar = position.Bar,
                Cell = position.Cell,
                BarFraction = position.BarFraction,
                ElapsedMs = position.ElapsedMs
            };
        }
    }

    public bool TryTakePendingPosition(out PlaybackPosition? position)
    {
        lock (_pendingGate)
        {
            position = _pendingPosition;
            _pendingPosition = null;
            return position is not null;
        }
    }

    public void MarkFinished()
    {
        lock (_pendingGate) _finishedPending = true;
    }

    public bool TakeFinished()
    {
        lock (_pendingGate)
        {
            var finished = _finishedPending;
            _finishedPending = false;
            return finished;
        }
    }

    public void ClearPending()
    {
        lock (_pendingGate)
        {
            _pendingPosition = null;
            _finishedPending = false;
        }
    }

    public void ClearPlaybackPosition()
    {
        ClearPending();
        PlayheadMs = 0;
        PlayheadBar = -1;
        PlayheadCell = 0;
        PlayheadFraction = 0;
        IsPlayingVisual = false;
        PlaybackBarRemap = null;
        PlaybackBarMappingsBySnapshot.Clear();
    }

    private void OnEngineTimelineChanged(ScoreTimeline timeline)
    {
        Timeline = timeline;
        PlayheadMs = timeline.PlayFromMs;
        PlayheadBar = -1;
        PlayheadCell = 0;
        PlayheadFraction = 0;
        TimelineChanged?.Invoke(timeline);
    }

    private void OnEngineTimelineRevised(ScoreTimeline timeline)
    {
        void ApplyRevision()
        {
            if (_disposed) return;
            Timeline = timeline;
            TimelineRevised?.Invoke(timeline);
        }

        if (_ownerContext is not null && Environment.CurrentManagedThreadId != _ownerThreadId)
            _ownerContext.Post(_ => ApplyRevision(), null);
        else
            ApplyRevision();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Engine.TimelineChanged -= OnEngineTimelineChanged;
        Engine.TimelineRevised -= OnEngineTimelineRevised;
        Engine.Dispose();
        ClearPlaybackPosition();
        Timeline = null;
        TimelineChanged = null;
        TimelineRevised = null;
    }
}
