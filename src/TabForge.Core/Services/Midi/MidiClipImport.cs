using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: turning a Standard MIDI File into one MIDI clip (every note of every track, in seconds through the file's own tempo map).
// Does not own: reading the file bytes (SmfReader), placing the clip or the undo step (the take-lane menu's import), or building a song from the file (MidiToSong).
// Tests: TabForge.Avalonia.Tests.TakeLaneMenuTests.
/// <summary>A MIDI file as a clip for a take lane.</summary>
public static class MidiClipImport
{
    /// <summary>The clip for the file's bytes (start 0, lane 0), or null when the file holds no notes. Throws <see cref="InvalidDataException"/> for a file that is not a readable MIDI score.</summary>
    public static AudioClip? Read(byte[] data, string name)
    {
        var file = SmfReader.Read(data);
        var notes = file.Tracks.SelectMany(track => track.Notes).OrderBy(note => note.Start).ThenBy(note => note.Pitch).ToList();
        if (notes.Count == 0) return null;
        var clipNotes = notes.Select(note =>
        {
            var start = SecondsAt(file, note.Start);
            return new ClipNote(start, Math.Max(0.001, SecondsAt(file, note.End) - start), note.Pitch, note.Velocity);
        }).ToList();
        var length = Math.Max(0.001, clipNotes.Max(note => note.StartSec + note.LengthSec));
        return new AudioClip { Name = name, Notes = clipNotes, SourceLengthSec = length, FileLengthSec = length };
    }

    // Ticks to seconds through the tempo changes (500000 microseconds per quarter until the first one).
    private static double SecondsAt(SmfFile file, long tick)
    {
        double seconds = 0;
        long previous = 0;
        var mpq = 500000;
        foreach (var (at, tempo) in file.Tempos)
        {
            if (at >= tick) break;
            seconds += (at - previous) * (double)mpq / 1_000_000 / file.Division;
            previous = at;
            mpq = tempo;
        }
        return seconds + (tick - previous) * (double)mpq / 1_000_000 / file.Division;
    }
}
