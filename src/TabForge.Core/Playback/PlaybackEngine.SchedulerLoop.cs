using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the scheduler thread's loop body, split into one step per concern (seek rebuild, seek, arrangement swap, loop wrap, count-ins, skips, resync, finish).
// Does not own: the helpers those steps call (PlaybackEngine.Scheduler.cs) or the output port (PlaybackEngine.Scheduler.Output.cs).
// Tests: TestNoHangingNotes. Realtime: the steps allocate nothing and take no lock the original loop did not take (the arrangement swap checks its request flag first and locks only when one is pending).

public sealed partial class PlaybackEngine
{
    /// <summary>What a loop step tells the scheduler loop to do next.</summary>
    private enum LoopStep { Proceed, Continue, Break }

    /// <summary>The scheduler loop's working state, passed by reference to the steps so they share it without allocating.</summary>
    private struct SchedulerState
    {
        public ScoreTimeline Timeline;
        public List<ScoreEvent> Events;
        public PlaybackOptions Opts;
        public double LoopStartMs, LoopEndMs;
        public bool LoopAvailable;
        public int LoopRangeVersion;
        public double StartMs, Ms;
        public int Index;
        public List<double>? SectionMs;
        public int NextSection;
        public double LastCountInMs, LastReportMs, LastDispatchMs;
    }

    private void SchedulerLoop(ScoreTimeline timeline, int generation, double startMs)
    {
        var s = BeginSchedulerRun(timeline, generation, startMs);
        while (_running && generation == Volatile.Read(ref _generation))
        {
            var step = StepRebuiltSeek(ref s, generation);
            if (step != LoopStep.Proceed) { if (step == LoopStep.Break) break; continue; }

            if (!TryReadClock(generation, out s.Ms, out var seek)) break;   // stopped: the next start has its own seek
            if (seek is { } seekRequest && StepSeek(ref s, generation, seekRequest) == LoopStep.Continue) continue;

            RefreshLoopBoundsIfChanged(ref s);
            if (StepArrangementSwap(ref s) == LoopStep.Continue) continue;
            if (_paused) { StepPaused(ref s, generation); continue; }
            StepRearm(ref s, generation);

            var loopEnabled = s.LoopAvailable && s.Opts.Loop;
            if (loopEnabled && s.Ms >= s.LoopEndMs)
            {
                step = StepLoopWrap(ref s, generation);
                if (step != LoopStep.Proceed) { if (step == LoopStep.Break) break; continue; }
            }
            if (StepRecordCountIn(ref s, generation) == LoopStep.Continue) continue;
            if (StepSectionCountIn(ref s, generation) == LoopStep.Continue) continue;
            if (StepSkips(ref s, generation, out var skips) == LoopStep.Continue) continue;
            ResyncAfterStall(ref s, generation);

            var horizon = DispatchHorizon(s.Ms, loopEnabled, s.LoopEndMs, s.SectionMs, s.NextSection, skips);
            DispatchDue(s.Timeline, s.Events, ref s.Index, horizon, s.Ms, s.StartMs, generation);
            s.LastDispatchMs = s.Ms;
            Volatile.Write(ref _sentThroughMs, s.Index > 0 ? Math.Max(s.Ms, s.Events[s.Index - 1].TimeMs) : s.Ms);

            if (!loopEnabled && s.Ms >= s.Timeline.TotalMs)
            {
                step = FinishRun(ref s, generation, s.Ms);
                if (step != LoopStep.Proceed) { if (step == LoopStep.Break) break; continue; }
            }

            if (s.Ms - s.LastReportMs >= PositionIntervalMs)
            {
                s.LastReportMs = s.Ms;
                ReportPosition(s.Ms, s.Timeline, generation);
            }

            var nextMs = s.Index < s.Events.Count ? s.Events[s.Index].TimeMs : double.MaxValue;
            if (loopEnabled) nextMs = Math.Min(nextMs, s.LoopEndMs + DispatchLeadMs); // wake exactly at the wrap
            SleepUntil(s.Ms, nextMs, _clockRate);
        }
        var dropped = Volatile.Read(ref _lateNotesDropped);
        if (dropped > 0) Services.Trace.Error(Services.Trace.Playback, $"Late note-ons skipped this playthrough: {dropped} (the scheduler fell more than {MaxLateMs} ms behind).");
    }

