using TabForge.Models;

namespace TabForge.Services;

// The note rules the score editor used to hold inline (string moves, pitch steps, effect toggles): pure model code, planned first and applied after, so the
// control only brackets them with its undo / refresh events and a test can run them without any WPF control.
public static partial class EditCommands
{
    /// <summary>The MIDI value of a fret on a string: tuning + capo + fret; on a drum track the number written is the GM percussion note itself.</summary>
    public static int NoteMidi(TrackModel track, int stringIndex, int fret)
    {
        if (track.StringTunings.Count == 0 || track.MidiChannel == 9 || track.Kind == TrackKind.Drums) return fret;
        return track.PitchOf(stringIndex, fret);
    }

    public sealed record StringMove(TabNote Note, int Target, int Fret);

    /// <summary>What moving notes to the adjacent string would do: the moves, or why nothing may move (<see cref="Refusal"/>).</summary>
    public sealed record StringMovePlan(IReadOnlyList<StringMove> Moves, string? Refusal);

    /// <summary>
    /// Plans moving notes to the adjacent string without changing the pitch (the fret is recalculated from the tuning and capo): <paramref name="delta"/> -1 is
    /// the higher string, +1 the lower. All or nothing: when any note cannot go (no such string, a fret below 0 or past the last fret, or the string is taken
    /// in that beat) the plan has a <see cref="StringMovePlan.Refusal"/> and no moves. <paramref name="beats"/> pairs each beat with the notes that move in it.
    /// </summary>
    public static StringMovePlan PlanStringMove(TrackModel track, IEnumerable<(TabCell Cell, IReadOnlyList<TabNote> Movers)> beats, int delta)
    {
        static StringMovePlan Refuse(string why) => new(Array.Empty<StringMove>(), why);
        if (track.StringTunings.Count == 0 || track.MidiChannel == 9 || track.Kind == TrackKind.Drums) return Refuse("This track has no strings to move notes between");
        var side = delta < 0 ? "higher" : "lower";
        var moves = new List<StringMove>();
        foreach (var (cell, movers) in beats)
            foreach (var note in movers)
            {
                var target = note.StringIndex + delta;
                if (target < 0 || target >= track.StringTunings.Count) return Refuse($"No change: the note is already on the {(delta < 0 ? "highest" : "lowest")} string");
                var fret = track.FretOf(target, NoteMidi(track, note.StringIndex, note.Fret));
                if (fret < 0) return Refuse($"No change: that pitch is below the open {side} string");
                if (fret > track.NumberOfFrets) return Refuse($"No change: that pitch is above the last fret of the {side} string");
                if (cell.Notes.Any(other => other.StringIndex == target && !movers.Contains(other))) return Refuse($"No change: the {side} string already has a note in that beat");
                moves.Add(new StringMove(note, target, fret));
            }
        return moves.Count == 0 ? Refuse("No note to move: put the cursor on a note or select some beats") : new StringMovePlan(moves, null);
    }

    /// <summary>Carries out a plan from <see cref="PlanStringMove"/>: the pitch stays, the string and fret change.</summary>
    public static void ApplyStringMove(TrackModel track, StringMovePlan plan)
    {
        foreach (var (note, target, fret) in plan.Moves)
        {
            var pitch = NoteMidi(track, note.StringIndex, note.Fret);
            note.StringIndex = target;
            note.Fret = fret;
            note.MidiValue = pitch;
        }
    }

    /// <summary>
    /// The string and fret a note takes when it moves <paramref name="semitones"/> up or down: the same string, one fret per semitone; below the open string
    /// the same pitch on the next lower free string, as a guitarist would. Null when it cannot move (already the lowest playable pitch).
    /// </summary>
    public static (int StringIndex, int Fret)? PlanPitchShift(TrackModel track, TabCell cell, TabNote note, int semitones)
    {
        var fret = note.Fret + semitones;
        var stringIndex = note.StringIndex;
        if (fret < 0)
        {
            var target = NoteMidi(track, note.StringIndex, note.Fret) + semitones;
            var lower = Enumerable.Range(note.StringIndex + 1, Math.Max(0, track.StringTunings.Count - note.StringIndex - 1))
                .FirstOrDefault(s => NoteMidi(track, s, 0) <= target && cell.Notes.All(n => n.StringIndex != s), -1);
            if (lower < 0) return null;
            stringIndex = lower;
            fret = target - NoteMidi(track, lower, 0);
        }
        return (stringIndex, fret);
    }

    public static void ApplyPitchShift(TrackModel track, TabNote note, int stringIndex, int fret)
    {
        note.StringIndex = stringIndex;
        note.Fret = fret;
        note.MidiValue = NoteMidi(track, stringIndex, fret);
    }

    /// <summary>An effect over a selection: on when any note lacks it, otherwise off for every note.</summary>
    public static void ToggleTechnique(IReadOnlyList<TabNote> notes, string technique)
    {
        var add = notes.Any(note => !note.Techniques.Contains(technique));
        foreach (var note in notes)
        {
            if (add) note.Techniques.Add(technique);
            else note.Techniques.Remove(technique);
        }
    }
}
