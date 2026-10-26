using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the MIDI listener, the run calibration, the wait gate and the practice silence of the Learn engine (partial of KeyboardLearnEngine).
// Tests: TabForge.Avalonia.Tests.KeyboardLearnEngineTests.

public sealed partial class KeyboardLearnEngine : IDisposable
{
    // ---- listener (the MIDI listener, minus the Thru play path) ----

    private void EnsureListener(bool listen)
    {
        if (!listen || _hubProvider?.Invoke() is not { } hub)
        {
            if (!listen) StopListening(clearPractice: false);
            return;
        }
        var want = KeyboardModeSettings.NormalizeMidiInput(Learn?.MidiInput);
        var device = want == KeyboardModeSettings.MidiAny ? null : want == KeyboardModeSettings.MidiOff ? null : want;
        if (want == KeyboardModeSettings.MidiOff)
        {
            StopListening(clearPractice: false);
            return;
        }
        if (_midi is null)
        {
            _midi = hub.CreateClient();
            _midi.Message += OnMidiMessage;
            if (_midi.Open() == 0)
            {
                _midi.Message -= OnMidiMessage;
                _midi.Dispose();
                _midi = null;
                return;
            }
            _rescanAt = Now();
        }
        _midi.Device = device;
    }

    private void StopListening(bool clearPractice)
    {
        if (_midi is null)
        {
            if (clearPractice) ApplyPracticeSilence(-1);
            return;
        }
        _midi.Message -= OnMidiMessage;
        _midi.Dispose();
        _midi = null;
        while (_queue.TryDequeue(out _)) { }
        Volatile.Write(ref _queued, 0);
        if (clearPractice) ApplyPracticeSilence(-1);
    }

    private void OnMidiMessage(int status, int data1, int data2, long stamp)
    {
        var type = status & 0xF0;
        if (type is not (0x80 or 0x90)) return;
        var session = _session;
        var live = session is { IsPlaying: true, IsPaused: false };
        var sec = live ? session!.Transport.PlayheadMs / 1000.0 : double.NaN;
        var unstamped = double.IsNaN(sec);
        if (unstamped || Volatile.Read(ref _queued) >= MaxQueued) { if (!unstamped) return; }
        if (unstamped && Volatile.Read(ref _queued) >= MaxQueued) return;
        Interlocked.Increment(ref _queued);
        _queue.Enqueue(new KeyboardModePlayed(data1, type == 0x90 && data2 > 0, unstamped ? double.NaN : KeyboardModeJudge.Compensate(sec, _latencySec?.Invoke() ?? 0)));
    }

    private void Drain(Action<KeyboardModePlayed> sink)
    {
        while (_queue.TryDequeue(out var e)) { Interlocked.Decrement(ref _queued); sink(e); }
    }

    /// <summary>A self-test / computer-keyboard press: queued with a known song time, drained on the next frame.</summary>
    public void PressKey(int midi, bool on, double timeSec)
    {
        if (Volatile.Read(ref _queued) >= MaxQueued) return;
        Interlocked.Increment(ref _queued);
        _queue.Enqueue(new KeyboardModePlayed(midi, on, timeSec));
    }

    // ---- run (the keyboard session, with the bias calibration) ----

    private void StartRun(double songSec, KeyboardModeLoop? loop)
    {
        if (_source is null) return;
        var hands = KeyboardHands.Parse(Learn?.Hands);
        var tol = KeyboardModeTolerances.Of(Learn?.TimingTolerance);
        var from = (loop is { IsUsable: true } l && songSec * 1000 < l.StartMs ? l.StartMs : songSec * 1000) / 1000 - StartSlackSec;
        var to = loop is { IsUsable: true } l2 ? l2.EndMs / 1000 : double.MaxValue;
        Judge = new KeyboardModeJudge(_source.Expected(hands).Where(x => x.OnsetSec >= from && x.OnsetSec < to), tol);
        _handsOfJudge = hands;
        _tolOfJudge = tol;
        Score.Reset();
        Feedback.Reset();
        Drain(_ => { });
        Runs++;
    }

    private KeyboardModePlayed Calibrated(KeyboardModePlayed e)
    {
        if (!e.On || double.IsNaN(e.TimeSec) || Judge is null) return e;
        var raw = Judge.NearestOffsetMs(new KeyboardModePlayed(e.Midi, true, e.TimeSec + _biasSec));
        if (!double.IsNaN(raw))
        {
            _offsets.Enqueue(raw + _biasSec * 1000);
            while (_offsets.Count > OffsetWindow) _offsets.Dequeue();
            _biasSec = BiasFrom(_offsets);
        }
        return e with { TimeSec = e.TimeSec - _biasSec };
    }

