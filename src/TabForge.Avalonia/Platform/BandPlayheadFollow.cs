using TabForge.Avalonia.Transport;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: which bar the Band view's cards follow while the song plays: the session's playing bar (the same position every card's playhead draws),
//   unless the score's follow setting is off. Read from the session, not from a card, so the order the view and its cards hear a frame in does
//   not matter. The cards share their bars, so that bar's system is the same one in every card, and every card slides its window to it.
// Does not own: the cards' windows (BandTrackPanel.ShowSystemsAround), each card's playhead line (its staff), the follow settings (passed in).
// Tests: TabForge.Avalonia.Tests.BandPlayheadFollowTests.
// Threads: UI thread only.
internal static class BandPlayheadFollow
{
    /// <summary>The bar the cards follow now, or null when follow is off or the session is not playing.</summary>
    public static int? PlayingBar(AvaloniaPlaybackSession? session, FollowSettings settings)
    {
        if (!settings.FollowPlayback || session is not { IsPlaying: true }) return null;
        return session.Playhead.Bar;
    }
}
