using TabForge.Avalonia.Transport;

namespace TabForge.Avalonia.Platform;

// Owns: the score's playing state while a session plays: the playhead's frame and state events, the playing-bar band, lit notes and playhead line
//   of the shown track (kept on the score probe's play state), and the repaint, the placement of the overlay and the follow that they need.
// Does not own: the playhead position (AvaloniaPlaybackSession, PlayheadClock), the drawing (Core ScoreRenderer), the scroll itself
//   (ScorePlayheadFollower, through the view's FollowPlayhead).
// Tests: TabForge.Avalonia.Tests.ScoreFollowContinuousPlayTests, TabForge.Avalonia.Tests.ScoreZoomAndHoverTests.
// Threads: UI thread only (the session raises its events on it).

/// <summary>Follows one playback session for the score view; the view binds and unbinds it through BindPlayback.</summary>
internal sealed class ScorePlayheadBinding
{
    private readonly ScorePreviewView _view;
    private AvaloniaPlaybackSession? _playback;   // the session the view follows, kept while the view is detached
    private bool _attached;                       // the subscriptions to _playback exist only while the view is attached
    private bool _subscribed;
    private double _lastPlayMs;

    internal ScorePlayheadBinding(ScorePreviewView view) => _view = view;

    /// <summary>Follows <paramref name="session"/> (null unbinds), and shows its current state at once.</summary>
    internal void Bind(AvaloniaPlaybackSession? session)
    {
        Unsubscribe();
        _playback = session;
        if (_attached) Subscribe();
        ShowPlayhead();
    }

    /// <summary>Subscribes on attach and releases the subscriptions on detach, so a detached score does no per-frame work.</summary>
    internal void SetAttached(bool attached)
    {
        if (_attached == attached) return;
        _attached = attached;
        if (!attached) { Unsubscribe(); return; }
        Subscribe();
        ShowPlayhead();
    }

    private void Subscribe()
    {
        if (_playback is null || _subscribed) return;
        _playback.PlayheadFrame += ShowPlayhead;
        _playback.StateChanged += ShowPlayhead;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _playback!.PlayheadFrame -= ShowPlayhead;
        _playback.StateChanged -= ShowPlayhead;
        _subscribed = false;
    }

    private void ShowPlayhead()
    {
        var play = _view.Probe.Play;
        var overlay = _view.PlayheadOverlay;
        var transport = _playback?.Transport;
        if (transport is null || !transport.IsPlaying || transport.IsPaused || transport.Timeline is null || transport.PlayheadBar < 0)
        {
            if (!play.Active) return;
            play.Clear();
            _view.InvalidateVisual();
            overlay.Place();
            return;
        }
        var position = _playback!.Playhead;
        var ms = position.ElapsedMs;
        if (!play.Active) _view.ResumeFollow();   // playback starts: a hand scroll no longer holds the follow back
        var moved = !play.Active || play.Measure != position.Bar || play.Cell != position.Cell || play.NeedsRepaint(_lastPlayMs, ms);
        _lastPlayMs = ms;
        if (!moved)
        {
            // Inside the beat only the line moves (a transform on the overlay); the page keeps its drawing.
            play.SetFraction(position.BarFraction);
            overlay.Place();
            _view.FollowPlayhead();
            return;
        }
        // The lit notes are the shown track's: the score's own track, not track 0.
        play.Set(transport.Timeline, _view.Probe.TrackIndex, position.Bar, position.Cell, position.BarFraction, ms);
        _view.InvalidateVisual();
        overlay.Place();
        _view.FollowPlayhead();
    }
}
