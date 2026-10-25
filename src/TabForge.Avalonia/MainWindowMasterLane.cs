using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Master row's edits in the arrangement, on the shown song. The FX part opens the master effects chain (as the mixer's Master row does),
//   the power part bypasses it as one undo step (MixerBuses.SetOn), the pan slider sets the song's master pan as one undo step and pushes the live
//   mix, and the volume slider sets the transport's master knob, which saves the audio settings and reaches the engines (MainWindowEngineSync).
// Does not own: the row's drawing and hit tests (TrackHeaderColumn, MasterLaneRowPainter), the drag gesture (TimelineInputController), the chain
//   window (FxChainWindows), the master volume's storage (AudioSettings). The master volume is not an undo step: it is the transport knob's value.
// Tests: TabForge.Avalonia.Tests.MainWindowMasterLaneTests.
public static class MainWindowMasterLane
{
    /// <summary>Routes the master row's requests and slider releases of one arrangement to the shown song.</summary>
    /// <param name="songs">The tabs; the cells act on the shown song.</param>
    /// <param name="owner">The window that owns the opened chain window.</param>
    /// <param name="playback">The playback sessions the pan and the power reach; null (no engine host) edits the song only.</param>
    /// <param name="plugins">The plug-in settings the power switch's engine sync uses.</param>
    public static void Attach(ArrangementView arrangement, MainWindowTabs songs, Window owner, AvaloniaPlaybackSessions? playback, Func<PluginSettings> plugins)
    {
        arrangement.Input.TrackCellRequested += (row, cell) =>
        {
            if (row != TrackHeaderColumn.MasterRow) return;
            var song = songs.Active;
            if (cell == TrackRowCells.Cell.Fx) MainWindowTrackFxKeys.OpenChain(MixerBuses.MasterTrack(song.Project), owner);
            else if (cell == TrackRowCells.Cell.FxPower) TogglePower(song, arrangement, playback, plugins());
        };
        arrangement.Input.MasterSliderReleased += (cell, value) =>
        {
            if (cell == TrackRowCells.Cell.Volume) arrangement.Transport.MasterVolumeKnob.Value = value;
            else SetPan(songs.Active, value, playback);
        };
    }

    /// <summary>The master chain's power as one undo step; the engine takes the new chain at once, and the cell redraws from the song.</summary>
    private static void TogglePower(SongDocument song, ArrangementView arrangement, AvaloniaPlaybackSessions? playback, PluginSettings plugins)
    {
        DocumentEdits.Run(song, project =>
        {
            MixerBuses.SetOn(project.Mixer.Master, !project.Mixer.Master.On);
            return true;
        }, invalidatesTimeline: false);
        playback?.SyncEngine(song, plugins);
        arrangement.Header.InvalidateVisual();
    }

    /// <summary>The master pan as one undo step, then the live mix; a release at the value it already has changes nothing.</summary>
    private static void SetPan(SongDocument song, int pan, AvaloniaPlaybackSessions? playback)
    {
        var changed = DocumentEdits.Run(song, project =>
        {
            if (project.Mixer.MasterPan == pan) return false;
            project.Mixer.MasterPan = pan;
            return true;
        }, invalidatesTimeline: false, label: "Master pan").Changed;
        if (changed) playback?.PushLiveMix(song);
    }
}