    /// <summary>Resets the loop counters and the clock, sets the device up, plays any count-in and finds the first event to send.</summary>
    private SchedulerState BeginSchedulerRun(ScoreTimeline timeline, int generation, double startMs)
    {
        var s = new SchedulerState { Timeline = timeline, Events = timeline.Events, Opts = _options!, StartMs = startMs };
        var opts = s.Opts;
        (s.LoopStartMs, s.LoopEndMs, s.LoopAvailable) = ComputeLoopBounds(timeline, opts);
        s.LoopRangeVersion = Volatile.Read(ref _loopRangeVersion);
        _loopsDone = 0;
        LoopCompleted?.Invoke(this, 0);
        lock (_gate)
        {
            // Re-anchor so a trainer rate change never jumps the position.
            _anchorMs += _clock.Elapsed.TotalMilliseconds * _clockRate;
            _clock.Restart();
            _clockRate = opts.Loop ? TrainerRate(0) : 1.0;
        }

        // Start clean: kill anything left from a previous run, then restore deterministic channel state.
        ResetAndSetup(timeline, generation);
        if (_countInBeforeStart)
        {
            // Count-in before a start inside a loop: the clock anchors at the start position only after these bars.
            _countInBeforeStart = false;
            for (var bar = 0; bar < Math.Clamp(opts.CountInBars, 1, 4) && _running && generation == Volatile.Read(ref _generation); bar++)
                PlayLoopCountIn(timeline, startMs, generation);
        }

        // The reset above waits for every queued device reset (Stop() of the previous run queues one
        // per jump while playing, and a driver can take hundreds of ms to flush sounding notes). The
        // clock started in Start(), so without this the position is already past the target when the
        // loop begins and the first note(s) at the target read as stale (> MaxLateMs late) and are dropped.
        lock (_gate)
        {
            if (!_paused && generation == Volatile.Read(ref _generation))
            {
                _anchorMs = startMs;
                _clock.Restart();
            }
            if (generation == Volatile.Read(ref _generation)) _startSettling = false;
        }

        s.Index = FirstIndexToPlay(s.Events, startMs);
        s.LastCountInMs = startMs;
        s.LastReportMs = double.NegativeInfinity;
        s.LastDispatchMs = startMs;
        return s;
    }

    /// <summary>Takes a finished seek rebuild, if one is compiling: Continue while waiting, Break when it failed, Proceed with the new timeline swapped in (or none pending).</summary>
    private LoopStep StepRebuiltSeek(ref SchedulerState s, int generation)
    {
        if (!_seekRebuild.IsPending) return LoopStep.Proceed;
        // A seek the schedule could not reuse is compiling: nothing plays until its timeline is swapped in, then the seek branch resumes there.
        var step = TakeRebuiltSeek(generation, out var rebuilt, out var resumeMs);
        if (step == RebuildStep.Waiting)
        {
            Thread.Sleep(1);
            return LoopStep.Continue;
        }
        if (step == RebuildStep.Failed)
        {
            _timer.Set(false);
            PanicAsync();
            _onFinished?.Invoke();
            return LoopStep.Break;
        }
        s.Timeline = rebuilt!;
        s.Events = s.Timeline.Events;
        // The dispatch position belongs to the old list: it is found again in the new one, so no note is skipped and nothing is read
        // past its end even when no seek is pending any more (the seek branch below re-finds it as well when one is).
        s.StartMs = resumeMs;
        s.Index = FirstIndexAtOrAfter(s.Events, resumeMs);
        s.LastDispatchMs = resumeMs;
        s.LastCountInMs = resumeMs;
        s.LastReportMs = double.NegativeInfinity;
        s.SectionMs = null;
        (s.LoopStartMs, s.LoopEndMs, s.LoopAvailable) = ComputeLoopBounds(s.Timeline, s.Opts);
        s.LoopRangeVersion = Volatile.Read(ref _loopRangeVersion);
        TimelineRevised?.Invoke(s.Timeline);
        return LoopStep.Proceed;
    }

