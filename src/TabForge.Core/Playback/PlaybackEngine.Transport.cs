using TabForge.Models;

namespace TabForge.Playback;

// Owns: transport requests and state (pause/resume/seek).
// Does not own: timeline compilation or output ownership.
// Tests: PlaybackStartHoldTests.

/// <summary>Playback transport controls.</summary>
/// <remarks>Owns: transport requests and state. Does not own: timeline compilation or output ownership. Tests: TestPlaybackScheduleReuse and existing transport tests.</remarks>
public sealed partial class PlaybackEngine : IDisposable
{
    private readonly record struct PendingPlaybackSeek(int Bar, int Cell, double TargetMs, long RequestId);

    public void Pause()
    {
        if (SetPendingStartPaused(true)) return;
        if (!_running || _paused) return;
        lock (_gate)
        {
            _anchorMs += _clock.Elapsed.TotalMilliseconds * _clockRate;
            _clock.Reset();
            _paused = true;
            // Queue the reset before a concurrent Resume can make the scheduler enqueue rearm setup.
            PanicAsync();
        }
        _timer.Set(false);   // RT-10: paused is idle
    }

    public void Resume()
    {
        if (SetPendingStartPaused(false)) return;
        if (!_running || !_paused) return;
        _timer.Set(true);
        lock (_gate)
        {
            _clock.Restart();
            _paused = false;
        }
        RefreshChannelSetupFromLive();   // levels or sounds changed while paused play from the first note
        // Pause sent a device reset, which can clear controllers/bank state on some synths, so
        // restore program/volume/pan before the next note (on the scheduler thread).
        _rearm = true;
        if (Preferences.CountInOnSeek) RequestRecordCountIn();   // playing on from a pause counts in too
    }

    public void Stop()
    {
        var generation = ++_generation;
        Volatile.Write(ref _pendingStart, null);
        // A record count-in asked for but never taken must not leak into the next run's schedule.
        Interlocked.Exchange(ref _recordCountInPending, 0);
        _running = false;
        _paused = false;
        // The scheduler thread is not waited for: a stopped run neither starts notes nor reports positions (both check the generation, the
        // notes under the output lock the device reset below also takes), and it leaves its loop at its next check.
        _thread = null;
        PanicAsync();
        // Any thread: only the live song's reference is carried over (never dereferenced here).
        lock (_gate) Volatile.Write(ref _song, new SongState(null, LiveProject, null, Array.Empty<Guid>()));
        Volatile.Write(ref _dropped, null);
        lock (_gate)
        {
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            _arrangementRefreshRequested = false;
            _pendingSeek = null;
            _scheduleReuse = null;
            _seekRebuild.Cancel();
        }
        _activeMetronomeNotes.Clear();
        _clock.Reset();
        lock (_gate) { _anchorMs = 0; _startSettling = false; }
        _currentStreamMs = 0;
        _timer.Set(false);   // RT-10: stopped is idle
    }

    /// <summary>
    /// Moves playback to a bar and cell, keeping the play or pause state. A seek the playing schedule can reuse moves at once. Any other seek
    /// returns at once as well: the playhead shows the target, the sounding notes are silenced, and the timeline is rebuilt in the background
    /// (see <see cref="BeginSeekRebuild"/>); the scheduler resumes at the target when it is ready.
    /// </summary>
    public void Seek(SongProject project, int bar, int cell) => Seek(project, bar, cell, null);

