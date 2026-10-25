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
// Tests: TestNoHangingNotes.

/// <summary>The scheduler thread: musical clock, event dispatch, loop wrap and the playhead read-outs.</summary>
public sealed partial class PlaybackEngine : IDisposable, IPlaybackEngine
{
    private (ScoreTimeline? Timeline, int[] Bars, List<double> Ms) _sectionCache = (null, Array.Empty<int>(), new List<double>());

    private List<double> SectionStartMs(ScoreTimeline timeline, int[] bars)
    {
        if (bars.Length == 0) return new List<double>();
        var cache = _sectionCache;
        if (ReferenceEquals(cache.Timeline, timeline) && ReferenceEquals(cache.Bars, bars)) return cache.Ms;
        var list = new List<double>();
        var previous = -1;
        foreach (var bar in timeline.Bars)
        {
            if (bar.Bar != previous && Array.BinarySearch(bars, bar.Bar) >= 0) list.Add(bar.StartMs);
            previous = bar.Bar;
        }
        _sectionCache = (timeline, bars, list);
        return list;
    }

    private (double Start, double End)[] SkipMs(ScoreTimeline timeline)
    {
        var bars = Volatile.Read(ref _skipBars);
        if (bars.Length == 0) return Array.Empty<(double, double)>();
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

    /// <summary>What the scheduler found for a seek rebuild: nothing ready yet, the rebuilt timeline swapped in, or the compile failed.</summary>
    private enum RebuildStep { Waiting, Swapped, Failed }

    /// <summary>
    /// The scheduler's turn to take a finished seek rebuild. Under the engine lock the rebuilt timeline becomes the live one, and the pending seek takes
    /// its start, so the seek branch of the loop resumes there on the new timeline. Runs on the scheduler thread only.
    /// </summary>
    private RebuildStep TakeRebuiltSeek(int generation, out ScoreTimeline? timeline, out double resumeMs)
    {
        timeline = null;
        resumeMs = 0;
        lock (_gate)
        {
            if (generation != Volatile.Read(ref _generation)) return RebuildStep.Waiting;   // a stopped run leaves the next start's rebuild alone
            if (!_seekRebuild.TryTake(out var request)) return RebuildStep.Waiting;
            if (request!.Failure is not null)
            {
                _running = false;
                _pendingSeek = null;
                return RebuildStep.Failed;
            }
            var built = request.Timeline!;
            Publish(built, request.Project, request.Snapshot);
            ClearProgramPreviews();   // the rebuilt timeline carries the programs of the song as it is now
            _scheduleReuse = null;   // the reuse check needs the engine's own song and options; a rebuilt timeline is captured on the next start
            _resumeSkipThroughMs = double.NaN;
            if (_pendingSeek is { } pending) _pendingSeek = pending with { TargetMs = request.StartMs };
            _anchorMs = request.StartMs;
            _currentStreamMs = request.StartMs;
            _sentThroughMs = request.StartMs;
            _startMs = request.StartMs;
            // A pending seek restarts the clock once its device reset is done; with none left (it was taken on the old timeline), playback resumes now.
            if (_paused || _pendingSeek is not null) _clock.Reset(); else _clock.Restart();
            timeline = built;
            resumeMs = request.StartMs;
            return RebuildStep.Swapped;
        }
    }

    /// <summary>Reads the song position under the gate and takes a pending seek (re-anchoring the clock on it); false when this run was stopped.</summary>
    private bool TryReadClock(int generation, out double ms, out PendingPlaybackSeek? seek)
    {
        lock (_gate)
        {
            if (generation != Volatile.Read(ref _generation)) { ms = 0; seek = null; return false; }
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
            return true;
        }
    }

    /// <summary>How far ahead of the clock events may be sent: the dispatch lead, cut at the loop end, the next section's count-in, a skip and a pending arrangement swap.</summary>
    private double DispatchHorizon(double ms, bool loopEnabled, double loopEndMs, List<double>? sectionMs, int nextSection, (double Start, double End)[] skips)
    {
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
        return horizon;
    }

    private void DispatchDue(ScoreTimeline timeline, List<ScoreEvent> events, ref int index, double horizon, double ms, double startMs, int generation)
    {
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
                Dispatch(e, ms, startMs, generation);
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
            if (stale)
            {
                if (e.IsNoteOn && !e.IsMetronome) Interlocked.Increment(ref _lateNotesDropped);   // old attacks are skipped, releases are essential
                continue;
            }
            if (staleStateDropped)
            {
                RestoreChannelStateAt(timeline, startMs, ms);
                staleStateDropped = false;
            }
            if (!CanScheduleWork(generation)) break;
            Dispatch(e, ms, startMs, generation);
        }
        if (staleStateDropped) RestoreChannelStateAt(timeline, startMs, ms);
    }

    // The running song and its timeline change together: a reader under the gate (a seek's reuse check) never sees a new timeline with the old song or the reverse.
    private void Publish(ScoreTimeline? timeline, SongProject project, SongSnapshot snapshot)
    {
        lock (_gate)
        {
            // Tracks deleted from the old timeline are dropped with it: the old Timeline is not kept alive by _dropped.
            if (!ReferenceEquals(Volatile.Read(ref _song)?.Timeline, timeline)) Volatile.Write(ref _dropped, null);
            Volatile.Write(ref _song, new SongState(timeline, project, snapshot, snapshot.TrackIds));
        }
    }

    private (double startMs, double endMs, bool available) ComputeLoopBounds(ScoreTimeline timeline, PlaybackOptions opts)
    {
        IScoreQuery? score = Volatile.Read(ref _song)?.Snapshot;   // scheduler thread: the song copy, never the live song
        (double startMs, double endMs, bool available) bounds = score is null ? (0d, 0d, false) : score.LoopBounds(timeline, opts);
        _loopHold.Set(bounds.startMs, bounds.endMs, bounds.available);
        return bounds;
    }

    // The loop bounds the drawn playhead is held within (AudiblePlayhead); written by the scheduler thread here.
    private readonly LoopPlayheadHold _loopHold = new();
}
