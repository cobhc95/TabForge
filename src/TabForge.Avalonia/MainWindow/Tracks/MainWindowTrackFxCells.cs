using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the FX cells of the arrangement's track rows. The FX part opens that row's track FX chain window (MainWindowTrackFxKeys.OpenChain,
//   as the FX split button's OpenChain does). The power part switches the row's track between its plug-in chain and Windows MIDI as one undoable
// step, then syncs the engine and repaints the header cell, which is drawn from the track's sound source.
// Does not own: the windows (FxChainWindow), the cell geometry and art (TrackRowCells, TrackHeaderColumn), the engine sync (AvaloniaPlaybackSessions),
//   the mixer's FX buttons (MixerWindow), the bypass of single plug-ins (FxChainWindow).
// Tests: TabForge.Avalonia.Tests.MainWindowTrackFxCellsTests.
public static class MainWindowTrackFxCells
{
    /// <param name="panes">The document panes; each new arrangement's FX cells are attached.</param>
    /// <param name="songs">The tabs; the cells act on the shown song.</param>
    /// <param name="owner">The window that owns the opened chain windows.</param>
    /// <param name="playback">The playback sessions the power switch syncs; null (no engine host) makes the switch edit the song only.</param>
    /// <param name="plugins">The plug-in settings the engine sync uses.</param>
    public static void Register(MainWindowDocumentPanes panes, MainWindowTabs songs, Window owner, AvaloniaPlaybackSessions? playback, Func<PluginSettings> plugins, FxChainWindows? windows = null)
    {
        if (windows is not null)
        {
            if (panes.Arrangement is { } shown) windows.TrackListChanged = () => shown.Header.InvalidateVisual();
            panes.ArrangementCreated += arrangement => windows.TrackListChanged = () => arrangement.Header.InvalidateVisual();
        }
        // The dock builds the first arrangement before the window registers this, so that one is attached here too.
        if (panes.Arrangement is { } existing) Attach(existing, songs, owner, playback, plugins);
        panes.ArrangementCreated += arrangement => Attach(arrangement, songs, owner, playback, plugins);
    }

    /// <summary>Routes the FX and power cell requests of one arrangement to the shown song's track of that row; the master row's cells go to <see cref="MainWindowMasterLane"/>.</summary>
    public static void Attach(ArrangementView arrangement, MainWindowTabs songs, Window owner, AvaloniaPlaybackSessions? playback, Func<PluginSettings> plugins)
    {
        MainWindowMasterLane.Attach(arrangement, songs, owner, playback, plugins);
        arrangement.Input.TrackCellRequested += (row, cell) =>
        {
            var song = songs.Active;
            if (row < 0 || row >= song.Project.Tracks.Count) return;
            if (cell == TrackRowCells.Cell.Fx) MainWindowTrackFxKeys.OpenChain(song.Project.Tracks[row], owner);
            else if (cell == TrackRowCells.Cell.FxPower) TogglePower(song, row, arrangement, playback, plugins(), owner);
        };
    }

    private static void TogglePower(SongDocument song, int row, ArrangementView arrangement, AvaloniaPlaybackSessions? playback, PluginSettings plugins, Window owner)
    {
        DocumentEdits.Run(song, project =>
        {
            var track = project.Tracks[row];
            track.SoundSource = track.SoundSource == SoundSources.Plugins ? SoundSources.Midi : SoundSources.Plugins;
            return true;
        }, invalidatesTimeline: false);
        playback?.SyncEngine(song, plugins);
        arrangement.Header.InvalidateVisual();
        FxChainWindows.For(owner)?.Refresh(song.Project.Tracks[row]);
    }
}
