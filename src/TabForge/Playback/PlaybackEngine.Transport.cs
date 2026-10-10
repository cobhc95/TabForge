using TabForge.Models;

namespace TabForge.Playback;

/// <summary>Playback transport controls.</summary>
/// <remarks>Owns: transport requests and state. Does not own: timeline compilation or output ownership. Tests: TestPlaybackScheduleReuse and existing transport tests.</remarks>
public sealed partial class PlaybackEngine : IDisposable
{
    private readonly record struct PendingPlaybackSeek(int Bar, int Cell, double TargetMs, long RequestId);

    public void Pause()
    {
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
        if (!_running || !_paused) return;
        _timer.Set(true);
        lock (_gate)
        {
            _clock.Restart();
            _paused = false;
        }
        // Pause sent a device reset, which can clear controllers/bank state on some synths, so
        // restore program/volume/pan before the next note (on the scheduler thread).
        _rearm = true;
    }

    public void Stop()
    {
        var generation = ++_generation;
        _running = false;
        _paused = false;
        var thread = _thread;
        _thread = null;
        if (thread is not null && thread.IsAlive && thread != Thread.CurrentThread)
        {
            // Never block the caller (normally the UI thread) waiting for MIDI work.
            if (!thread.Join(150)) Task.Run(() => thread.Join(1500));
        }
        PanicAsync();
        _timeline = null;
        lock (_gate)
        {
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            _arrangementRefreshRequested = false;
            _pendingSeek = null;
            _scheduleReuse = null;
        }
        _activeMetronomeNotes.Clear();
        _clock.Reset();
        lock (_gate) { _anchorMs = 0; }
        _currentStreamMs = 0;
        _timer.Set(false);   // RT-10: stopped is idle
    }

    /// <summary>Rebuild the timeline from a new musical position, preserving play/pause state.</summary>
    public void Seek(SongProject project, int bar, int cell)
    {
        var opts = _options;
        if (opts is null) return;
        var targetBar = Math.Max(0, bar);
        var targetCell = Math.Max(0, cell);
        opts.StartBar = targetBar;
        opts.StartCell = targetCell;
        bool wasPaused;
        bool candidate;
        PlaybackScheduleReuse? reuse;
        ScoreTimeline? timeline;
        lock (_gate)
        {
            wasPaused = _paused;
            reuse = _scheduleReuse;
            timeline = _timeline;
            candidate = _running && ReferenceEquals(project, _project) && !_arrangementRefreshRequested &&
                _pendingArrangementRefresh is null && _pendingLoopRefresh is null && reuse is not null && timeline is not null;
        }
        var targetMs = 0d;
        var canReuse = candidate && reuse!.TryResolve(project, opts.Clone(), timeline!, targetBar, targetCell, out targetMs);
        var queued = false;
        lock (_gate)
        {
            if (canReuse && _running && ReferenceEquals(_options, opts) && ReferenceEquals(project, _project) && ReferenceEquals(_scheduleReuse, reuse) &&
                ReferenceEquals(_timeline, timeline) && !_arrangementRefreshRequested &&
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
                queued = true;
            }
        }
        if (!queued) RestartKeepingState(project, opts, wasPaused);
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
        var opts = _options;
        if (opts is null || !_running || opts.Speed.Equals(clamped)) return; // unchanged: never restart
        var pos = Playhead();
        opts.Speed = clamped;
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        RestartKeepingState(project, opts, _paused);
    }

    private void RestartKeepingState(SongProject project, PlaybackOptions opts, bool wasPaused)
    {
        var onPosition = _onPosition ?? (_ => { });
        var onFinished = _onFinished ?? (() => { });
        opts.CountIn = false; // seeks / speed / option changes mid-song never replay the count-in
        Start(project, opts, onPosition, onFinished, startPaused: wasPaused);
    }


    public void SetLoop(bool loop) { if (_options is not null) _options.Loop = loop; }
}
