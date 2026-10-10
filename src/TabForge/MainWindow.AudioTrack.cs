using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// MainWindow: adding an audio track (the Add-track lane's prompt, the + Track button's menu) and turning an audio track into an instrument track.
// The model work is TrackController (CreateTrack / ConvertAudioToInstrument); this is only the window's refresh around it.
// Owns: adding an audio track from the Add-track lane or the + Track menu, and turning an audio track into an instrument track, as the window's refresh around them.
// Does not own: the track work itself (TrackController).
// Tests: listed in docs/feature-map/mixer-and-audio-engine.md.
public partial class MainWindow
{
    private void HookAddTrackLane()
    {
        Arrangement.AddTrackLaneClicked += AddTrackFromPrompt;
        Arrangement.ConvertAudioTrackRequested += ConvertAudioTrack;
        HookTrackRowMenu();
    }

    /// <summary>View > Show or hide the Add-track lane (the Preferences setting).</summary>
    private void ToggleAddTrackLane()
    {
        var timeline = _settings.Timeline;
        timeline.ShowAddTrackLane = !timeline.ShowAddTrackLane;
        Arrangement.ShowAddTrackLane = timeline.ShowAddTrackLane;
        ScheduleFitTimelineToTracks();
        SaveSettings();
        StatusText.Text = timeline.ShowAddTrackLane ? "Add-track lane shown" : "Add-track lane hidden";
    }

    /// <summary>The Add-track lane: Audio or Instrument first; an instrument goes on to the Add track window.</summary>
    internal void AddTrackFromPrompt()
    {
        switch (Views.AddTrackPrompt.Ask(this))
        {
            case Views.AddTrackChoice.Audio: AddAudioTrack(); break;
            case Views.AddTrackChoice.Instrument: AddTrackWithWindow(); break;
        }
    }

    /// <summary>Adds an empty audio track at the end (no dialog), selects it and says so.</summary>
    internal void AddAudioTrack()
    {
        var added = _trackController.AddAudioTrack(Doc);
        if (added.Value is not { } track) return;
        // An empty audio track at the end adds no notes and moves no track: playback keeps running without a rebuild (a rebuild restarts it).
        FinishTrackListChange(_project.Tracks.IndexOf(track), rebuildPlayback: false);
        StatusText.Text = $"Added {track.Name} (audio track): drop audio or MIDI files on its lane";
    }

    /// <summary>Right-click an audio track > Convert to instrument track: the instrument picker (search focused), then what to do with its clips; one undo step.</summary>
    internal void ConvertAudioTrack(int index)
    {
        if (index < 0 || index >= _project.Tracks.Count || !_project.Tracks[index].IsAudio) return;
        var audio = _project.Tracks[index];
        var result = new Controllers.ConvertToInstrumentFlow(_trackController, _settings.Editing).Run(this, Doc, audio, sec => Doc.Playback.Clock.BarAt(_project, sec), SaveSettings);
        if (result is null) return;
        if (!result.Value.Changed) { StatusText.Text = "Could not convert that track"; return; }
        FinishTrackListChange(_project.Tracks.IndexOf(audio));
        Editor.InvalidateScoreLayout();
        StatusText.Text = $"{audio.Name} is now an instrument track ({audio.InstrumentName}); its clips moved to a lane below the tab lane";
    }

    private void FinishTrackListChange(int selectIndex, bool rebuildPlayback = true)
    {
        if (rebuildPlayback) _midi.Rebuild(_project);
        RefreshTracks();
        TrackMixerGrid.SelectedIndex = selectIndex;
        _trackSwitchSync?.Cancel();
        RefreshArrangement();
        RefreshInstrument();
        _engineSync.SyncOrSchedule(Doc, deferred: !Doc.Playback.Engine.IsPlaying);
        ScheduleFitTimelineToTracks();
        UpdateTitle();
    }
}
