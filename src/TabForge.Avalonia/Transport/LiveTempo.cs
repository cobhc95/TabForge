using System;
using System.Diagnostics;
using TabForge.Documents;
using TabForge.Playback;

namespace TabForge.Avalonia.Transport;

// Owns: the tempo sounding at the shown song's playhead (PlaybackTempo on the playing timeline), and the throttled Changed event the tempo readouts
//     (status bar, View and tempo bar) refresh on: only when the value changed, at most once per interval while playing, at once on a seek, stop or tab switch.
// Does not own: the tempo math (PlaybackTempo), the readouts' texts and layout (their views), the sessions (AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.LiveTempoTests.
// Threads: UI thread only. No allocation per frame.
/// <summary>Follows the active tab's playback and raises <see cref="Changed"/> when the live tempo changes.</summary>
public sealed class LiveTempo : IDisposable
{
    private readonly Func<SongDocument> _active;
    private readonly AvaloniaPlaybackSessions _sessions;
    private readonly long _minTicks;
    private AvaloniaPlaybackSession? _watched;
    private int _last;      // the tempo last announced while playing; 0 when none
    private long _stamp;

    /// <param name="minIntervalMs">The shortest time between two changes announced while a song plays (a bar-by-bar ramp changes the value every frame otherwise).</param>
    public LiveTempo(Func<SongDocument> active, AvaloniaPlaybackSessions sessions, int minIntervalMs = 200)
    {
        _active = active;
        _sessions = sessions;
        _minTicks = minIntervalMs * Stopwatch.Frequency / 1000;
        sessions.AnyStateChanged += Rebind;
        Rebind();
    }

    /// <summary>Raised when the shown value of the readouts may differ now; the handler reads <see cref="Playing"/>.</summary>
    public event Action? Changed;

    /// <summary>The tempo at the playing position of the active tab's song, or null when it is not playing (the readout then shows the cursor's tempo).</summary>
    public int? Playing
    {
        get
        {
            var session = _watched;
            if (session is not { IsPlaying: true }) return null;
            var transport = session.Transport;
            if (transport.PlayheadBar < 0 || transport.StartPending || transport.Timeline is not { } timeline)
                return _last > 0 ? _last : null;   // the start or a seek is still compiling: the value shown stays
            return PlaybackTempo.AtTime(_active().Project, timeline, transport.PlayheadMs);
        }
    }

    /// <summary>Follows the active tab's session (the tab switched, or a session started or stopped).</summary>
    public void Rebind()
    {
        var session = _sessions.Existing(_active());
        if (!ReferenceEquals(session, _watched))
        {
            Unwatch();
            _watched = session;
            if (session is not null)
            {
                session.PlayheadFrame += OnFrame;
                session.PlayheadBarChanged += OnJump;
            }
        }
        _last = Playing ?? 0;
        Changed?.Invoke();
    }

    private void OnFrame()
    {
        var now = Playing ?? 0;
        if (now == _last) return;
        var stamp = Stopwatch.GetTimestamp();
        if (stamp - _stamp < _minTicks) return;   // a later frame announces it
        _stamp = stamp;
        _last = now;
        Changed?.Invoke();
    }

    private void OnJump()
    {
        var now = Playing ?? 0;
        if (now == _last) return;   // the same value: nothing to redraw
        _last = now;
        Changed?.Invoke();
    }

    private void Unwatch()
    {
        if (_watched is null) return;
        _watched.PlayheadFrame -= OnFrame;
        _watched.PlayheadBarChanged -= OnJump;
        _watched = null;
    }

    public void Dispose()
    {
        _sessions.AnyStateChanged -= Rebind;
        Unwatch();
    }
}
