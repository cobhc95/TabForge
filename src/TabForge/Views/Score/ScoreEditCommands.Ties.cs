using System.Linq;
using TabForge.Models;

namespace TabForge.Views.Score;

// ScoreEditCommands: the tie command (L / tie button).
// Owns: the per-string tie at the cursor (as GP5: L copies the previous beat's note on the cursor string as a tied note at the writing
// duration, a fret 0 when no note comes before; L on a tied note removes it), the beat tie (Ctrl+L: an empty beat or rest takes the
// previous beat's notes, tied), toggling ties over a selection, and dropping a tie when a fret is written onto it.
// Does not own: drawing (ScoreRenderer, StaffNotationArcs), bar length (MusicTime), playback sustain (ScoreToMidiCompiler).
// Tests: TestTieFillsEmptyBeat, TestTiePerString, TestEditToolToggles, TestCursorSnap, TestGp5EditStories.
public sealed partial class ScoreEditCommands
{
    public void ToggleTie()
    {
        if (ApplyToolSelection(cells =>
            {
                var makeTied = !cells.All(cell => cell.IsTied || cell.Notes.Any(note => note.Tied));
                foreach (var selected in cells)
                {
                    if (makeTied && FillTiedBeat(selected)) continue;
                    selected.IsTied = makeTied;
                    foreach (var note in selected.Notes) note.Tied = makeTied;
                }
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        var stringIndex = SelectedString;
        var previous = PreviousCellOf(cell);
        var source = previous?.Notes.FirstOrDefault(n => n.StringIndex == stringIndex && !n.Dead && !n.IsGraceNote);
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == stringIndex);
        var tiedNow = note is not null && (note.Tied || cell.IsTied);
        var nothingBefore = previous is null || previous.Notes.Count == 0;   // as GP5: no note before (song start, after a rest) ties a fret 0
        if (!tiedNow && source is null && !nothingBefore) return;   // as GP5: the beat before has notes, none on this string
        RunEdit(() =>
        {
            // As GP5, ties are per note on the cursor string: a beat tie becomes note ties first.
            if (cell.IsTied) { foreach (var n in cell.Notes) n.Tied = true; cell.IsTied = false; }
            if (tiedNow) { cell.Notes.Remove(note!); return true; }   // L on a tied note removes it (the beat empties when it was the last)
            if (note is null)
            {
                if (cell.Notes.Count == 0) ApplyPendingDuration(cell);   // an empty slot or rest takes the writing duration
                note = new TabNote { StringIndex = stringIndex, Velocity = source?.Velocity ?? CurrentVelocity };
                cell.Notes.Add(note);
            }
            note.Fret = source?.Fret ?? 0; note.MidiValue = source?.MidiValue ?? MidiOf(Track!, stringIndex, 0); note.Tied = true;
            cell.IsRest = false;
            return true;
        });
    }

    private bool CanFillTiedBeat(TabCell cell) => cell.Notes.Count == 0 && PreviousCellOf(cell) is { IsRest: false, Notes.Count: > 0 } previous && previous.Notes.Any(n => !n.Dead);

    /// <summary>An empty beat or rest after notes becomes a real tied beat: the previous beat's notes, tied, at this beat's duration.</summary>
    private bool FillTiedBeat(TabCell cell)
    {
        if (cell.Notes.Count > 0) return false;
        var previous = PreviousCellOf(cell);
        if (previous is null || previous.IsRest || previous.Notes.Count == 0) return false;
        foreach (var source in previous.Notes.Where(note => !note.Dead))
            cell.Notes.Add(new TabNote { StringIndex = source.StringIndex, Fret = source.Fret, MidiValue = source.MidiValue, Velocity = source.Velocity, Tied = true });
        if (cell.Notes.Count == 0) return false;
        cell.IsTied = true; cell.IsRest = false;
        return true;
    }

    /// <summary>
    /// A fret written onto a tied note or into a beat-tied beat is a new attack, as GP5 (ties are per note): the written note
    /// loses its tie and a beat tie becomes note ties on the beat's other notes, so the written note sounds and shows its fret.
    /// </summary>
    private static void SplitBeatTie(TabCell cell, int stringIndex)
    {
        foreach (var note in cell.Notes) note.Tied = note.StringIndex != stringIndex && (note.Tied || cell.IsTied);   // the written note itself is a new attack: its tie goes
        cell.IsTied = false;
    }

    private TabCell? PreviousCellOf(TabCell cell)
    {
        var track = Track;
        if (track is null) return null;
        TabCell? previous = null;
        foreach (var measure in track.Measures)
            foreach (var candidate in CellsFor(measure))
            {
                if (ReferenceEquals(candidate, cell)) return previous;
                if (candidate.Notes.Count > 0 || candidate.IsRest) previous = candidate;   // empty slots between beats are not beats
            }
        return null;
    }
}
