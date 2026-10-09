using TabForge.Documents;
using TabForge.Playback;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>What the playback view needs from its window: the song on show and pure view updates (no document access, no engine access).</summary>
internal interface IPlaybackViewHost
{
    bool IsClosed { get; }
    /// <summary>The song on show.</summary>
    DocumentSession ActiveDocument { get; }
    /// <summary>The score's playback surface (allocation-free per-tick members).</summary>
    IScorePlayhead Score { get; }
    /// <summary>The track whose notes the score highlights while playing.</summary>
    int ScoreTrackIndex { get; }
    /// <summary>Points the score's highlight at <see cref="ScoreTrackIndex"/>.</summary>
    void SelectScoreTrack();
    bool OnUiThread { get; }
    void Post(Action work);

    /// <summary>Keeps the score in view for the playing bar.</summary>
    void FollowPlayheadBar(int bar);
    /// <summary>Selects the section the playing bar is in (-1: none).</summary>
    void ShowPlayingSection(int bar);
    void ShowArrangementPlayhead(int bar, double fraction, bool paused);
    /// <summary>True while a drum hit's glow fades on the instrument panel (it needs every frame).</summary>
    bool InstrumentAnimating { get; }
    bool FollowsFretboard { get; }
    void RefreshInstrument();
    void RefreshStatus();
    /// <summary>Moves the playhead overlay to the score's current playhead geometry.</summary>
    void ShowPlayheadGeometry();

    /// <summary>The compiled timeline of the song on show changed or was revised: the score and the window's timeline copy take it.</summary>
    void ShowTimeline(ScoreTimeline timeline, bool rebaseBarMappings);
    /// <summary>The shown song is not playing: the score, overlay, arrangement and transport display the stopped state.</summary>
    void ShowStopped();
    /// <summary>The shown song plays (or is paused): transport buttons and status show it.</summary>
    void ShowTransportRunning(bool paused);
    /// <summary>The shown song played to its end.</summary>
    void ShowPlaybackFinished();
    void HaltFollow();
    void ReattachFollow();
    void ResetFollowRow();
}

// Owns: one window's playback view: the display-synchronised tick that applies the playback position to score, arrangement,
//     instrument and status line, and the playback-event subscriptions.
// Does not own: playback timing and the audio engine; drawing inside each view.
// Tests: TestInteractions, TestClosedDocumentChainsReleased, TestWindowLifetime.
/// <summary>
/// One window's playback view: the display-synchronised tick that applies the newest playback position to the score, the arrangement, the instrument
/// and the status line, and the listening to the playback events (timeline changed or revised) of the song on show. Engine threads publish only into the
/// document state; this controller reads it on the UI thread, once per rendered frame. <see cref="Dispose"/> (once, when the window has really closed)
/// stops the tick and detaches from the song.
/// </summary>
internal sealed class PlaybackViewController : IDisposable
{
    private readonly IPlaybackViewHost _host;
    private readonly FrameTicker _ticker = new();
    private DocumentSession? _observed;
    private int _lastBar = -1;
    private int _lastCell = -1;
    private double _lastMs;
    private DateTime _lastEditorUpdate = DateTime.MinValue;
    private DateTime _lastInstrumentUpdate = DateTime.MinValue;
    private bool _disposed;

    public PlaybackViewController(IPlaybackViewHost host)
    {
        _host = host;
        _ticker.Tick += OnTick;
    }

    /// <summary>True while the per-frame tick is attached to rendering.</summary>
    public bool IsTicking => _ticker.IsEnabled;

    /// <summary>The song whose playback events this window listens to (null before the first and after the last).</summary>
    public DocumentSession? Observed => _observed;

    public void StartTick() { if (!_disposed) _ticker.Start(); }

    public void StopTick() => _ticker.Stop();

    /// <summary>Listens to the playback events of <paramref name="session"/> and to no other song's.</summary>
    public void Observe(DocumentSession session)
    {
        if (_disposed || ReferenceEquals(_observed, session)) return;
        Detach();
        _observed = session;
        session.Playback.TimelineChanged += OnTimelineChanged;
        session.Playback.TimelineRevised += OnTimelineRevised;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ticker.Stop();
        _ticker.Tick -= OnTick;
        Detach();
    }

    private void Detach()
    {
        if (_observed is not { } observed) return;
        observed.Playback.TimelineChanged -= OnTimelineChanged;
        observed.Playback.TimelineRevised -= OnTimelineRevised;
        _observed = null;
    }

    private void OnTick(object? sender, EventArgs e) => Apply();

    /// <summary>Receives the engine's compiled timeline (also after a seek or a speed change).</summary>
    private void OnTimelineChanged(ScoreTimeline timeline)
    {
        if (_disposed || _host.IsClosed || !ReferenceEquals(_observed, _host.ActiveDocument)) return;
        _host.ShowTimeline(timeline, rebaseBarMappings: true);
        var playback = _host.ActiveDocument.Playback;
        playback.PlayheadMs = timeline.PlayFromMs;
        playback.PlayheadBar = -1;                     // status shows the cursor until the first position tick
        playback.PlayheadCell = 0;
        _host.Score.Ms = timeline.PlayFromMs;
        _lastBar = -1;
        _lastCell = -1;
        _lastMs = timeline.PlayFromMs - 1;
    }

