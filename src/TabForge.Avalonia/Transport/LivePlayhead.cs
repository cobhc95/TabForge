using System;
using System.Diagnostics;
using TabForge.Documents;
using TabForge.Playback;

namespace TabForge.Avalonia.Transport;

/// <summary>What the active song's transport is doing, for the status bar's state word.</summary>
public enum TransportPhase { Ready, Stopped, Playing, Paused, CountIn }

// Owns: the active song's transport phase and, while it plays, the playhead's bar, cell, performed time and tempo, and the throttled Changed event the
//     status bar refreshes on: at once when the phase, the tab or the session changes, else at most once per interval, and only when the shown bar, cell, tenth of a second or tempo moved.
// Does not own: the playhead clock (PlayheadClock), the performed-time mapping (the session), the texts (StatusTextPresenter), the sessions (AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.StatusBarTransportTests.
// Threads: UI thread only. Allocates nothing per frame.
/// <summary>Follows the active tab's playback frames for the status bar.</summary>
public sealed class LivePlayhead : IDisposable
{
    private readonly Func<SongDocument> _active;
    private readonly AvaloniaPlaybackSessions _sessions;
    private readonly Func<double> _nowMs;
    private readonly double _minMs;
    private AvaloniaPlaybackSession? _watched;
    private double _stamp = double.NegativeInfinity;
    private (int Bar, int Cell, long Tenths, int Tempo) _shown;

    /// <param name="minIntervalMs">The shortest time between two changes announced while a song plays (8 a second by default).</param>
    /// <param name="nowMs">The wall clock in milliseconds; null uses a stopwatch (tests pass a simulated one).</param>
    public LivePlayhead(Func<SongDocument> active, AvaloniaPlaybackSessions sessions, int minIntervalMs = 125, Func<double>? nowMs = null)
    {
        _active = active;
        _sessions = sessions;
        _minMs = minIntervalMs;
        var watch = Stopwatch.StartNew();
        _nowMs = nowMs ?? (() => watch.Elapsed.TotalMilliseconds);
        sessions.AnyStateChanged += Rebind;
        Rebind();
    }

    /// <summary>Raised when the shown state or position may differ now; the handler reads the properties.</summary>
    public event Action? Changed;

    /// <summary>The transport phase of the active song.</summary>
    public TransportPhase Phase { get; private set; }

    /// <summary>True while the song plays with its start compiled: <see cref="Bar"/>, <see cref="Cell"/>, <see cref="Sec"/> and <see cref="Tempo"/> then hold the playhead.</summary>
    public bool Following { get; private set; }

    /// <summary>The playhead's written bar (zero based).</summary>
    public int Bar { get; private set; }

    /// <summary>The playhead's cell in that bar (zero based).</summary>
    public int Cell { get; private set; }

    /// <summary>The playhead's performed time in seconds (repeats and jumps applied).</summary>
    public double Sec { get; private set; }

    /// <summary>The tempo sounding at the playhead.</summary>
    public int Tempo { get; private set; }

    /// <summary>Follows the active tab's session (the tab switched, or a session started, paused or stopped); announces at once.</summary>
    public void Rebind()
    {
        var session = _sessions.Existing(_active());
        if (!ReferenceEquals(session, _watched))
        {
            Unwatch();
            _watched = session;
            if (session is not null) session.PlayheadFrame += OnFrame;
        }
        Read(announceAlways: true);
    }

    private void OnFrame() => Read(announceAlways: false);

    private void Read(bool announceAlways)
    {
        var session = _watched;
        var phase = PhaseOf(session);
        var following = phase is TransportPhase.Playing && session!.Transport is { StartPending: false, PlayheadBar: >= 0, Timeline: not null };
        var phaseChanged = phase != Phase || following != Following;
        Phase = phase;
        Following = following;
        if (!following)
        {
            if (announceAlways || phaseChanged) Changed?.Invoke();
            return;
        }
        var position = session!.Playhead;
        var tempo = PlaybackTempo.AtTime(_active().Project, session.Transport.Timeline!, position.ElapsedMs);
        var sec = session.PlayheadSongSec;
        var key = (position.Bar, position.Cell, (long)(sec * 10), tempo);
        (Bar, Cell, Sec, Tempo) = (position.Bar, position.Cell, sec, tempo);
        if (!announceAlways && !phaseChanged)
        {
            if (key == _shown) return;   // nothing the status shows moved
            var now = _nowMs();
            if (now - _stamp < _minMs) return;   // a later frame announces it
            _stamp = now;
        }
        else _stamp = _nowMs();
        _shown = key;
        Changed?.Invoke();
    }

    private static TransportPhase PhaseOf(AvaloniaPlaybackSession? session) =>
        session is null ? TransportPhase.Ready
        : !session.IsPlaying ? (session.HasPlayed ? TransportPhase.Stopped : TransportPhase.Ready)
        : session.IsPaused ? TransportPhase.Paused
        : session.InCountIn ? TransportPhase.CountIn
        : TransportPhase.Playing;

    private void Unwatch()
    {
        if (_watched is null) return;
        _watched.PlayheadFrame -= OnFrame;
        _watched = null;
    }

    public void Dispose()
    {
        _sessions.AnyStateChanged -= Rebind;
        Unwatch();
    }
}