    /// <summary>Applies a pending seek: moves the dispatch position to the target and restores the device state there; Continue when the run ended or the seek was not accepted.</summary>
    private LoopStep StepSeek(ref SchedulerState s, int generation, PendingPlaybackSeek seekRequest)
    {
        s.StartMs = seekRequest.TargetMs;
        s.Index = FirstIndexAtOrAfter(s.Events, s.StartMs);
        s.LastDispatchMs = s.StartMs;
        s.LastCountInMs = s.StartMs;
        s.LastReportMs = double.NegativeInfinity;
        s.SectionMs = null;
        s.NextSection = 0;
        _rearm = false;
        ResetAndSetup(s.Timeline, generation);
        if (!_running || generation != Volatile.Read(ref _generation)) return LoopStep.Continue;
        if (!CanScheduleWork(generation)) return LoopStep.Continue;
        if (Preferences.CountInOnSeek) RequestRecordCountIn();   // a click on the timeline counts in at the clicked spot
        RestoreChannelStateAt(s.Timeline, s.Timeline.PlayFromMs, s.StartMs);
        var seekPosition = CommitPendingSeekPosition(generation, s.Timeline, seekRequest, s.StartMs);
        if (seekPosition is not { } acceptedPosition) return LoopStep.Continue;
        _onPosition?.Invoke(acceptedPosition);
        s.Ms = s.StartMs;
        return LoopStep.Proceed;
    }

    private void RefreshLoopBoundsIfChanged(ref SchedulerState s)
    {
        var currentLoopVersion = Volatile.Read(ref _loopRangeVersion);
        if (currentLoopVersion == s.LoopRangeVersion) return;
        s.LoopRangeVersion = currentLoopVersion;
        (s.LoopStartMs, s.LoopEndMs, s.LoopAvailable) = ComputeLoopBounds(s.Timeline, s.Opts);
    }

    /// <summary>Swaps in a recompiled arrangement once its boundary is reached; Continue while the compile is awaited.</summary>
    private LoopStep StepArrangementSwap(ref SchedulerState s)
    {
        var waitForArrangement = false;
        var timelineRevised = false;
        if (!Volatile.Read(ref _arrangementRefreshRequested)) return LoopStep.Proceed;   // no refresh asked: skip the lock (a request made this instant is seen next tick)
        lock (_gate)
        {
            if (_arrangementRefreshRequested && s.Ms + 0.001 >= _arrangementRefreshBoundaryMs)
            {
                if (_pendingArrangementRefresh is not { } refresh)
                {
                    if (_arrangementRefreshLive)
                    {
                        // A live edit has no compile ready: the old schedule plays on (nothing dropped, nothing late) and the edit is taken a bar later.
                        _arrangementRefreshRequested = false;
                        _refreshSeq++;
                        _pendingLoopRefresh = null;
                        Volatile.Write(ref _liveRefreshMissed, 1);
                    }
                    else waitForArrangement = true;
                }
                else if (!_paused && s.Opts.CountIn && _loopsDone == 0 && s.Timeline.CountInMs > 0 && s.Ms < s.Timeline.CountInMs)
                {
                    // The count-in of the start still clicks (song time is still inside its span): hold the swap until it is
                    // done instead of cutting the clicks, as a tempo or time-signature edit landing mid-count-in would.
                    waitForArrangement = true;
                }
                else
                {
                    s.Timeline = refresh.Timeline;
                    Publish(s.Timeline, refresh.Project, refresh.Snapshot);
                    _scheduleReuse = null;
                    s.Events = s.Timeline.Events;
                    s.Index = FirstIndexAtOrAfter(s.Events, refresh.BoundaryMs);
                    s.LastDispatchMs = s.Ms;
                    _pendingArrangementRefresh = null;
                    _arrangementRefreshRequested = false;
                    (s.LoopStartMs, s.LoopEndMs, s.LoopAvailable) = ComputeLoopBounds(s.Timeline, s.Opts);
                    timelineRevised = true;
                }
            }
        }
        if (waitForArrangement)
        {
            Thread.Sleep(1);
            return LoopStep.Continue;
        }
        if (timelineRevised) { TimelineRevised?.Invoke(s.Timeline); s.SectionMs = null; }
        return LoopStep.Proceed;
    }

    /// <summary>
    /// A paused transport retains its timeline position but must not dispatch upcoming notes.
    /// This also makes seeking while paused safe: the replacement scheduler starts paused.
    /// </summary>
    private void StepPaused(ref SchedulerState s, int generation)
    {
        if (s.Ms - s.LastReportMs >= PositionIntervalMs)
        {
            s.LastReportMs = s.Ms;
            ReportPosition(s.Ms, s.Timeline, generation);
        }
        Thread.Sleep((int)PositionIntervalMs);
    }