    /// <summary>Installs the live-reordered future timeline without moving the current playhead.</summary>
    private void OnTimelineRevised(ScoreTimeline timeline)
    {
        if (!_host.OnUiThread)
        {
            _host.Post(() => OnTimelineRevised(timeline));
            return;
        }
        if (_disposed || _host.IsClosed || !ReferenceEquals(_observed, _host.ActiveDocument)) return;
        _host.ShowTimeline(timeline, rebaseBarMappings: false);
    }

    /// <summary>Applies the newest playback position, doing only the work that actually changed.</summary>
    public void Apply()
    {
        var playback = _host.ActiveDocument.Playback;
        if (playback.TakeFinished())
        {
            Finish(playback);
            return;
        }
        if (!playback.TryTakePendingPosition(out var position) || position is null) return;
        var engine = playback.Engine;
        // Draw what is heard now: the live clock minus the output latency, not the scheduler's last report.
        if (engine.IsPlaying && !engine.IsPaused) position = engine.AudiblePlayhead();

        var remap = playback.PlaybackBarRemap;
        var bar = remap is not null && position.Bar >= 0 && position.Bar < remap.Length
            ? remap[position.Bar]
            : position.Bar;
        playback.PlayheadMs = position.ElapsedMs;
        playback.PlayheadBar = bar;
        playback.PlayheadCell = position.Cell;
        playback.PlayheadFraction = position.BarFraction;
        // Keep score following independent of score-cache repaint cadence. In particular, layout/zoom
        // and loop-wrap updates can change the follow target without changing a rendered note boundary.
        _host.FollowPlayheadBar(bar);

        var barChanged = bar != _lastBar || position.Cell != _lastCell;
        var now = DateTime.UtcNow;
        if (barChanged) _host.ShowPlayingSection(bar);

        // The arrangement playhead is a cheap overlay: update every tick.
        _host.ShowArrangementPlayhead(bar, position.BarFraction, engine.IsPaused);

        // The score page is the expensive one: repaint only when the beat changes, when a note
        // actually starts/ends (so the highlight is exactly as long as the note), or as a fallback
        // cadence. The fretboard follows at a lower cadence.
        var score = _host.Score;
        var noteBoundary = score.NeedsRepaint(_lastMs, position.ElapsedMs);
        if (barChanged || noteBoundary || (now - _lastEditorUpdate).TotalMilliseconds >= 250)
        {
            _lastEditorUpdate = now;
            score.Ms = position.ElapsedMs;
            score.Fraction = position.BarFraction;
            _host.SelectScoreTrack();
            score.SetPlayhead(bar, position.Cell);
        }
        else
        {
            score.Ms = position.ElapsedMs;
            score.Fraction = position.BarFraction;
        }

        // A drum hit starts at a note boundary; per-frame redraws run only while its glow is fading.
        if (_host.InstrumentAnimating || barChanged || noteBoundary || (now - _lastInstrumentUpdate).TotalMilliseconds >= 200)
        {
            _lastInstrumentUpdate = now;
            if (_host.FollowsFretboard) _host.RefreshInstrument();
        }
        if (barChanged) _host.RefreshStatus();
        _host.ShowPlayheadGeometry();
        _lastBar = bar;
        _lastCell = position.Cell;
        _lastMs = position.ElapsedMs;
    }

    private void Finish(DocumentPlaybackState playback)
    {
        playback.IsPlayingVisual = false;
        playback.PlayheadBar = -1;
        playback.PlaybackBarRemap = null;
        playback.PlaybackBarMappingsBySnapshot.Clear();
        _ticker.Stop();
        _host.ShowPlaybackFinished();
    }

    /// <summary>Shows the song just put on show: running (the tick follows it) or stopped.</summary>
    public void ShowActiveDocument()
    {
        var playback = _host.ActiveDocument.Playback;
        var engine = playback.Engine;
        if (!engine.IsPlaying || !playback.IsPlayingVisual)
        {
            _ticker.Stop();
            _host.HaltFollow();
            _host.ShowStopped();
            return;
        }

        var score = _host.Score;
        score.Bind(playback.Timeline, playback.PlaybackBarRemap, _host.ScoreTrackIndex);
        score.Active = true;
        _host.ResetFollowRow();
        _lastBar = -1;
        _lastCell = -1;
        _lastMs = double.NegativeInfinity;
        playback.ReportPosition(engine.Playhead());
        StartTick();
        _host.ReattachFollow();   // shown again while playing: following is armed again, even after a manual scroll
        Apply();
        _host.ShowArrangementPlayhead(Math.Max(0, playback.PlayheadBar), playback.PlayheadFraction, engine.IsPaused);
        _host.ShowTransportRunning(engine.IsPaused);
    }
}
