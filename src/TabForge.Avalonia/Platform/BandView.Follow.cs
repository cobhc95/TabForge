using System.Linq;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Band view's playhead follow per frame: sliding or gliding every shown card's window of systems to the followed bar. Split from BandView.cs.
// Does not own: which bar is followed (BandPlayheadFollow), the window itself (BandTrackPanel) or the fit (BandCardFit).
// Tests: TabForge.Avalonia.Tests (Band view follow tests).
public sealed partial class BandView
{
    // Runs on each playback frame: the cards slide only when the followed bar changes, so a frame in the same bar does no layout.
    private void FollowPlayhead()
    {
        var settings = FollowSource();
        if (BandPlayheadFollow.PlayingBar(_followedSession, settings) is not { } bar)
        {
            // Playback stopped mid-glide: the cards land on the last followed bar instead of freezing part-way.
            if (settings.FollowPlayback && settings.Mode == FollowModes.Smooth && _followedSession is { IsPlaying: false }) SlideCards();
            return;
        }
        var smooth = settings.Mode == FollowModes.Smooth;
        // Smooth keeps gliding on every frame until the card lands; page-by-page does nothing in the same bar.
        if (bar == _followedBar) { if (smooth) GlideCards(); return; }
        _followedBar = bar;
        if (smooth) GlideCards(); else SlideCards();
        // A window with more unused top above its tab gives room, one with less takes it: the cards fit again only when they now overflow.
        if (_current.Shown.Sum(index => _current.Panels[index].Bounds.Height) + (_current.Shown.Count - 1) * CardGap > Bounds.Height) FitCards();
    }

    /// <summary>Slides every shown card's window of systems to the followed bar's system.</summary>
    private void SlideCards()
    {
        foreach (var index in _current.Shown) _current.Panels[index].ShowSystemsAround(_followedBar);
    }

    // Smooth follow: every shown card glides a step toward the followed bar's system.
    private void GlideCards()
    {
        foreach (var index in _current.Shown) _current.Panels[index].GlideSystemsTo(_followedBar);
    }
}