    private void StepRearm(ref SchedulerState s, int generation)
    {
        if (!_rearm) return;
        _rearm = false;
        RearmChannelSetup(s.Timeline, generation);
        // Then whatever changed since the start (a new program, volume or pan set in the song) as of now, so a
        // synth created mid-song sounds like the rest, not like its defaults.
        if (s.Ms > s.StartMs) RestoreChannelStateAt(s.Timeline, s.StartMs, s.Ms);
    }

    /// <summary>The loop end was reached: counts the loop, finishes after the last one, or wraps seamlessly (swapping in a recompiled song when an edit is waiting).</summary>
    private LoopStep StepLoopWrap(ref SchedulerState s, int generation)
    {
        bool boundaryAdmitted;
        lock (_gate)
            boundaryAdmitted = _running && generation == Volatile.Read(ref _generation) && _pendingSeek is null;
        if (!boundaryAdmitted) return LoopStep.Continue;
        var settings = Preferences.Loop;
        _loopsDone++;
        LoopCompleted?.Invoke(this, _loopsDone);
        // All requested loops played: finish at the loop end.
        if (settings.Count > 0 && _loopsDone >= settings.Count) return FinishRun(ref s, generation, s.LoopEndMs);

        var length = s.LoopEndMs - s.LoopStartMs;
        var overshoot = s.Ms - s.LoopEndMs;
        LoopRefresh? loopSwap;
        lock (_gate) { loopSwap = _pendingLoopRefresh is { } pendingLoop && pendingLoop.Seq == _refreshSeq ? pendingLoop : null; _pendingLoopRefresh = null; }   // a refresh newer than the compile has replaced it
        if (loopSwap is not null)
        {
            // An edit was made while looping: the whole song, recompiled, replaces the timeline at the wrap, so edited loop bars behind the playhead are heard.
            ReleaseForLoopWrap(s.Timeline);
            s.Timeline = loopSwap.Timeline;
            Publish(s.Timeline, loopSwap.Project, loopSwap.Snapshot);
            ClearProgramPreviews();   // the whole song, recompiled, carries the programs as they are now
            _scheduleReuse = null;
            s.Events = s.Timeline.Events;
            (s.LoopStartMs, s.LoopEndMs, s.LoopAvailable) = ComputeLoopBounds(s.Timeline, s.Opts);
            length = s.LoopEndMs - s.LoopStartMs;
            TimelineRevised?.Invoke(s.Timeline);
            s.SectionMs = null;
        }
        var wrapped = s.LoopStartMs + (length > 0 ? overshoot % length : 0);
        // Seamless wrap: release sounding notes and restore the controller state of the loop
        // start (a few messages) instead of a full device reset, so there is no gap.
        ReleaseForLoopWrap(s.Timeline);
        RestoreChannelStateAt(s.Timeline, s.StartMs, wrapped);
        if (settings.CountInEachLoop) PlayLoopCountIn(s.Timeline, s.LoopStartMs, generation);
        var nextLoopMs = settings.CountInEachLoop ? s.LoopStartMs : wrapped;
        if (!TrySetSchedulerAnchor(generation, nextLoopMs, TrainerRate(_loopsDone))) return LoopStep.Continue;
        s.Ms = nextLoopMs;
        // Keep events at the loop first instant in the dispatch window. If the scheduler
        // wakes a few milliseconds after the boundary, indexing from the wrapped clock would
        // skip a note-on exactly at loopStart and create an audible missing beat.
        s.Index = FirstIndexAtOrAfter(s.Events, s.LoopStartMs);
        s.LastDispatchMs = s.Ms;
        // The position clock moved backwards. Start a new reporting interval from the
        // wrapped timestamp or the stale pre-wrap value suppresses every UI update until
        // the playhead reaches the time of the previous iteration again.
        s.LastReportMs = s.Ms;
        ReportPosition(s.Ms, s.Timeline, generation);
        return LoopStep.Proceed;
    }

    /// <summary>Ends the run, reporting <paramref name="reportMs"/>: Break when finished or when this generation is gone, Continue when a newer start took over.</summary>
    private LoopStep FinishRun(ref SchedulerState s, int generation, double reportMs)
    {
        if (!TryFinishGeneration(generation))
            return IsGenerationCurrent(generation) ? LoopStep.Continue : LoopStep.Break;
        _timer.Set(false);
        PanicAsync();
        ReportPosition(reportMs, s.Timeline, generation);
        _onFinished?.Invoke();
        return LoopStep.Break;
    }

