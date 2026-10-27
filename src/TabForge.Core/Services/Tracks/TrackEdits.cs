using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

// Owns: the whole-track edits the track row menus share: the unique name a pasted or duplicated track takes, and the
//   instrument-to-audio conversion (one undo step: the notation becomes a MIDI clip on the track).
// Does not own: the menus and prompts (each window), the clipboard (ClipboardService), the refresh of the track list after an edit.
// Tests: TabForge.Avalonia.Tests.MainWindowTrackRowMenuTests.
/// <summary>Whole-track edits shared by the track row menus.</summary>
public static class TrackEdits
{
    /// <summary>"Name copy", then "Name copy 2"...: the first name no track of the song has.</summary>
    public static string UniqueName(SongProject project, string name)
    {
        var baseName = name.Trim().Length == 0 ? "Track" : name.Trim();
        if (project.Tracks.All(t => !string.Equals(t.Name, baseName, StringComparison.Ordinal))) return baseName;
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? $"{baseName} copy" : $"{baseName} copy {n}";
            if (project.Tracks.All(t => !string.Equals(t.Name, candidate, StringComparison.Ordinal))) return candidate;
        }
    }

    /// <summary>
    /// Turns an instrument track into an audio track, one undo step: its notation becomes one MIDI clip (song time 0, on the first free
    /// lane; nothing is lost) and its bars are emptied (headers and bar count kept). Instrument, tuning and MIDI sound are cleared to the
    /// audio defaults; name, colour, mix, plug-in chain and the existing clips are kept. The track stays silent unless an instrument
    /// plug-in is on it. False for an audio track or one not in the song; nothing changes then.
    /// </summary>
    public static EditOutcome ConvertInstrumentToAudio(IEditTarget document, TrackModel track) =>
        document.Run(project =>
        {
            if (track.IsAudio || !project.Tracks.Contains(track)) return false;
            var clip = NotationToMidiClip.Build(project, track);
            if (clip is not null)
            {
                clip.Lane = ClipLanes.FreeLane(track, 0, clip.EndSec);
                ClipLanes.Ensure(track, clip.Lane + 1);
                track.AudioClips.Add(clip);
            }
            NotationToMidiClip.Empty(track);
            track.Kind = TrackKind.Audio;
            track.InstrumentName = "";
            track.MidiProgram = 0;
            track.MidiChannel = 0;
            track.NumberOfFrets = 0;
            track.StringTunings = new();
            track.MidiSound = false;
            track.AudioInput = AudioInputs.Input1;
            ClipLanes.Ensure(track, 1);
            track.Rig.Name = "Audio";
            track.Rig.ArticulationMap = "Generic Guitar";
            return true;
        });
}
