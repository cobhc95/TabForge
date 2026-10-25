using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Docking;

namespace TabForge.Avalonia.Lyrics;

// Owns: placing the Lyrics pane in the dock and keeping it on the active tab's song.
// Does not own: the lyrics text and its edit (LyricsSongPane), the per-bar editor (LyricsByBarWindow), the tab strip (MainWindowTabs), the playback sessions.
// Tests: TabForge.Avalonia.Tests.LyricsSongPaneTests.
public static class LyricsPaneFlow
{
    /// <summary>Registers the Lyrics pane as a shared control and follows the active tab from now on.</summary>
    public static LyricsSongPane Register(DockPaneSetup setup, MainWindowTabs songs, AvaloniaPlaybackSessions? playback)
    {
        var pane = new LyricsSongPane(() => playback?.For(songs.Active));
        songs.ActiveChanged += () => pane.Show(songs.Active);
        pane.Show(songs.Active);
        setup.RegisterShared("lyrics", pane);
        return pane;
    }
}
