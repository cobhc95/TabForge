using System;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;

namespace TabForge.Avalonia.Features.Lyrics;

// Owns: placing the Lyrics pane in the dock and keeping it on the active tab's song.
// Does not own: the lyrics text and its edit (LyricsSongPane), the per-bar editor (LyricsByBarWindow), the tab strip (MainWindowTabs), the playback sessions.
// Tests: TabForge.Avalonia.Tests.LyricsSongPaneTests.
public static class LyricsPaneFlow
{
    internal static WindowFeatureModule Module { get; } = new(RegisterPanes: Register);

    private static void Register(DockPaneSetup setup, Func<SongDocument> active,
        Func<AvaloniaPlaybackSession?> playback, Action<Action> observeActive)
    {
        var pane = new LyricsSongPane(playback);
        void Reload() => pane.Show(active());
        observeActive(Reload);
        Reload();
        setup.RegisterShared("lyrics", pane);
    }
}
