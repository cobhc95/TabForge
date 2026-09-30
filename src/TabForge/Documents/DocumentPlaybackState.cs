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
    public ScoreTimeline? Timeline { get; set; }
    public double PlayheadMs { get; set; }
    public int PlayheadBar { get; set; } = -1;
    public int PlayheadCell { get; set; }
    public double PlayheadFraction { get; set; }
    public bool IsPlayingVisual { get; set; }
    public int[]? PlaybackBarRemap { get; set; }
    public Dictionary<string, int[]> PlaybackBarMappingsBySnapshot { get; } = new(StringComparer.Ordinal);

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