    /// <summary>
    /// Record count-in (asked for by RequestRecordCountIn while the song plays): one bar of clicks held at the playhead, then on from there.
    /// A pause holds the request instead of consuming it, so resuming still plays the bar rather than dropping it.
    /// </summary>
    private LoopStep StepRecordCountIn(ref SchedulerState s, int generation)
    {
        if (_paused || Interlocked.Exchange(ref _recordCountInPending, 0) == 0 || generation != Volatile.Read(ref _generation)) return LoopStep.Proceed;
        PlayLoopCountIn(s.Timeline, s.Ms, generation);
        if (!TrySetSchedulerAnchor(generation, s.Ms)) return LoopStep.Continue;
        s.Index = FirstIndexAtOrAfter(s.Events, s.Ms);
        s.LastDispatchMs = s.Ms;
        return LoopStep.Proceed;
    }

    /// <summary>Section count-in: when playback reaches a section start, hold the music for the count-in.</summary>
    private LoopStep StepSectionCountIn(ref SchedulerState s, int generation)
    {
        if (!Preferences.CountInEachSection || _paused)
        {
            s.SectionMs = null;
            return LoopStep.Proceed;
        }
        var sectionMs = s.SectionMs ??= SectionStartMs(s.Timeline, Volatile.Read(ref _sectionStarts));
        if (s.Ms < s.LastCountInMs - 1)
        {
            // Jumped back (loop wrap / skip): arm the sections ahead of the new position again.
            s.LastCountInMs = s.Ms;
            var now = s.Ms;
            s.NextSection = sectionMs.FindIndex(t => t > now + 1);
            if (s.NextSection < 0) s.NextSection = sectionMs.Count;
        }
        while (s.NextSection < sectionMs.Count && sectionMs[s.NextSection] <= s.LastCountInMs + 1) s.NextSection++;
        if (s.NextSection < sectionMs.Count && s.Ms >= sectionMs[s.NextSection] && sectionMs[s.NextSection] > s.StartMs + 1)
        {
            var at = sectionMs[s.NextSection++];
            s.LastCountInMs = at;
            PlayLoopCountIn(s.Timeline, at, generation);
            if (!TrySetSchedulerAnchor(generation, at)) return LoopStep.Continue;
            s.Ms = at;
            s.Index = FirstIndexAtOrAfter(s.Events, s.Ms);
            s.LastDispatchMs = s.Ms;
        }
        return LoopStep.Proceed;
    }

    /// <summary>Jumps over a skipped area exactly like a seamless loop wrap; Continue when a seek arrived meanwhile.</summary>
    private LoopStep StepSkips(ref SchedulerState s, int generation, out (double Start, double End)[] skips)
    {
        skips = SkipMs(s.Timeline);
        foreach (var (skipStart, skipEnd) in skips)
        {
            if (s.Ms < skipStart || s.Ms >= skipEnd) continue;
            ReleaseForLoopWrap(s.Timeline);
            RestoreChannelStateAt(s.Timeline, s.StartMs, skipEnd);
            if (!TrySetSchedulerAnchor(generation, skipEnd)) return LoopStep.Continue;
            s.Ms = skipEnd;
            s.Index = FirstIndexAtOrAfter(s.Events, s.Ms);
            s.LastDispatchMs = s.Ms;
            ReportPosition(s.Ms, s.Timeline, generation);
            break;
        }
        return LoopStep.Proceed;
    }

    /// <summary>A backlog this big means the thread was suspended; jump to now instead of burst-sending.</summary>
    private void ResyncAfterStall(ref SchedulerState s, int generation)
    {
        if (!(s.Ms - s.LastDispatchMs > StaleResyncMs && s.Index < s.Events.Count && s.Events[s.Index].TimeMs < s.Ms - StaleResyncMs)) return;
        ResetAndSetup(s.Timeline, generation);
        RestoreChannelStateAt(s.Timeline, s.StartMs, s.Ms);
        s.Index = FirstIndexAtOrAfter(s.Events, s.Ms);
        s.LastDispatchMs = s.Ms;
    }
}
