using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>One note of the take being recorded, in piano roll beats; <paramref name="Held"/> is a key still down (its end follows the front).</summary>
public readonly record struct LiveRollNote(double StartBeat, double EndBeat, int Pitch, int Velocity, bool Held);

// Owns: the piano roll's live recording overlay model: the notes a live MIDI take has heard so far (finished and held), converted from song
//   seconds to the roll's beats through its time map. The view draws them translucent and clears them when the recording stops.
// Does not own: the live take (Avalonia LiveTakes), the drawing (PianoRollPlayheadLayer) or the landed clip (MidiTakeLanding).
// Tests: TabForge.Avalonia.Tests.MidiRecordLandingTests.
public static class PianoRollLiveNotes
{
    /// <summary>Refills <paramref name="into"/> (cleared first, reused so a redraw allocates nothing) from a take that began at
    /// <paramref name="takeStartSec"/> and has grown to <paramref name="frontSec"/>.</summary>
    public static void Fill(List<LiveRollNote> into, PianoRollTimeMap map, double takeStartSec, double frontSec,
        IReadOnlyList<ClipNote> done, IReadOnlyDictionary<int, (double Start, int Velocity)> held)
    {
        into.Clear();
        foreach (var note in done)
            into.Add(new LiveRollNote(map.BeatsAt(takeStartSec + note.StartSec), map.BeatsAt(takeStartSec + note.StartSec + note.LengthSec), note.Pitch, note.Velocity, false));
        foreach (var (pitch, hold) in held)
            into.Add(new LiveRollNote(map.BeatsAt(hold.Start), map.BeatsAt(Math.Max(hold.Start, frontSec)), pitch, hold.Velocity, true));
    }
}