    private static double BiasFrom(Queue<double> offsets)
    {
        if (offsets.Count < MinOffsets) return 0;
        var sorted = offsets.OrderBy(x => x).ToArray();
        var median = sorted[sorted.Length / 2];
        var spread = sorted[sorted.Length - 1 - sorted.Length / 4] - sorted[sorted.Length / 4];
        return spread <= MaxSpreadMs && Math.Abs(median) >= MinBiasMs ? Math.Clamp(median, -MaxBiasMs, MaxBiasMs) / 1000 : 0;
    }

    // ---- wait gate ----

    private void Press(KeyboardModePlayed e)
    {
        if (e.On) _latest[e.Midi] = (++_seq, Now());
    }

    private void StepWait(KeyboardModeJudge judge, double songSec, bool running)
    {
        if (!ReferenceEquals(judge, _waitJudge)) { RearmWait(); _waitJudge = judge; _waitYielded = -1; }
        if (!running) { DropWait(); return; }
        if (_waiting)
        {
            if (HostIsPlaying) { _waitYielded = _waitGroup; _waiting = false; return; }
            if (Satisfied(judge)) FinishWait(resolveNotes: true);
            return;
        }
        while (_waitGroup < judge.GroupStarts.Count && (judge.GroupState(_waitGroup).Complete
            || (_waitCatchUp && judge.Results[judge.GroupStarts[_waitGroup]].Expected.OnsetSec < songSec - CatchUpSec))) _waitGroup++;
        if (_waitGroup >= judge.GroupStarts.Count || _waitGroup == _waitYielded || !HostIsPlaying) return;
        _waitCatchUp = false;
        if (songSec < judge.Results[judge.GroupStarts[_waitGroup]].Expected.OnsetSec) return;
        _waitStartTick = Now();
        _waiting = true;
        Pauses++;
        _waitPausedOwn = true;
        _session?.PlayOrPause();
        RefreshAwaited();
    }

    private bool Satisfied(KeyboardModeJudge judge)
    {
        for (var i = judge.GroupStarts[_waitGroup]; i < GroupEnd(judge); i++)
            if (!judge.Results[i].IsResolved && !Pressed(judge.Results[i].Expected.Midi)) return false;
        return true;
    }

    private bool Pressed(int midi) =>
        _latest.TryGetValue(midi, out var p) && p.Seq > _used.GetValueOrDefault(midi) && p.Tick >= _waitStartTick - PreWindowMs;

    private void FinishWait(bool resolveNotes)
    {
        var judge = _waitJudge;
        if (judge is not null)
        {
            if (resolveNotes)
                for (var i = judge.GroupStarts[_waitGroup]; i < GroupEnd(judge); i++)
                    if (!judge.Results[i].IsResolved) judge.Feed(new KeyboardModePlayed(judge.Results[i].Expected.Midi, true, judge.Results[i].Expected.OnsetSec));
            for (var i = judge.GroupStarts[_waitGroup]; i < GroupEnd(judge); i++)
                if (_latest.TryGetValue(judge.Results[i].Expected.Midi, out var p)) _used[judge.Results[i].Expected.Midi] = p.Seq;
        }
        _waiting = false;
        Awaited = default;
        _waitGroup++;
        if (_waitPausedOwn)
        {
            _waitPausedOwn = false;
            Resumes++;
            if (_session is { IsPlaying: true, IsPaused: true }) _session.PlayOrPause();
        }
    }

    private int GroupEnd(KeyboardModeJudge judge) =>
        _waitGroup + 1 < judge.GroupStarts.Count ? judge.GroupStarts[_waitGroup + 1] : judge.Results.Count;

    private void RefreshAwaited()
    {
        var mask = default(KeyMask);
        if (_waiting && _waitJudge is { } judge)
            for (var i = judge.GroupStarts[_waitGroup]; i < GroupEnd(judge); i++)
                if (!judge.Results[i].IsResolved) mask.Set(judge.Results[i].Expected.Midi);
        Awaited = mask;
    }

    /// <summary>True while the awaited chord still lacks this key.</summary>
    public bool Awaits(int midi)
    {
        if (!_waiting || _waitJudge is null) return false;
        for (var i = _waitJudge.GroupStarts[_waitGroup]; i < GroupEnd(_waitJudge); i++)
            if (!_waitJudge.Results[i].IsResolved && _waitJudge.Results[i].Expected.Midi == midi) return true;
        return false;
    }

    private void RearmWait()
    {
        var held = _waiting && _waitPausedOwn;
        DropWait();
        if (held)
        {
            _waitPausedOwn = false;
            Resumes++;
            if (_session is { IsPlaying: true, IsPaused: true }) _session.PlayOrPause();
        }
    }

    private void DropWait()
    {
        _waiting = false;
        Awaited = default;
        _waitGroup = 0;
        _waitJudge = null;
        _waitPausedOwn = false;
    }
}
