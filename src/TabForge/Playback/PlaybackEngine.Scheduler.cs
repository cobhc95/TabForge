using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Models;
using TabForge.Services;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Playback;

// Owns: the scheduler loop that sends timed events, the playhead and audible position, section and skip ranges, the trainer tempo rate and the loop bounds.
// Does not own: compiling the timeline (ScoreToMidiCompiler.cs) or the output port (PlaybackEngine.MidiOut.cs, PlaybackEngine.Scheduler.Output.cs).
// Tests: TestPlaybackScheduleReuse, TestNoHangingNotes, TestSeekWhilePlayingSoundsFirstNote.

/// <summary>The scheduler thread: musical clock, event dispatch, loop wrap and the playhead read-outs.</summary>
public sealed partial class PlaybackEngine : IDisposable
{
    private static List<double> SectionStartMs(ScoreTimeline timeline, int[] bars)
    {
        var list = new List<double>();
        if (bars.Length == 0) return list;
        var previous = -1;
        foreach (var bar in timeline.Bars)
        {
            if (bar.Bar != previous && Array.BinarySearch(bars, bar.Bar) >= 0) list.Add(bar.StartMs);
            previous = bar.Bar;
        }
        return list;
    }

    private (double Start, double End)[] SkipMs(ScoreTimeline timeline)
    {
        var bars = Volatile.Read(ref _skipBars);
        var cache = _skipCache;
        if (ReferenceEquals(cache.Timeline, timeline) && ReferenceEquals(cache.Bars, bars)) return cache.Ms;
        var list = new List<(double, double)>();
        foreach (var bar in timeline.Bars)
        {
            if (!bars.Any(r => bar.Bar >= r.Item1 && bar.Bar <= r.Item2)) continue;
            if (list.Count > 0 && Math.Abs(list[^1].Item2 - bar.StartMs) < 0.5) list[^1] = (list[^1].Item1, bar.EndMs);
            else list.Add((bar.StartMs, bar.EndMs));
        }
        var ms = list.ToArray();
        _skipCache = (timeline, bars, ms);
        return ms;
    }
    private double TrainerRate(int loopsDone)
    {
        var s = Preferences.Loop;
        if (!s.Trainer) return 1.0;
        var pct = Math.Min(Math.Max(s.TrainerFromPercent, 10) + Math.Max(0, s.TrainerStepPercent) * loopsDone, Math.Max(s.TrainerToPercent, 10));
        return Math.Clamp(pct / 100.0, 0.1, 2.0);
    }

    public PlaybackPosition Playhead()
    {
        var timeline = _timeline;
        if (timeline is null) return new PlaybackPosition();
        return PlayheadMapper.Map(timeline, Volatile.Read(ref _currentStreamMs), CurrentBar, CurrentCell);
    }

    internal double SchedulerClockMs
    {
        get
        {
            lock (_gate) return _paused ? _anchorMs : _anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate;
        }
    }

    /// <summary>
    /// Song position being heard: the scheduler position minus the output latency. The latency is wall-clock time and
    /// the timeline is already compiled at the playback speed, so it converts at the clock rate (speed trainer) only.
    /// </summary>
    internal static double AudibleStreamMs(double schedulerMs, double latencyWallMs, double clockRate) =>
        schedulerMs - Math.Max(0, latencyWallMs) * Math.Clamp(clockRate, 0.1, 4);

    /// <summary>The playhead for drawing: the live clock now (not the last 16 ms report), at the audible position.</summary>
    public PlaybackPosition AudiblePlayhead()
    {
        var timeline = _timeline;
        if (timeline is null) return new PlaybackPosition();
        double ms;
        lock (_gate)
        {
            if (!_running || _paused) return Playhead();
            var latency = _output is Audio.RoutedMidiOutput routed ? routed.AudibleLatencyMs : 0;
            ms = AudibleStreamMs(_anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate, latency, _clockRate);
        }
        // Never run ahead of what the scheduler has reached (a stalled scheduler) nor back before the start.
        // After a seek the output latency would draw the playhead just before the target (the note there would not light up yet): hold it at the target.
        var floor = _loopsDone == 0 ? Math.Max(0, _startMs) : 0;
        ms = Math.Max(floor, Math.Min(ms, Volatile.Read(ref _currentStreamMs) + MaxSleepMs + PositionIntervalMs));
        return PlayheadMapper.Map(timeline, ms, CurrentBar, CurrentCell);
    }

