using System.Collections.Generic;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia.Playback;

// Owns: the playback of the other tabs when the person switches to another tab, from the PlaybackOnTabSwitch setting: continue, pause or stop it.
// Adding a tab (new, open beside, duplicate) runs no policy: the playing song is never disturbed by opening another one.
// Does not own: the playback sessions (AvaloniaPlaybackSessions), the setting itself (TabSettings), the tab order (MainWindowTabs).
// Threads: UI thread only.
public static class TabPlaybackSwitchPolicy
{
    /// <summary>Pauses or stops every other song that is playing, when the setting says so; the shown song keeps playing.</summary>
    public static void Apply(AvaloniaPlaybackSessions? playback, IEnumerable<SongDocument> songs, SongDocument shown, string preference)
    {
        if (playback is null) return;
        var action = TabPlaybackActions.Resolve(preference);
        if (action == TabPlaybackAction.Continue) return;
        foreach (var song in songs)
        {
            if (ReferenceEquals(song, shown)) continue;
            var session = playback.Existing(song);
            if (session is null || !session.IsPlaying || session.IsPaused) continue;
            if (action == TabPlaybackAction.Pause) session.PlayOrPause();
            else session.Stop();
        }
    }
}
