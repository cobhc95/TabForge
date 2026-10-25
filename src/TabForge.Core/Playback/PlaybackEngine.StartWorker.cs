using TabForge.Models;

namespace TabForge.Playback;

// Owns: the start once its timeline is compiled (StartCompiled: engine state, clock, scheduler thread), and the start whose timeline compiles off the owner (UI) thread: the pending start (song copy, options, callbacks, paused flag), the
//     hand-back to the owner thread when the compile is ready, and the start-settling flag that holds the playhead until the scheduler anchors
//     the clock at the start.
// Does not own: the synchronous Start (PlaybackEngine.cs), the scheduler loop, seeks while playing (PlaybackSeekRebuild).
// Tests: PlaybackStartWorkerTests, PlaybackStartHoldTests.
// Threads: BeginStart, the pending-start edits (pause, resume, seek) and FinishStart run on the owner thread when it has a SynchronizationContext;
//     the compile runs on a thread-pool thread. Without a context (tests, tools) the start finishes on the pool thread.

/// <summary>A start that compiles on a worker: the transport reports playing at once and audio begins when the timeline is ready.</summary>
public sealed partial class PlaybackEngine
{
    /// <summary>One start waiting for its compile. The compile thread writes Timeline, LoopCompileStart and Failure before it hands the start back.</summary>
    private sealed class PendingStart
    {
        public PendingStart(int generation, SongProject project, SongSnapshot snapshot, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused)
        {
            Generation = generation;
            Project = project;
            Snapshot = snapshot;
            Options = options;
            OnPosition = onPosition;
            OnFinished = onFinished;
            StartPaused = startPaused;
        }

        public int Generation { get; }
        public SongProject Project { get; }
        public SongSnapshot Snapshot { get; }
        public PlaybackOptions Options { get; }
        public Action<PlaybackPosition> OnPosition { get; }
        public Action OnFinished { get; }
        public bool StartPaused { get; set; }
        public ScoreTimeline? Timeline { get; set; }
        public int LoopCompileStart { get; set; }
        public Exception? Failure { get; set; }
    }

    private PendingStart? _pendingStart;   // the start waiting for its compile; null when none; written on the owner thread, read on any
    /// <summary>How long a start's compile may take before the start fails cleanly instead of holding the transport in "playing" limbo.</summary>
    private const int StartCompileTimeoutMs = 30_000;
    // True from a start until the scheduler anchors the clock at the start position, and while a count-in plays before a section or a loop
    // start (PlayLoopCountIn): the audible playhead holds at the stream position. Written under _gate, read under it by AudiblePlayhead.
    private bool _startSettling;

    /// <summary>True while a start (or a record/loop count-in) still holds the clock before the music begins.</summary>
    internal bool StartSettling { get { lock (_gate) return _startSettling; } }

