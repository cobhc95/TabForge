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
            lock (_gate)
            {
                ms = _paused ? _anchorMs : _anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate;
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
                var settings = Preferences.Loop;
                _loopsDone++;
                LoopCompleted?.Invoke(this, _loopsDone);
                if (settings.Count > 0 && _loopsDone >= settings.Count)
                {
                    // All requested loops played: finish at the loop end.
                    _running = false;
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
                lock (_gate)
                {
                    _anchorMs = settings.CountInEachLoop ? loopStartMs : wrapped;
                    _clock.Restart();
                    _clockRate = TrainerRate(_loopsDone);
                    ms = _anchorMs;
                }
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
                    lock (_gate)
                    {
                        _anchorMs = at;
                        _clock.Restart();
                        ms = _anchorMs;
                    }
                    index = FirstIndexAtOrAfter(events, ms);
                    lastDispatchMs = ms;
                }
            }
            else sectionMs = null;

            var skips = SkipMs(timeline);
            foreach (var (skipStart, skipEnd) in skips)
            {
                if (ms < skipStart || ms >= skipEnd) continue;
                // Jump over the skipped area exactly like a seamless loop wrap.
                ReleaseForLoopWrap(timeline);
                RestoreChannelStateAt(timeline, startMs, skipEnd);
                lock (_gate)
                {
                    _anchorMs = skipEnd;
                    _clock.Restart();
                    ms = _anchorMs;
                }
                index = FirstIndexAtOrAfter(events, ms);
                lastDispatchMs = ms;
                ReportPosition(ms, timeline);
                break;
            }

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
                Dispatch(e, ms, startMs);
            }
            if (staleStateDropped) RestoreChannelStateAt(timeline, startMs, ms);
            lastDispatchMs = ms;
            Volatile.Write(ref _sentThroughMs, index > 0 ? Math.Max(ms, events[index - 1].TimeMs) : ms);

            if (!loopEnabled && ms >= timeline.TotalMs)
            {
                _running = false;
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

    private void ReleaseForLoopWrap(ScoreTimeline timeline)
    {
        _activeMetronomeNotes.Clear();
        var channels = new HashSet<(int Device, int Channel)>();
        foreach (var e in timeline.ChannelSetup) channels.Add((e.DeviceId, e.Status & 0x0F));
        lock (_outputGate)
            foreach (var (device, channel) in channels)
            {
                _output.Send(device, 0xB0 | channel, 64, 0);  // sustain off
                _output.Send(device, 0xB0 | channel, 123, 0); // all notes off
            }
    }

    // One bar of clicks before the loop restarts (accent on the first beat), at the loop's tempo.
    private void PlayLoopCountIn(ScoreTimeline timeline, double loopStartMs, int generation)
    {
        var bar = timeline.BarAt(loopStartMs);
        // Quarter-note length on the timeline clock, then in wall time under the trainer rate.
        var barMs = Math.Max(1, bar.EndMs - bar.StartMs);
        var quarter = TempoMath.MsPerBeat(Math.Max(20, bar.Tempo)) / Math.Max(0.1, Speed);
        if (Math.Round(barMs / quarter) is < 1 or > 12) quarter = TempoMath.MsPerBeat(Math.Max(20, bar.Tempo));
        var beats = Math.Clamp((int)Math.Round(barMs / quarter), 1, 12);
        var beatMs = quarter / Math.Max(0.1, _clockRate);
        var device = timeline.ChannelSetup.Count > 0 ? timeline.ChannelSetup[0].DeviceId : 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var beat = 0; beat < beats && _running && generation == Volatile.Read(ref _generation); beat++)
        {
            var note = beat == 0
                ? (Preferences.CountInAccentNote >= 0 ? Preferences.CountInAccentNote : Volatile.Read(ref _metronomeAccentNote))
                : (Preferences.CountInClickNote >= 0 ? Preferences.CountInClickNote : Volatile.Read(ref _metronomeClickNote));
            var velocity = MetronomeVelocity(Math.Clamp((int)Math.Round(127 * Preferences.CountInVolume / 100.0), 1, 127), 9);
            lock (_outputGate)
            {
                _output.Send(device, 0x99, note, velocity);
                if (Preferences.MetronomeBoost) foreach (var layer in BoostLayers) if (layer != note) _output.Send(device, 0x99, layer, velocity);
            }
            var until = (beat + 1) * beatMs;
            while (clock.Elapsed.TotalMilliseconds < until - 1) Thread.Sleep(1);
        }
    }

    private void Dispatch(ScoreEvent e, double streamMs, double startMs)
    {
        var status = e.Status;
        var data1 = e.Data1;
        var data2 = e.Data2;
        // Live mute/solo: every track is compiled, silenced tracks simply do not start notes.
        if (e.IsNoteOn && e.TrackIndex >= 0 && Volatile.Read(ref _audible) is { } audible &&
            e.TrackIndex < audible.Length && !audible[e.TrackIndex]) return;
        if (e.IsMetronome)
        {
            if (e.IsNoteOff)
            {
                if (!_activeMetronomeNotes.TryRemove(e.MetronomePairId, out data1)) return;
                data2 = 0;
            }
            else
            {
                if (!e.IsCountInClick && Volatile.Read(ref _metronomeEnabled) == 0) return;
                var subdivisions = Volatile.Read(ref _metronomeSubdivision);
                if (e.MetronomeTick % (12 / subdivisions) != 0) return;
                var note = e.IsMetronomeAccent
                    ? (e.IsCountInClick && Preferences.CountInAccentNote >= 0 ? Preferences.CountInAccentNote : Volatile.Read(ref _metronomeAccentNote))
                    : (e.IsCountInClick && Preferences.CountInClickNote >= 0 ? Preferences.CountInClickNote : Volatile.Read(ref _metronomeClickNote));
                var relativeVolume = e.IsMetronomeAccent
                    ? Volatile.Read(ref _metronomeAccentVolume)
                    : Volatile.Read(ref _metronomeClickVolume);
                // Full scale: at 100% the click hits maximum velocity at full channel level, above the song.
                var level = e.IsCountInClick ? Preferences.CountInVolume : Volatile.Read(ref _metronomeVolume);
                data2 = (int)Math.Round(127 * level / 100.0 * relativeVolume / 100.0);
                if (data2 <= 0) return;
                data2 = MetronomeVelocity(Math.Clamp(data2, 1, 127), status);
                data1 = note;
                _activeMetronomeNotes[e.MetronomePairId] = note;
            }
        }
        data2 = MasterScaled(status, data1, data2);
        lock (_outputGate)
        {
            _output.Send(e.DeviceId, status, data1, data2);
            if (e.IsMetronome && Preferences.MetronomeBoost)
                foreach (var layer in BoostLayers)
                    if (layer != data1) _output.Send(e.DeviceId, status, layer, data2);
        }
        if (!_diagnostics) return;
        var wall = _diagnosticClock.Elapsed.TotalMilliseconds - _playbackStartWallMs;
        var latency = wall - (streamMs - startMs);
        var record = new DispatchRecord(streamMs, latency, e.TrackIndex, status, data1, data2, e.DeviceId);
        lock (_logGate) { if (_dispatchLog.Count < InputLimits.MaxDiagnosticRecords) _dispatchLog.Add(record); }
    }

    /// <summary>
    /// Reapply only the latest persistent channel messages at <paramref name="timeMs"/>. This avoids
    /// replaying a stale modulation ramp while ensuring a skipped reset cannot leave MIDI state stuck.
    /// </summary>
    internal void RestoreChannelStateAt(ScoreTimeline timeline, double startMs, double timeMs)
    {
        var latest = new Dictionary<(int Device, int Status, int Data1), ScoreEvent>();
        foreach (var e in timeline.ChannelSetup)
            if (IsChannelStateMessage(e)) latest[ChannelStateKey(e)] = e;

        var events = timeline.Events;
        for (var i = FirstIndexAtOrAfter(events, startMs); i < events.Count && events[i].TimeMs <= timeMs; i++)
        {
            var e = events[i];
            if (!e.IsSetup && IsChannelStateMessage(e)) latest[ChannelStateKey(e)] = e;
        }

        lock (_outputGate)
            foreach (var e in latest.Values.OrderBy(e => e.TimeMs))
                _output.Send(e.DeviceId, e.Status, e.Data1, MasterScaled(e.Status, e.Data1, e.Data2));
    }

    private static bool IsChannelStateMessage(ScoreEvent e)
    {
        var kind = e.Status & 0xF0;
        return kind switch
        {
            0xA0 or 0xC0 or 0xD0 or 0xE0 => true,
            0xB0 => e.Data1 < 120, // channel-mode messages (all notes off/reset) are not state snapshots
            _ => false
        };
    }

    internal static bool IsEssentialReleaseOrReset(ScoreEvent e)
    {
        if (e.IsNoteOff) return true;
        var kind = e.Status & 0xF0;
        if (kind == 0xE0 && e.Data1 == 0 && e.Data2 == 64) return true; // pitch-wheel centre
        if (kind == 0xB0 && e.Data1 == 1 && e.Data2 == 0) return true; // modulation-wheel neutral
        return kind == 0xB0 && e.Data1 is 120 or 121 or 123;
    }

    private static (int Device, int Status, int Data1) ChannelStateKey(ScoreEvent e)
    {
        var kind = e.Status & 0xF0;
        return (e.DeviceId, e.Status, kind is 0xA0 or 0xB0 ? e.Data1 : -1);
    }

    private void ReportPosition(double ms, ScoreTimeline timeline)
    {
        Volatile.Write(ref _currentStreamMs, ms);
        var pos = PlayheadMapper.Map(timeline, ms, CurrentBar, CurrentCell);
        CurrentBar = pos.Bar;
        CurrentCell = pos.Cell;
        _onPosition?.Invoke(pos);
    }

    /// <summary>Sleeps until shortly before the next event, or a short tick so the UI keeps updating.</summary>
    private static void SleepUntil(double nowMs, double nextEventMs, double rate = 1.0)
    {
        var wait = ((nextEventMs - DispatchLeadMs) - nowMs) / Math.Max(0.1, rate);
        if (wait <= 0.2) { Thread.Yield(); return; }
        var sleep = (int)Math.Min(Math.Ceiling(wait), MaxSleepMs);
        Thread.Sleep(Math.Max(1, sleep));
    }

    /// <summary>An event within this of a seek/loop target is AT the target (the target and the event are computed by different float paths).</summary>
    private const double SeekToleranceMs = 0.05;

    private static int FirstIndexAtOrAfter(List<ScoreEvent> events, double ms)
    {
        var lo = 0;
        var hi = events.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (events[mid].TimeMs < ms - SeekToleranceMs) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private (double startMs, double endMs, bool available) ComputeLoopBounds(ScoreTimeline timeline, PlaybackOptions opts)
    {
        var project = _project;
        if (project is null) return (0, 0, false);
        return PlaybackOrder.LoopBounds(timeline, project, opts);
    }
}
