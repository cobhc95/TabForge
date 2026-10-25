using System.Collections.Generic;
using TabForge.Playback;

namespace TabForge.Presenters;

// Owns: which positions the playback movement lines join: each next note to the sounding note on the same string (the held notes, or the released
//   chord during a rest). A line needs the fret to change; a note of the sounding chord itself, a repeated shape or a string with no sounding
//   note gets no line.
// Does not own: drawing the lines (InstrumentPanelView) or building the note lists (InstrumentPlaybackSnapshot).
// Tests: TabForge.Avalonia.Tests.FretboardPlaybackMarkerTests.
public static class FretboardMovementRules
{
    /// <summary>One line: the finger's position on a string now, and where it goes next.</summary>
    public readonly record struct Move((int String, int Fret) From, (int String, int Fret) To);

    /// <summary>The lines to draw, empty when the hand does not move. The sounding shape is <paramref name="held"/>, or <paramref name="released"/>
    /// when nothing is held.</summary>
    public static List<Move> Between(IReadOnlyList<NoteEvent> held, IReadOnlyList<NoteEvent> released, IReadOnlyList<NoteEvent> next)
    {
        var moves = new List<Move>();
        var current = held.Count > 0 ? held : released;
        foreach (var target in next)
        {
            if (SameChordAsAny(current, target)) continue;
            foreach (var now in current)
            {
                if (now.StringIndex != target.StringIndex) continue;
                if (now.Fret != target.Fret) moves.Add(new Move((now.StringIndex, now.Fret), (target.StringIndex, target.Fret)));
                break;
            }
        }
        return moves;
    }

    /// <summary>Whether a note is struck on a string and fret that a sounding note already holds: a repeat, so it needs no next marker.</summary>
    public static bool Repeats(IReadOnlyList<NoteEvent> sounding, NoteEvent note)
    {
        foreach (var n in sounding) if (n.StringIndex == note.StringIndex && n.Fret == note.Fret) return true;
        return false;
    }

    private static bool SameChordAsAny(IReadOnlyList<NoteEvent> notes, NoteEvent note)
    {
        foreach (var n in notes)
            if (n.Bar == note.Bar && n.Cell == note.Cell && n.VoiceIndex == note.VoiceIndex && n.TrackIndex == note.TrackIndex) return true;
        return false;
    }
}
