using System.Linq;
using TabForge.Models;
using TabForge.Services;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Playback;

/// <summary>Scheduler output and event helpers.</summary>
/// <remarks>Owns: event dispatch, channel restoration and output-side scheduler helpers. Does not own: transport state or timeline compilation. Tests: playback scheduling, loop and seek tests.</remarks>
public sealed partial class PlaybackEngine : IDisposable
{
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
                data2 = (int)Math.Round(127 * level / 100.0 * relativeVolume / 100.0);
                if (data2 <= 0) return;
                data2 = MetronomeVelocity(Math.Clamp(data2, 1, 127), status);
                data1 = note;
                _activeMetronomeNotes[e.MetronomePairId] = note;
            }
        }
        data2 = MasterScaled(status, data1, data2);
        data1 = ProgramFor(status, data1);
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
    internal void RestoreChannelStateAt(ScoreTimeline timeline, double startMs, double timeMs, int onlyTrack = -1)
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

        lock (_outputGate)
            foreach (var e in latest.Values.OrderBy(e => e.TimeMs))
                _output.Send(e.DeviceId, e.Status, ProgramFor(e.Status, e.Data1), MasterScaled(e.Status, e.Data1, e.Data2));
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

    private void ReportPosition(double ms, ScoreTimeline timeline)
    {
        PlaybackPosition pos;
        lock (_gate)
        {
            if (_pendingSeek is not null) return;
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
