using TabForge.Models;

namespace TabForge.Presenters;

// Owns: which view the score pane shows a track in (score or piano roll) from the track's saved choice (TrackModel.EditorView) and its content:
//       Auto shows MIDI tracks and MIDI takes in the piano roll and tab tracks in the score; and where the piano roll keeps a track's notes (its
//       MIDI clips, or its written tab).
// Does not own: the pane swap (the app's EditorPaneSwitch), the switch commands (the menus) or the note sources (PianoRollClipSource, PianoRollTabSource).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public static class PianoRollViewRules
{
    /// <summary>True when the track carries a MIDI clip (a MIDI take) on any lane.</summary>
    public static bool HasMidiClips(TrackModel track) => track.AudioClips.Any(clip => clip.IsMidi);

    /// <summary>True when any bar of the track holds a written note (either voice).</summary>
    public static bool HasWrittenNotes(TrackModel track)
    {
        foreach (var measure in track.Measures)
        {
            foreach (var cell in measure.Cells) if (cell.Notes.Count > 0) return true;
            foreach (var cell in measure.Voice2Cells) if (cell.Notes.Count > 0) return true;
        }
        return false;
    }

    /// <summary>True for a MIDI track: one that holds MIDI takes, or records from a MIDI input, and has no written notes (an audio track with an
    /// instrument counts when it holds MIDI takes).</summary>
    public static bool IsMidiTrack(TrackModel track) =>
        track.IsAudio ? HasMidiClips(track) : (HasMidiClips(track) || AudioInputs.IsMidi(track.AudioInput)) && !HasWrittenNotes(track);

    /// <summary>True when the piano roll edits the track's MIDI clips; false when it edits the written tab (the tab prompt applies).</summary>
    public static bool UsesClips(TrackModel track) => IsMidiTrack(track);

    /// <summary>True when the piano roll can show the track at all (an audio track needs a MIDI take).</summary>
    public static bool CanShow(TrackModel track) => !track.IsAudio || HasMidiClips(track);

    /// <summary>True when the score pane shows the track in the piano roll: the user's choice, else MIDI tracks and takes by default.</summary>
    public static bool ShowsPianoRoll(TrackModel track) => CanShow(track) && track.EditorView switch
    {
        TrackEditorView.PianoRoll => true,
        TrackEditorView.Score => false,
        _ => IsMidiTrack(track),
    };
}
