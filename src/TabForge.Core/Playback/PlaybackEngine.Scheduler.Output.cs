using System.Linq;
using TabForge.Models;
using TabForge.Services;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Playback;

// Owns: event dispatch, channel restoration and output-side scheduler helpers.
// Does not own: transport state or timeline compilation.
// Tests: PlaybackSeekSwapTests.

/// <summary>Scheduler output and event helpers.</summary>
/// <remarks>Owns: event dispatch, channel restoration and output-side scheduler helpers. Does not own: transport state or timeline compilation. Tests: playback scheduling, loop and seek tests.</remarks>
public sealed partial class PlaybackEngine : IDisposable
{
    /// <summary>1 while a record count-in is waiting for the scheduler thread; the scheduler takes it (sets 0) once per request.</summary>
    private int _recordCountInPending;

    /// <summary>Asks the playing schedule for one bar of count-in clicks held at the playhead (the record button while the song plays). The scheduler plays it; this call never blocks.</summary>
    public void RequestRecordCountIn() => Interlocked.Exchange(ref _recordCountInPending, 1);

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

    // One bar of clicks before the loop, a section or a start inside a loop (accent on the first beat), at that bar's tempo. The audible playhead
    // holds at loopStartMs meanwhile, and the clock is anchored there before the hold ends, so the drawn line never runs on and steps back.
    private void PlayLoopCountIn(ScoreTimeline timeline, double loopStartMs, int generation)
    {
        bool wasHeld;
        lock (_gate)
        {
            if (generation != Volatile.Read(ref _generation)) return;
            (wasHeld, _startSettling, _currentStreamMs, _anchorMs) = (_startSettling, true, loopStartMs, loopStartMs);
        }
        try
        {
            var bar = timeline.BarAt(loopStartMs);
            // Quarter-note length on the timeline clock, then in wall time under the trainer rate.
            var barMs = Math.Max(1, bar.EndMs - bar.StartMs);
            var quarter = TempoMath.MsPerBeat(Math.Max(20, bar.Tempo)) / Math.Max(0.1, Speed);
            if (Math.Round(barMs / quarter) is < 1 or > 12) quarter = TempoMath.MsPerBeat(Math.Max(20, bar.Tempo));
            var beats = Math.Clamp((int)Math.Round(barMs / quarter), 1, 12);
            var beatMs = quarter / Math.Max(0.1, _clockRate);
            // The clicks go to the device the compiled count-in clicks use (the metronome's own destination, never a song channel's route).
            var device = timeline.Events.FirstOrDefault(e => e.IsCountInClick)?.DeviceId ?? (timeline.ChannelSetup.Count > 0 ? timeline.ChannelSetup[0].DeviceId : 0);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var beat = 0; beat < beats && _running && generation == Volatile.Read(ref _generation); beat++)
            {
                var note = beat == 0
                    ? (Preferences.CountInAccentNote >= 0 ? Preferences.CountInAccentNote : Volatile.Read(ref _metronomeAccentNote))
                    : (Preferences.CountInClickNote >= 0 ? Preferences.CountInClickNote : Volatile.Read(ref _metronomeClickNote));
                var velocity = MetronomeVelocity(Math.Clamp((int)Math.Round(MidiLimits.Max * Preferences.CountInVolume / 100.0), 1, MidiLimits.Max), 9);
                lock (_outputGate)
                {
                    if (generation != Volatile.Read(ref _generation)) return;   // stopped meanwhile: no click after the device reset
                    _output.SendMetronome(device, 0x99, note, velocity);
                    if (Preferences.MetronomeBoost) foreach (var layer in BoostLayers) if (layer != note) _output.SendMetronome(device, 0x99, layer, velocity);
                }
                var until = (beat + 1) * beatMs;
                while (clock.Elapsed.TotalMilliseconds < until - 2) Thread.Sleep(1);
                while (clock.Elapsed.TotalMilliseconds < until) { Thread.Sleep(1); Thread.Yield(); }   // the last stretch: yield instead of spinning, the next click follows on the beat
            }
        }
        finally
        {
            lock (_gate)
            {
                if (generation == Volatile.Read(ref _generation) && _pendingSeek is null && !_paused) { _anchorMs = loopStartMs; _clock.Restart(); }
                if (generation == Volatile.Read(ref _generation) && !wasHeld) _startSettling = false;   // a start's own hold ends when the scheduler anchors it
            }
        }
    }

    /// <summary>The event's data bytes as one pair: a setup event is rewritten by the live push under the output lock, so it is read under it too (never a new Data1 with an old Data2).</summary>
    private (int Data1, int Data2) DataOf(ScoreEvent e)
    {
        if (!e.IsSetup) return (e.Data1, e.Data2);
        lock (_outputGate) return (e.Data1, e.Data2);
    }

    private void Dispatch(ScoreEvent e, double streamMs, double startMs, int generation)
    {
        var status = e.Status;
        var (data1, data2) = DataOf(e);
        if (e.TrackIndex >= 0 && Volatile.Read(ref _dropped) is { } gone && e.TrackIndex < gone.Dropped.Length && gone.Dropped[e.TrackIndex] &&
            ReferenceEquals(gone.Timeline, _timeline)) return;   // a track deleted while this schedule plays: silent at once
        // Live mute/solo: every track is compiled, silenced tracks simply do not start notes (an articulation keyswitch is no note: it is still sent, so the instrument is in the right articulation on unmute).
        if (e.IsNoteOn && !e.IsArticulation && e.TrackIndex >= 0 && Volatile.Read(ref _audible) is { } audible &&
            e.TrackIndex < audible.Length && !audible[e.TrackIndex]) return;
        // Practice silence: the track's notes neither start nor end, so a song note-off never cuts a key the player holds on its channel (notes sounding when it was set got All Notes Off).
        // Its bend, sustain, volume/expression and program messages are dropped too: they would land on the channel the player's live notes use (restored when the silence ends).
        if (e.TrackIndex >= 0 && e.TrackIndex == Volatile.Read(ref _practiceSilence) && (e.IsNoteOn || e.IsNoteOff || IsPracticeSilencedState(e))) return;
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
                data2 = (int)Math.Round(MidiLimits.Max * level / 100.0 * relativeVolume / 100.0);
                if (data2 <= 0) return;
                data2 = MetronomeVelocity(Math.Clamp(data2, 1, MidiLimits.Max), status);
                data1 = note;
                _activeMetronomeNotes[e.MetronomePairId] = note;
            }
        }
        data2 = MasterScaled(status, data1, data2);
        data1 = ProgramFor(status, data1);
        lock (_outputGate)
        {
            // A stopped run starts no note: Stop moves the generation before its device reset, which runs under this lock.
            if (e.IsNoteOn && generation != Volatile.Read(ref _generation)) return;
            if (e.IsMetronome)
            {
                _output.SendMetronome(e.DeviceId, status, data1, data2);
                if (Preferences.MetronomeBoost)
                    foreach (var layer in BoostLayers)
                        if (layer != data1) _output.SendMetronome(e.DeviceId, status, layer, data2);
            }
            else
            {
                _output.Send(e.DeviceId, status, data1, data2);
                if (e.TrackIndex >= 0 && (e.IsNoteOn || e.IsNoteOff)) NoteSounding(e);
            }
        }
        if (!_diagnostics) return;
        var wall = _diagnosticClock.Elapsed.TotalMilliseconds - _playbackStartWallMs;
        var latency = wall - (streamMs - startMs);
        var record = new DispatchRecord(streamMs, latency, e.TrackIndex, status, data1, data2, e.DeviceId);
        lock (_logGate) { if (_dispatchLog.Count < InputLimits.MaxDiagnosticRecords) _dispatchLog.Add(record); }
    }

    /// <summary>The latest persistent channel message per (device, status, controller) at <paramref name="timeMs"/>: the setup, overwritten by the mix points and other events up to then. The one definition of a track's effective level at a position.</summary>
    private static Dictionary<(int Device, int Status, int Data1), ScoreEvent> LatestChannelState(ScoreTimeline timeline, double startMs, double timeMs, int onlyTrack)
    {
        var latest = new Dictionary<(int Device, int Status, int Data1), ScoreEvent>();
        foreach (var e in timeline.ChannelSetup)
            if (IsChannelStateMessage(e) && (onlyTrack < 0 || e.TrackIndex == onlyTrack)) latest[ChannelStateKey(e)] = e;

        var events = timeline.Events;
        // The scan always starts at the timeline origin: a seek target or loop start must not hide earlier mix-table events.
        for (var i = FirstIndexAtOrAfter(events, Math.Min(startMs, timeline.PlayFromMs)); i < events.Count && events[i].TimeMs <= timeMs; i++)
        {
            var e = events[i];
            if (!e.IsSetup && IsChannelStateMessage(e) && (onlyTrack < 0 || e.TrackIndex == onlyTrack)) latest[ChannelStateKey(e)] = e;
        }
        return latest;
    }

    /// <summary>
    /// Reapply only the latest persistent channel messages at <paramref name="timeMs"/>. This avoids
    /// replaying a stale modulation ramp while ensuring a skipped reset cannot leave MIDI state stuck.
    /// </summary>
    internal void RestoreChannelStateAt(ScoreTimeline timeline, double startMs, double timeMs, int onlyTrack = -1)
    {
        var latest = LatestChannelState(timeline, startMs, timeMs, onlyTrack);

        lock (_outputGate)
        {
            var ordered = new List<ScoreEvent>(latest.Values);
            ordered.Sort(static (a, b) => a.TimeMs.CompareTo(b.TimeMs));
            foreach (var e in ordered)
                _output.Send(e.DeviceId, e.Status, ProgramFor(e.Status, e.Data1), MasterScaled(e.Status, e.Data1, e.Data2));
            SendKeyswitchesAt(timeline, startMs, timeMs, onlyTrack);   // the articulation keyswitch in force there (see PlaybackEngine.Articulations.cs)
        }
        if (Trace.IsOn(Trace.Playback)) Trace.Write(Trace.Playback, RestoreTrace.Describe(timeMs, latest.Values));
    }

    internal static bool IsPracticeSilencedState(ScoreEvent e) => (e.Status & 0xF0) switch
    {
        0xC0 or 0xE0 => true,
        0xB0 => e.Data1 is 64 or 7 or 11,
        _ => false
    };

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

    private void ReportPosition(double ms, ScoreTimeline timeline, int generation)
    {
        PlaybackPosition pos;
        lock (_gate)
        {
            // A stopped run (its thread may still be finishing) never reports into the next start.
            if (_pendingSeek is not null || generation != Volatile.Read(ref _generation)) return;
            Volatile.Write(ref _currentStreamMs, ms);
            pos = PlayheadMapper.Map(timeline, ms, CurrentBar, CurrentCell);
            CurrentBar = pos.Bar;
            CurrentCell = pos.Cell;
        }
        _onPosition?.Invoke(pos);
    }

    private bool IsGenerationCurrent(int generation)
    {
        lock (_gate) return _running && generation == Volatile.Read(ref _generation);
    }

    private bool CanScheduleWork(int generation)
    {
        lock (_gate) return _running && generation == Volatile.Read(ref _generation) && _pendingSeek is null;
    }

    private bool TryFinishGeneration(int generation)
    {
        lock (_gate)
        {
            if (!_running || generation != Volatile.Read(ref _generation) || _pendingSeek is not null ||
                (_arrangementRefreshRequested && !_arrangementRefreshLive)) return false;
            _running = false;
            return true;
        }
    }

    private bool TrySetSchedulerAnchor(int generation, double targetMs, double? clockRate = null)
    {
        lock (_gate)
        {
            if (!_running || generation != Volatile.Read(ref _generation) || _pendingSeek is not null) return false;
            _anchorMs = targetMs;
            _clock.Restart();
            if (clockRate is { } rate) _clockRate = rate;
            return true;
        }
    }

    private PlaybackPosition? CommitPendingSeekPosition(int generation, ScoreTimeline timeline, PendingPlaybackSeek request, double targetMs)
    {
        lock (_gate)
        {
            if (!_running || generation != Volatile.Read(ref _generation) ||
                (_pendingSeek is { } newer && newer.RequestId > request.RequestId)) return null;
            _anchorMs = targetMs;
            _currentStreamMs = targetMs;
            _startMs = targetMs;
            if (_paused) _clock.Reset(); else _clock.Restart();
            var position = PlayheadMapper.Map(timeline, targetMs, CurrentBar, CurrentCell);
            CurrentBar = position.Bar;
            CurrentCell = position.Cell;
            return position;
        }
    }

    /// <summary>Sleeps until shortly before the next event, or a short tick so the UI keeps updating.</summary>
    private static void SleepUntil(double nowMs, double nextEventMs, double rate = 1.0)
    {
        var wait = ((nextEventMs - DispatchLeadMs) - nowMs) / Math.Max(0.1, rate);
        if (wait <= 0.2) { Thread.Yield(); return; }
        if (wait > 10) { Thread.Sleep(4); return; }   // far off: short horizon-aware ticks so a loop/seek/refresh is noticed promptly
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

}
