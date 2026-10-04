using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Services;

// Owns: turning one instrument track's notation into a single MIDI clip (the playback compiler's notes, in song time) and emptying the notation.
// Does not own: the track-kind change and undo (TrackController.ConvertInstrumentToAudio) or writing a clip back (MidiClipToTab).
// Tests: TestInstrumentToAudioConversion.
public static class NotationToMidiClip
{
    /// <summary>
    /// One MIDI clip at song time 0 holding every note the track plays (repeats included), or null when it plays none.
    /// Pitches are stored without the track's transposition, which the clip's own playback adds again.
    /// </summary>
    public static AudioClip? Build(SongProject project, TrackModel track)
    {
        var index = project.Tracks.IndexOf(track);
        if (index < 0) return null;
        var timeline = new ScoreToMidiCompiler(project, new PlaybackOptions { RespectMuteSolo = false, SkipClips = true }).Build();
        var notes = timeline.NotesFor(index)
            .Select(n => new ClipNote(n.OnsetMs / 1000.0, Math.Max(0.01, n.DurationMs / 1000.0), Math.Clamp(n.Midi - track.Transpose, 0, 127), Math.Clamp(n.Velocity, 1, 127)))
            .ToList();
        if (notes.Count == 0) return null;
        var length = Math.Max(timeline.TotalMs / 1000.0, notes.Max(n => n.StartSec + n.LengthSec));
        return new AudioClip { Name = track.Name, Notes = notes, StartSec = 0, SourceLengthSec = length, FileLengthSec = length };
    }

    /// <summary>Empties every bar's notes and second voice but keeps the bar headers (time signatures, tempo, repeats, sections).</summary>
    public static void Empty(TrackModel track)
    {
        foreach (var measure in track.Measures)
        {
            measure.Cells = new MeasureModel().Cells;
            measure.Voice2Cells = new();
            measure.SimileOneBar = false;
            measure.SimileTwoBar = false;
        }
    }
}