    /// <summary>
    /// Starts like <see cref="Start"/>, but returns at once: the engine reports playing (or paused) and holds the playhead at the start bar while
    /// the timeline compiles on a worker from a copy of the song; the scheduler starts when the compile is ready. A stop, or a newer start, drops it.
    /// </summary>
    public void BeginStart(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
    {
        Stop();
        var generation = ++_generation;
        var opts = options.Clone();
        PlaybackSeekRebuild.KeepStartInsideLoop(project, opts);
        if (opts.Loop) opts.StartOccurrence = null;   // T1 enters a loop by bar/cell, not by a pass
        CurrentBar = opts.StartBar;
        CurrentCell = opts.StartCell;
        var snapshot = _seekRebuild.TakeSnapshot(project);   // decoded on the worker, not here
        var pending = new PendingStart(generation, project, snapshot, opts, onPosition, onFinished, startPaused);
        Volatile.Write(ref _pendingStart, pending);
        if (!startPaused) _timer.Set(true);   // the 1 ms timer is raised now, so the scheduler's first sleeps are already fine-grained
        var owner = SynchronizationContext.Current;
        var compileOptions = opts.Clone();   // the pending options may change on the owner thread while this compiles
        _ = Task.Run(() =>
        {
            try
            {
                pending.Timeline = snapshot.Compile(compileOptions, out var loopCompileStart);
                pending.LoopCompileStart = loopCompileStart;
            }
            catch (Exception ex)
            {
                // The start is handed back with its failure: it ends as a finished playback and the trace names the cause.
                TabForge.Services.Trace.Error(TabForge.Services.Trace.Playback, "Start compile failed: " + ex.Message);
                pending.Failure = ex;
            }
            if (owner is null) FinishStart(pending);
            else owner.Post(static state => { var (engine, start) = ((PlaybackEngine, PendingStart))state!; engine.FinishStart(start); }, (this, pending));
        });
        // A compile that never comes back (a pathological song) must not hold the transport in "playing" with no sound:
        // drop this start only, so the UI unsticks and the next Play compiles again. A finished, superseded or stopped
        // start fails the generation or identity check and the timeout does nothing.
        _ = Task.Delay(StartCompileTimeoutMs).ContinueWith(_ =>
        {
            if (Volatile.Read(ref _generation) != generation) return;
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _pendingStart, null, pending), pending)) return;
            _timer.Set(false);
            TabForge.Services.Trace.Error(TabForge.Services.Trace.Playback, $"Start compile timed out after {StartCompileTimeoutMs} ms.");
            pending.OnFinished();
        });
    }

    /// <summary>While a start compiles, the playhead holds at its bar and cell; otherwise <paramref name="position"/> is returned as it is.</summary>
    private PlaybackPosition HeldForPendingStart(PlaybackPosition position)
    {
        if (Volatile.Read(ref _pendingStart) is null) return position;
        position.Bar = CurrentBar;
        position.Cell = CurrentCell;
        return position;
    }

    /// <summary>Owner thread: starts a compiled pending start, unless a stop or a newer start replaced it.</summary>
    private void FinishStart(PendingStart pending)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _pendingStart, null, pending), pending) || pending.Generation != Volatile.Read(ref _generation)) return;
        if (pending.Timeline is not { } timeline)
        {
            _timer.Set(false);
            pending.OnFinished();
            return;
        }
        StartCompiled(pending.Project, pending.Options, timeline, pending.Snapshot, pending.LoopCompileStart, pending.Generation, pending.OnPosition, pending.OnFinished, pending.StartPaused);
    }

    /// <summary>A pause or resume before the compile is ready: the start begins paused or playing. False when no start is pending.</summary>
    private bool SetPendingStartPaused(bool paused)
    {
        if (Volatile.Read(ref _pendingStart) is not { } pending) return false;
        pending.StartPaused = paused;
        _timer.Set(!paused);
        return true;
    }

    /// <summary>A seek before the compile is ready: the start is begun again at the new bar and cell (and pass occurrence). False when no start is pending.</summary>
    private bool ReseekPendingStart(SongProject project, int bar, int cell, int? occurrence) => ChangePendingStart(project, options =>
    {
        options.StartBar = bar;
        options.StartCell = cell;
        options.StartOccurrence = occurrence is > 1 && !options.Loop ? occurrence : null;
    }, forceRestart: true);

    /// <summary>
    /// An option change before the compile is ready (speed, metronome, count-in, loop): a change the compile does not see is applied to the
    /// pending options, any other begins the start again with them. False when no start is pending.
    /// </summary>
    private bool ChangePendingStart(SongProject? project, Action<PlaybackOptions> mutate, bool forceRestart = false)
    {
        if (Volatile.Read(ref _pendingStart) is not { } pending) return false;
        var options = pending.Options.Clone();
        mutate(options);
        if (!forceRestart && options.CompilesSameAs(pending.Options) && options.Loop == pending.Options.Loop &&
            options.LoopStartBar == pending.Options.LoopStartBar && options.LoopEndBar == pending.Options.LoopEndBar &&
            options.LoopStartCell == pending.Options.LoopStartCell && options.LoopEndCell == pending.Options.LoopEndCell)
        {
            mutate(pending.Options);
            return true;
        }
        BeginStart(project ?? pending.Project, options, pending.OnPosition, pending.OnFinished, pending.StartPaused);
        return true;
    }

    /// <summary>The start once its timeline is compiled (owner thread, or a pool thread without a context): the engine state, the clock and the scheduler thread.
    /// <paramref name="project"/> is the live song and is only carried through as an identity key (Publish, Capture): this method reads the song through <paramref name="snapshot"/> alone.</summary>
    private void StartCompiled(SongProject project, PlaybackOptions options, ScoreTimeline timeline, SongSnapshot snapshot, int loopCompileStart, int generation,
        Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused)
    {
        _options = options;
        SetMetronomeSettings(options.Metronome, options.MetronomeVolume, options.MetronomeAccentVolume,
            options.MetronomeClickVolume, options.MetronomeAccentNote, options.MetronomeClickNote,
            options.MetronomeSubdivision);
        _onPosition = onPosition;
        _onFinished = onFinished;
        Speed = options.Speed <= 0 ? 1.0 : options.Speed;

        // Playback timelines hold candidate clicks for every supported subdivision. The scheduler
        // gates/parameters them live, so changing these controls never restarts the transport.
        IScoreQuery score = snapshot;   // this runs on a pool thread when the owner has no context: only the copy is read here
        if (options.RespectMuteSolo)
        {
            Volatile.Write(ref _audible, score.AudibleMask(Volatile.Read(ref _liveSolo)));
        }
        else
        {
            Volatile.Write(ref _audible, null);
        }
        Publish(timeline, project, snapshot);
        ClearProgramPreviews();   // a start compiles the programs of the song as it is now
        _scheduleReuse = PlaybackScheduleReuse.Capture(project, snapshot, options, timeline);
        lock (_gate)
        {
            _pendingArrangementRefresh = null;
            _pendingLoopRefresh = null;
            _arrangementRefreshRequested = false;
        }
        CurrentBar = options.StartBar;
        CurrentCell = options.StartCell;

        if (timeline.Events.Count == 0 || timeline.TotalMs <= 0)
        {
            Stop();
            onFinished?.Invoke();
            return;
        }

        _timer.Set(!startPaused);   // RT-10: held while playing only
        // A start inside a loop is compiled from the loop start: its count-in plays first, then playback begins at the requested bar and cell.
        // A start inside a bar would leave the bar's skipped part as silence after the compiled count-in, so there too the count-in is played first
        // and the clock then begins exactly at the start position.
        var midBar = options.CountIn && timeline.PlayFromMs > timeline.CountInMs + 0.5;
        _startMs = score.LoopEntryMs(timeline, options, loopCompileStart,
            options.CountIn && loopCompileStart < 0 && !midBar ? 0 : timeline.PlayFromMs);
        _countInBeforeStart = options.CountIn && (loopCompileStart >= 0 || midBar);
        ApplyResume(timeline);
        _sentThroughMs = _startMs;
        lock (_gate)
        {
            _anchorMs = _startMs;
            _startSettling = true;
        }
        _currentStreamMs = _startMs;
        Volatile.Write(ref _lateNotesDropped, 0);
        _paused = startPaused;
        _running = true;
        _playbackStartWallMs = _diagnosticClock.Elapsed.TotalMilliseconds;
        if (startPaused) _clock.Reset();
        else _clock.Restart();

        _thread = new Thread(() => SchedulerLoop(timeline, generation, _startMs))
        {
            IsBackground = true,
            Name = "TabForge MIDI sequencer",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        TimelineChanged?.Invoke(timeline);
    }
}