    private void SchedulerLoop(ScoreTimeline timeline, int generation, double startMs)
    {
        var events = timeline.Events;
        var opts = _options!;
        var (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
        var loopRangeVersion = Volatile.Read(ref _loopRangeVersion);
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
        }

        var index = FirstIndexToPlay(events, startMs);
        List<double>? sectionMs = null;
        var nextSection = 0;
        var lastCountInMs = startMs;
        var lastReportMs = double.NegativeInfinity;
        var lastDispatchMs = startMs;

        while (_running && generation == Volatile.Read(ref _generation))
        {
            double ms;
            PendingPlaybackSeek? seek;
            lock (_gate)
            {
                ms = _paused ? _anchorMs : _anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate;
                seek = _pendingSeek;
                _pendingSeek = null;
                if (seek is { } request)
                {
                    ms = request.TargetMs;
                    _anchorMs = ms;
                    _currentStreamMs = ms;
                    _sentThroughMs = ms;
                    _startMs = ms;
                    CurrentBar = request.Bar;
                    CurrentCell = request.Cell;
                    _clock.Reset();
                }
            }

            if (seek is { } seekRequest)
            {
                startMs = seekRequest.TargetMs;
                index = FirstIndexAtOrAfter(events, startMs);
                lastDispatchMs = startMs;
                lastCountInMs = startMs;
                lastReportMs = double.NegativeInfinity;
                sectionMs = null;
                nextSection = 0;
                _rearm = false;
                ResetAndSetup(timeline, generation);
                if (!_running || generation != Volatile.Read(ref _generation)) continue;
                if (!CanScheduleWork(generation)) continue;
                RestoreChannelStateAt(timeline, timeline.PlayFromMs, startMs);
                var seekPosition = CommitPendingSeekPosition(generation, timeline, seekRequest, startMs);
                if (seekPosition is { } acceptedPosition) _onPosition?.Invoke(acceptedPosition);
                else continue;
                ms = startMs;
            }

            var currentLoopVersion = Volatile.Read(ref _loopRangeVersion);
            if (currentLoopVersion != loopRangeVersion)
            {
                loopRangeVersion = currentLoopVersion;
                (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
            }

            var waitForArrangement = false;
            var timelineRevised = false;
            lock (_gate)
            {
                if (_arrangementRefreshRequested && ms + 0.001 >= _arrangementRefreshBoundaryMs)
                {
                    if (_pendingArrangementRefresh is not { } refresh)
                    {
                        if (_arrangementRefreshLive)
                        {
                            // A live edit's compile is not ready: the old schedule plays on (nothing dropped, nothing late) and the edit is taken a bar later.
                            _arrangementRefreshRequested = false;
                            _refreshSeq++;
                            _pendingLoopRefresh = null;
                            Volatile.Write(ref _liveRefreshMissed, 1);
                        }
                        else waitForArrangement = true;
                    }
                    else
                    {
                        timeline = refresh.Timeline;
                        Volatile.Write(ref _timeline, timeline);
                        _scheduleReuse = null;
                        _project = refresh.Project;
                        events = timeline.Events;
                        index = FirstIndexAtOrAfter(events, refresh.BoundaryMs);
                        lastDispatchMs = ms;
                        _pendingArrangementRefresh = null;
                        _arrangementRefreshRequested = false;
                        (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
                        timelineRevised = true;
                    }
                }
            }
            if (waitForArrangement)
            {
                Thread.Sleep(1);
                continue;
            }
            if (timelineRevised) { TimelineRevised?.Invoke(timeline); sectionMs = null; }

            // A paused transport retains its timeline position but must not dispatch upcoming notes.
            // This also makes seeking while paused safe: the replacement scheduler starts paused.
            if (_paused)
            {
                if (ms - lastReportMs >= PositionIntervalMs)
                {
                    lastReportMs = ms;
                    ReportPosition(ms, timeline);
                }
                Thread.Sleep((int)PositionIntervalMs);
                continue;
            }

            if (_rearm)
            {
                _rearm = false;
                RearmChannelSetup(timeline, generation);
                // Then whatever changed since the start (a new program, volume or pan set in the song) as of now, so a
                // synth created mid-song sounds like the rest, not like its defaults.
                if (ms > startMs) RestoreChannelStateAt(timeline, startMs, ms);
            }

            var loopEnabled = loopAvailable && opts.Loop;
            if (loopEnabled && ms >= loopEndMs)
            {
                bool boundaryAdmitted;
                lock (_gate)
                    boundaryAdmitted = _running && generation == Volatile.Read(ref _generation) && _pendingSeek is null;
                if (!boundaryAdmitted) continue;
                var settings = Preferences.Loop;
                _loopsDone++;
                LoopCompleted?.Invoke(this, _loopsDone);
                if (settings.Count > 0 && _loopsDone >= settings.Count)
                {
                    // All requested loops played: finish at the loop end.
                    if (!TryFinishGeneration(generation))
                    {
                        if (IsGenerationCurrent(generation)) continue;
                        break;
                    }
                    _timer.Set(false);
                    PanicAsync();
                    ReportPosition(loopEndMs, timeline);
                    _onFinished?.Invoke();
                    break;
                }
                var length = loopEndMs - loopStartMs;
                var overshoot = ms - loopEndMs;
                LoopRefresh? loopSwap;
                lock (_gate) { loopSwap = _pendingLoopRefresh; _pendingLoopRefresh = null; }
                if (loopSwap is not null)
                {
                    // An edit was made while looping: the whole song, recompiled, replaces the timeline at the wrap, so edited loop bars behind the playhead are heard.
                    ReleaseForLoopWrap(timeline);
                    timeline = loopSwap.Timeline;
                    Volatile.Write(ref _timeline, timeline);
                    _scheduleReuse = null;
                    _project = loopSwap.Project;
                    events = timeline.Events;
                    (loopStartMs, loopEndMs, loopAvailable) = ComputeLoopBounds(timeline, opts);
                    length = loopEndMs - loopStartMs;
                    TimelineRevised?.Invoke(timeline);
                    sectionMs = null;
                }
                var wrapped = loopStartMs + (length > 0 ? overshoot % length : 0);
                // Seamless wrap: release sounding notes and restore the controller state of the loop
                // start (a few messages) instead of a full device reset, so there is no gap.
                ReleaseForLoopWrap(timeline);
                RestoreChannelStateAt(timeline, startMs, wrapped);
                if (settings.CountInEachLoop) PlayLoopCountIn(timeline, loopStartMs, generation);
                var nextLoopMs = settings.CountInEachLoop ? loopStartMs : wrapped;
                if (!TrySetSchedulerAnchor(generation, nextLoopMs, TrainerRate(_loopsDone))) continue;
                ms = nextLoopMs;
                // Keep events at the loop's first instant in the dispatch window. If the scheduler
                // wakes a few milliseconds after the boundary, indexing from the wrapped clock would
                // skip a note-on exactly at loopStart and create an audible missing beat.
                index = FirstIndexAtOrAfter(events, loopStartMs);
                lastDispatchMs = ms;
                // The position clock moved backwards. Start a new reporting interval from the
                // wrapped timestamp or the stale pre-wrap value suppresses every UI update until
                // the playhead reaches the previous iteration's time again.
                lastReportMs = ms;
                ReportPosition(ms, timeline);
            }

            // Section count-in: when playback reaches a section start, hold the music for the count-in.
            if (Preferences.CountInEachSection && !_paused)
            {
                sectionMs ??= SectionStartMs(timeline, Volatile.Read(ref _sectionStarts));
                if (ms < lastCountInMs - 1)
                {
                    // Jumped back (loop wrap / skip): arm the sections ahead of the new position again.
                    lastCountInMs = ms;
                    nextSection = sectionMs.FindIndex(t => t > ms + 1);
                    if (nextSection < 0) nextSection = sectionMs.Count;
                }
                while (nextSection < sectionMs.Count && sectionMs[nextSection] <= lastCountInMs + 1) nextSection++;
                if (nextSection < sectionMs.Count && ms >= sectionMs[nextSection] && sectionMs[nextSection] > startMs + 1)
                {
                    var at = sectionMs[nextSection++];
                    lastCountInMs = at;
                    PlayLoopCountIn(timeline, at, generation);
                    if (!TrySetSchedulerAnchor(generation, at)) continue;
                    ms = at;
                    index = FirstIndexAtOrAfter(events, ms);
                    lastDispatchMs = ms;
                }
            }
            else sectionMs = null;

            var skips = SkipMs(timeline);
            var seekDuringSkip = false;
            foreach (var (skipStart, skipEnd) in skips)
            {
                if (ms < skipStart || ms >= skipEnd) continue;
                // Jump over the skipped area exactly like a seamless loop wrap.
                ReleaseForLoopWrap(timeline);
                RestoreChannelStateAt(timeline, startMs, skipEnd);
                if (!TrySetSchedulerAnchor(generation, skipEnd)) { seekDuringSkip = true; break; }
                ms = skipEnd;
                index = FirstIndexAtOrAfter(events, ms);
                lastDispatchMs = ms;
                ReportPosition(ms, timeline);
                break;
            }
            if (seekDuringSkip) continue;

            // A backlog this big means the thread was suspended; jump to now instead of burst-sending.
            if (ms - lastDispatchMs > StaleResyncMs && index < events.Count && events[index].TimeMs < ms - StaleResyncMs)
            {
                ResetAndSetup(timeline, generation);
                RestoreChannelStateAt(timeline, startMs, ms);
                index = FirstIndexAtOrAfter(events, ms);
                lastDispatchMs = ms;
            }

            var horizon = ms + DispatchLeadMs;
            // Never send notes past the loop end: they belong to the bar after the loop.
            if (loopEnabled) horizon = Math.Min(horizon, loopEndMs - 0.001);
            if (sectionMs is not null && nextSection < sectionMs.Count && sectionMs[nextSection] > ms)
                horizon = Math.Min(horizon, sectionMs[nextSection] - 0.001); // hold notes of the next section until after its count-in
            foreach (var (skipStart, skipEnd) in skips)
                if (skipStart > ms) { horizon = Math.Min(horizon, skipStart - 0.001); break; }
            lock (_gate)
            {
                if (_arrangementRefreshRequested && ms < _arrangementRefreshBoundaryMs)
                    horizon = Math.Min(horizon, _arrangementRefreshBoundaryMs - 0.001);
            }
            var staleStateDropped = false;
            while (index < events.Count && events[index].TimeMs <= horizon)
            {
                var e = events[index++];
                if (e.IsSetup) continue;                             // sent by SendChannelSetup
                var stale = e.TimeMs < ms - MaxLateMs;
                if (stale && IsEssentialReleaseOrReset(e))
                {
                    if (staleStateDropped)
                    {
                        RestoreChannelStateAt(timeline, startMs, ms);
                        staleStateDropped = false;
                    }
                    if (!CanScheduleWork(generation)) break;
                    Dispatch(e, ms, startMs);
                    continue;
                }
                if (stale && IsChannelStateMessage(e))
                {
                    // Do not play an old bend/controller ramp after a stall. Before the next live
                    // event, restore the last state that should be active now (including a missed
                    // pitch-wheel centre reset), rather than leaving the device in the last sent state.
                    staleStateDropped = true;
                    continue;
                }
                if (stale) continue;                                 // old attacks are skipped, releases are essential
                if (staleStateDropped)
                {
                    RestoreChannelStateAt(timeline, startMs, ms);
                    staleStateDropped = false;
                }
                if (!CanScheduleWork(generation)) break;
                Dispatch(e, ms, startMs);
            }
            if (staleStateDropped) RestoreChannelStateAt(timeline, startMs, ms);
            lastDispatchMs = ms;
            Volatile.Write(ref _sentThroughMs, index > 0 ? Math.Max(ms, events[index - 1].TimeMs) : ms);

            if (!loopEnabled && ms >= timeline.TotalMs)
            {
                if (!TryFinishGeneration(generation))
                {
                    if (IsGenerationCurrent(generation)) continue;
                    break;
                }
                _timer.Set(false);
                PanicAsync();
                ReportPosition(ms, timeline);
                _onFinished?.Invoke();
                break;
            }

            if (ms - lastReportMs >= PositionIntervalMs)
            {
                lastReportMs = ms;
                ReportPosition(ms, timeline);
            }

            var nextMs = index < events.Count ? events[index].TimeMs : double.MaxValue;
            if (loopEnabled) nextMs = Math.Min(nextMs, loopEndMs + DispatchLeadMs); // wake exactly at the wrap
            SleepUntil(ms, nextMs, _clockRate);
        }
    }

    private (double startMs, double endMs, bool available) ComputeLoopBounds(ScoreTimeline timeline, PlaybackOptions opts)
    {
        var project = _project;
        if (project is null) return (0, 0, false);
        return PlaybackOrder.LoopBounds(timeline, project, opts);
    }
}