    /// <summary>
    /// Moves playback to the <paramref name="occurrence"/>-th performance of a bar (1-based; null/1 is the unchanged start).
    /// A pass-aware seek outside a loop always compiles a fresh schedule because it changes the performed order itself.
    /// </summary>
    public void Seek(SongProject project, int bar, int cell, int? occurrence)
    {
        if (ReseekPendingStart(project, Math.Max(0, bar), Math.Max(0, cell), occurrence)) return;
        var opts = _options;
        if (opts is null) return;
        var targetBar = Math.Max(0, bar);
        var targetCell = Math.Max(0, cell);
        opts.StartBar = targetBar;
        opts.StartCell = targetCell;
        // T1: a loop compiles whole passes and enters by bar/cell, so a pass-aware occurrence does not apply there.
        opts.StartOccurrence = occurrence is > 1 && !opts.Loop ? occurrence : null;
        bool wasPaused;
        bool candidate;
        PlaybackScheduleReuse? reuse;
        ScoreTimeline? timeline;
        lock (_gate)
        {
            wasPaused = _paused;
            reuse = _scheduleReuse;
            var song = Volatile.Read(ref _song);
            timeline = song?.Timeline;
            candidate = _running && ReferenceEquals(project, song?.Project) && !_arrangementRefreshRequested &&
                _pendingArrangementRefresh is null && _pendingLoopRefresh is null && reuse is not null && timeline is not null;
        }
        var targetMs = 0d;
        var canReuse = candidate && reuse!.TryResolve(project, opts.Clone(), timeline!, targetBar, targetCell, out targetMs);
        var queued = false;
        lock (_gate)
        {
            var current = Volatile.Read(ref _song);
            if (canReuse && _running && ReferenceEquals(_options, opts) && ReferenceEquals(project, current?.Project) && ReferenceEquals(_scheduleReuse, reuse) &&
                ReferenceEquals(current?.Timeline, timeline) && !_arrangementRefreshRequested &&
                _pendingArrangementRefresh is null && _pendingLoopRefresh is null)
            {
                _pendingSeek = new PendingPlaybackSeek(targetBar, targetCell, targetMs, ++_seekRequestId);
                _anchorMs = targetMs;
                _currentStreamMs = targetMs;
                _sentThroughMs = targetMs;
                _startMs = targetMs;
                CurrentBar = targetBar;
                CurrentCell = targetCell;
                _clock.Reset(); // the scheduler restarts this target clock after the device reset is complete
                _seekRebuild.Cancel();
                queued = true;
            }
        }
        if (queued) return;
        // A stopped engine has no scheduler to take a rebuilt timeline, so it starts at the target as before.
        if (_running) BeginSeekRebuild(project, opts);
        else RestartKeepingState(project, opts, wasPaused);
    }

    /// <summary>
    /// Starts a seek the playing schedule cannot reuse, on the owner (UI) thread: the playhead moves to the target at once, the sounding notes are
    /// silenced, and the timeline compiles in the background from a copy of the song. The scheduler holds the seek until that compile is ready.
    /// </summary>
    private void BeginSeekRebuild(SongProject project, PlaybackOptions opts)
    {
        opts.CountIn = false; // seeks mid-song never replay the count-in
        PlaybackSeekRebuild.KeepStartInsideLoop(project, opts);
        var snapshot = _seekRebuild.TakeSnapshot(project);
        var bar = opts.StartBar;
        var cell = opts.StartCell;
        var options = opts.Clone();
        lock (_gate)
        {
            var requestId = ++_seekRequestId;
            var targetMs = PlaybackSeekRebuild.ProvisionalMs(_timeline, bar, cell, Volatile.Read(ref _currentStreamMs));
            _pendingSeek = new PendingPlaybackSeek(bar, cell, targetMs, requestId);
            _anchorMs = targetMs;
            _currentStreamMs = targetMs;
            _sentThroughMs = targetMs;
            _startMs = targetMs;
            CurrentBar = bar;
            CurrentCell = cell;
            _clock.Reset();
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            _arrangementRefreshRequested = false;
            _seekRebuild.Queue(requestId, project, snapshot, options);
        }
        PanicAsync();
    }

    /// <summary>The loop's start and end in the playing timeline's milliseconds while looping is on; null when it is off or has no usable span (read-only, for views).</summary>
    public (double StartMs, double EndMs)? ActiveLoopMs
    {
        get
        {
            var options = _options;
            var timeline = _timeline;
            if (options is not { Loop: true } || timeline is null) return null;
            var (start, end, available) = ComputeLoopBounds(timeline, options);
            return available ? (start, end) : null;
        }
    }

    /// <summary>Change the relative speed, preserving the current musical position.</summary>
    public void SetSpeed(SongProject project, double speed)
    {
        var clamped = Math.Clamp(speed, 0.25, 2.0);
        Speed = clamped;
        if (ChangePendingStart(project, options => options.Speed = clamped)) return;
        var opts = _options;
        if (opts is null || !_running || opts.Speed.Equals(clamped)) return; // unchanged: never restart
        var pos = Playhead();
        var occurrence = RestartOccurrence(project, opts, pos);   // before the start moves: it anchors the timeline match
        opts.Speed = clamped;
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        opts.StartOccurrence = occurrence;   // a speed restart resumes the same performance
        RestartKeepingState(project, opts, _paused);
    }

    private void RestartKeepingState(SongProject project, PlaybackOptions opts, bool wasPaused)
    {
        var onPosition = _onPosition ?? (_ => { });
        var onFinished = _onFinished ?? (() => { });
        opts.CountIn = false; // seeks / speed / option changes mid-song never replay the count-in
        Start(project, opts, onPosition, onFinished, startPaused: wasPaused);
    }


    public void SetLoop(bool loop)
    {
        if (ChangePendingStart(null, options => options.Loop = loop)) return;
        if (_options is not null) _options.Loop = loop;
        Interlocked.Increment(ref _loopRangeVersion);   // the running scheduler recomputes its loop bounds, so a loop turned on mid-song wraps
    }
}
