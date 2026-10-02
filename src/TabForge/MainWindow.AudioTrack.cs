using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// MainWindow: adding an audio track (the Add-track lane's prompt, the + Track button's menu) and turning an audio track into an instrument track.
// The model work is TrackController (CreateTrack / ConvertAudioToInstrument); this is only the window's refresh around it.
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
        FinishTrackListChange(_project.Tracks.IndexOf(track));
        StatusText.Text = $"Added {track.Name} (audio track): drop audio or MIDI files on its lane";
    }

    /// <summary>Right-click an audio track > Convert to instrument track: pick the instrument (the Add track window), keep every clip.</summary>
    internal void ConvertAudioTrack(int index)
    {
        if (index < 0 || index >= _project.Tracks.Count || !_project.Tracks[index].IsAudio) return;
        var audio = _project.Tracks[index];
        var pick = _trackController.CreateTrack(_project, TrackKind.Guitar);
        pick.Name = audio.Name;
        if (!Views.TrackPropertiesWindow.ShowAdd(this, pick, new Views.TrackPropertiesWindow.AddTrackPlacement(_project.Tracks.Count, index))) return;
        ConvertAudioTrackTo(audio, InstrumentNaming.WithoutStringCount(pick.InstrumentName));
    }

    /// <summary>Converts <paramref name="audio"/> to an instrument track with the catalogue sound <paramref name="instrument"/> (one undo step).</summary>
    internal bool ConvertAudioTrackTo(TrackModel audio, string instrument)
    {
        if (!_trackController.ConvertAudioToInstrument(Doc, audio, instrument).Changed) { StatusText.Text = "Could not convert that track"; return false; }
        FinishTrackListChange(_project.Tracks.IndexOf(audio));
        Editor.InvalidateScoreLayout();
        StatusText.Text = $"{audio.Name} is now an instrument track ({audio.InstrumentName}); its clips moved to a lane below the tab lane";
        return true;
    }

    private void FinishTrackListChange(int selectIndex)
    {
        _midi.Rebuild(_project);
        RefreshTracks();
        TrackMixerGrid.SelectedIndex = selectIndex;
        RefreshArrangement();
        RefreshInstrument();
        SyncAudioEngine();
        ScheduleFitTimelineToTracks();
        UpdateTitle();
    }
}
