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
// Owns: the playhead read-outs: the scheduler's musical position, its clock, and the audible position (the scheduler position less the output latency).
// Does not own: the scheduler loop that moves the clock (PlaybackEngine.Scheduler.cs) or the output port (PlaybackEngine.MidiOut.cs).
// Tests: no dedicated test class.

/// <summary>The playhead read-outs the UI polls on its frame tick; they read the scheduler's state under its lock and never block it long.</summary>
public sealed partial class PlaybackEngine
{
    public PlaybackPosition Playhead()
    {
        var timeline = _timeline;
        if (timeline is null) return HeldForPendingStart(new PlaybackPosition());
        var position = PlayheadMapper.Map(timeline, Volatile.Read(ref _currentStreamMs), CurrentBar, CurrentCell);
        // While a seek compiles, the requested bar and cell show: the timeline on screen may not hold that bar yet.
        if (_seekRebuild.IsPending)
        {
            position.Bar = CurrentBar;
            position.Cell = CurrentCell;
        }
        return position;
    }

    internal double SchedulerClockMs
    {
        get
        {
            lock (_gate) return _paused ? _anchorMs : _anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate;
        }
    }

    internal static double AudibleStreamMs(double schedulerMs, double latencyWallMs, double clockRate) =>
        PlaybackFrameRules.AudibleStreamMs(schedulerMs, latencyWallMs, clockRate);

    /// <summary>The playhead for drawing (UI thread): the live clock now, at the audible position; while it plays it fills <paramref name="frame"/>.</summary>
    public PlaybackPosition AudiblePlayhead(PlaybackPosition frame)
    {
        var timeline = _timeline;
        if (timeline is null) return HeldForPendingStart(PlayheadMapper.Reset(frame));
        double ms;
        double anchorMs;
        lock (_gate)
        {
            anchorMs = _anchorMs;
            if (_seekRebuild.IsPending)
            {
                // A seek is compiling: the playhead holds at the requested bar and cell until the scheduler resumes there.
                var held = PlayheadMapper.MapInto(frame, timeline, Volatile.Read(ref _currentStreamMs), CurrentBar, CurrentCell);
                held.Bar = CurrentBar;
                held.Cell = CurrentCell;
                return held;
            }
            // A start holds the playhead at its start until the scheduler anchors the clock there (after the device reset and any count-in before
            // the start or a section), so it never runs ahead and snaps back. The held position also floors the audible one under that anchor,
            // so the output latency never draws it back before the hold once the clock runs.
            if (!_running || _paused || _startSettling)
            {
                var held = Volatile.Read(ref _currentStreamMs);
                if (_startSettling) _audibleHold.Hold(timeline, anchorMs, held);
                return PlayheadMapper.MapInto(frame, timeline, held, CurrentBar, CurrentCell);
            }
            var latency = _output is Audio.RoutedMidiOutput routed ? routed.AudibleLatencyMs : 0;
            ms = AudibleStreamMs(_anchorMs + _clock.Elapsed.TotalMilliseconds * _clockRate, latency, _clockRate);
        }
        // Never run ahead of what the scheduler has reached (a stalled scheduler) nor back before the start.
        // After a seek the output latency would draw the playhead just before the target (the note there would not light up yet): hold it at the target.
        var floor = _loopsDone == 0 ? Math.Max(0, _startMs) : 0;
        ms = Math.Max(floor, Math.Min(ms, Volatile.Read(ref _currentStreamMs) + MaxSleepMs + PositionIntervalMs));
        ms = _audibleHold.Hold(timeline, anchorMs, ms);
        ms = _loopHold.Hold(ms, _options is { Loop: true }, _loopsDone > 0);   // inside the loop until the scheduler wraps
        return PlayheadMapper.MapInto(frame, timeline, ms, CurrentBar, CurrentCell);
    }

    // Holds the live audible position against a latency rise under one anchor (see AudiblePositionHold). UI thread only.
    private readonly AudiblePositionHold _audibleHold = new();
}
