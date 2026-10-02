using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// ScoreEditCommands: per-beat marks, ties, techniques, beat and bar operations and the tool-state queries for them.
public sealed partial class ScoreEditCommands
{
    public bool HasEditableNotes
    {
        get
        {
            var cells = ToolCells();
            return cells.SelectMany(cell => cell.Notes).Any(note => HasSelection || note.StringIndex == SelectedString);
        }
    }

    public bool HasSecondaryBeamEligibleNotes =>
        (HasSelection ? ToolCells() : CurrentCell() is { } cell ? new List<TabCell> { cell } : new List<TabCell>())
        .Any(cell => cell.Notes.Count > 0 && cell.DurationDenominator >= 16);

    public bool? GetNoteCellToolState(Func<TabCell, bool> state)
    {
        var cells = HasSelection ? ToolCells() : CurrentCell() is { } cell ? new List<TabCell> { cell } : new List<TabCell>();
        var notes = cells.Where(cell => cell.Notes.Count > 0).ToList();
        return notes.Count == 0 ? null : Uniform(notes.Select(state));
    }

    private List<TabCell> EditableCells() => HasSelection ? ToolCells(createVoice: true) :
        CurrentCell() is { } cell ? new List<TabCell> { cell } : new List<TabCell>();

    public void SetSoundDurationPercent(int percent)
    {
        percent = Math.Clamp(percent, 1, 200);
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        RunEdit(() => { foreach (var cell in cells) cell.SoundDurationPercent = percent; return true; });
    }

    public void SetOctaveShift(int semitones)
    {
        if (semitones is not (-24 or -12 or 0 or 12 or 24)) return;
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        RunEdit(() => { foreach (var cell in cells) cell.OctaveShiftSemitones = semitones; return true; });
    }

    public void SetBeamMode(BeamMode mode)
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        RunEdit(() => { foreach (var cell in cells) cell.BeamMode = mode; return true; });
    }

    public void ResetBeaming()
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        RunEdit(() =>
        {
            foreach (var cell in cells)
            {
                cell.BeamMode = BeamMode.Auto;
                cell.BreakSecondaryBeamBefore = false;
            }
            return true;
        });
    }

    public void SetSecondaryBeamBreak(bool value)
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0 && cell.DurationDenominator >= 16).ToList();
        if (cells.Count == 0) return;
        RunEdit(() => { foreach (var cell in cells) cell.BreakSecondaryBeamBefore = value; return true; });
    }

    public void SetStemDirection(StemDirection direction)
    {
        var cells = EditableCells().Where(cell => cell.Notes.Count > 0).ToList();
        if (cells.Count == 0) return;
        RunEdit(() => { foreach (var cell in cells) cell.StemDirection = direction; return true; });
    }

    public bool CanTieSelectedNote()
    {
        if (HasSelection) return false;
        var cell = CurrentCell();
        var note = cell?.Notes.FirstOrDefault(candidate => candidate.StringIndex == SelectedString);
        return note is not null && FindPreviousCompatibleNote(note) is not null;
    }

    public bool CanTieSelectedBeat()
    {
        if (HasSelection) return false;
        var cell = CurrentCell();
        return cell is not null && cell.Notes.Any(note => FindPreviousCompatibleNote(note) is not null);
    }

    public void TieSelectedNote()
    {
        if (!CanTieSelectedNote()) return;
        var note = CurrentCell()!.Notes.First(candidate => candidate.StringIndex == SelectedString);
        RunEdit(() => { note.Tied = true; return true; });
    }

    public void TieSelectedBeat()
    {
        if (!CanTieSelectedBeat()) return;
        var notes = CurrentCell()!.Notes.Where(note => FindPreviousCompatibleNote(note) is not null).ToList();
        RunEdit(() => { foreach (var note in notes) note.Tied = true; return true; });
    }

    private TabNote? FindPreviousCompatibleNote(TabNote destination)
    {
        var track = Track;
        if (track is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return null;
        var currentCells = CellsFor(track.Measures[SelectedMeasure]);
        if (SelectedCell < 0 || SelectedCell >= currentCells.Count) return null;
        var currentStart = AbsoluteCellStart(track, SelectedMeasure, SelectedCell, currentCells[SelectedCell]);
        for (var measureIndex = SelectedMeasure; measureIndex >= 0; measureIndex--)
        {
            var cells = CellsFor(track.Measures[measureIndex]);
            var firstCell = measureIndex == SelectedMeasure ? SelectedCell - 1 : cells.Count - 1;
            for (var cellIndex = firstCell; cellIndex >= 0; cellIndex--)
            {
                var cell = cells[cellIndex];
                var previous = cell.Notes.FirstOrDefault(candidate => candidate.StringIndex == destination.StringIndex);
                if (previous is null) continue;
                if (NotePitch(track, previous) != NotePitch(track, destination)) return null;
                var end = AbsoluteCellStart(track, measureIndex, cellIndex, cell) + MusicTime.CellSlots(cell);
                return Math.Abs(end - currentStart) <= 0.51 ? previous : null;
            }
        }
        return null;
    }

    private static int NotePitch(TrackModel track, TabNote note) => note.MidiValue > 0 ? note.MidiValue
        : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
            ? track.PitchOf(note.StringIndex, note.Fret) : note.Fret;

    private double AbsoluteCellStart(TrackModel track, int measureIndex, int cellIndex, TabCell cell)
    {
        var start = 0.0;
        for (var measure = 0; measure < measureIndex; measure++)
            start += _project is null ? 16 : MusicTime.BarSlots(_project, measure);
        return start + BeatStart(cell, cellIndex);
    }

    public bool CanSetDurationForTool(string key) => key switch
    {
        "whole" => CanSetDuration(1), "half" => CanSetDuration(2), "quarter" => CanSetDuration(4),
        "eighth" => CanSetDuration(8), "sixteenth" => CanSetDuration(16),
        "thirtysecond" => CanSetDuration(32), "sixtyfourth" => CanSetDuration(64),
        "dotted" => CanSetDots(1), "double-dotted" => CanSetDots(2),
        "tuplet" => CanSetTuplet(GetToolState("duration:tuplet") == true ? (0, 0) : (3, 2)),
        _ => true
    };

    public void ToggleRest()
    {
        if (ApplyToolSelection(cells =>
            {
                var makeRest = !cells.All(cell => cell.IsRest);
                foreach (var selected in cells)
                {
                    selected.IsRest = makeRest;
                    if (makeRest) selected.Notes.Clear();
                }
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() =>
        {
            cell.IsRest = !cell.IsRest;
            if (cell.IsRest) cell.Notes.Clear();
            return true;
        });
    }

    public void ToggleTie()
    {
        if (ApplyToolSelection(cells =>
            {
                var makeTied = !cells.All(cell => cell.IsTied || cell.Notes.Any(note => note.Tied));
                foreach (var selected in cells)
                {
                    selected.IsTied = makeTied;
                    foreach (var note in selected.Notes) note.Tied = makeTied;
                }
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() => { cell.IsTied = !cell.IsTied; return true; });
    }

    public void ToggleFermata()
    {
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() => { cell.Fermata = !cell.Fermata; return true; });
    }

    public void CycleAccent()
    {
        if (ApplyToolSelection(cells =>
            {
                var current = cells.Select(cell => cell.Accent).Distinct().Count() == 1 ? cells[0].Accent : 0;
                var target = (current + 1) % 3;
                foreach (var selected in cells) selected.Accent = target;
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() => { cell.Accent = (cell.Accent + 1) % 3; return true; });
    }

    public void SetAccent(int accent)
    {
        accent = Math.Clamp(accent, 0, 2);
        if (ApplyToolSelection(cells =>
            {
                foreach (var selected in cells) selected.Accent = accent;
            })) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() => { cell.Accent = accent; return true; });
    }

    public void ToggleStaccato()
    {
        if (ApplyToolSelection(Services.EditCommands.ToggleStaccato)) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() => { cell.Staccato = !cell.Staccato; return true; });
    }

    public void ToggleTenuto()
    {
        if (ApplyToolSelection(Services.EditCommands.ToggleTenuto)) return;
        var cell = CurrentCell(create: true); if (cell is null) return;
        RunEdit(() => { cell.Tenuto = !cell.Tenuto; return true; });
    }

    public void ToggleTechnique(string technique)
    {
        if (HasSelection)
        {
            var notes = ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList();
            if (notes.Count == 0) return;
            RunEdit(() =>
            {
                Services.EditCommands.ToggleTechnique(notes, technique);   // on when any note lacks it, otherwise off for all
                return true;
            });
            return;
        }
        var track = Track; var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        RunEdit(() =>
        {
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
            if (note is null)
            {
                ApplyPendingDuration(cell);
                note = new TabNote { StringIndex = SelectedString, Fret = 0, MidiValue = MidiOf(track, SelectedString, 0), Velocity = CurrentVelocity };
                cell.Notes.Add(note);
            }
            if (!note.Techniques.Add(technique)) note.Techniques.Remove(technique);
            return true;
        });
    }

    public void ToggleDead()
    {
        if (HasSelection)
        {
            var notes = ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList();
            if (notes.Count == 0) return;
            var value = notes.Any(note => !note.Dead);
            RunEdit(() => { foreach (var selectedNote in notes) selectedNote.Dead = value; return true; });
            return;
        }
        var track = Track; var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        RunEdit(() =>
        {
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
            if (note is null) { ApplyPendingDuration(cell); note = new TabNote { StringIndex = SelectedString, Dead = true, Fret = 0, MidiValue = MidiOf(track, SelectedString, 0), Velocity = CurrentVelocity }; cell.Notes.Add(note); }
            else note.Dead = !note.Dead;
            return true;
        });
    }

    public void ToggleGhost()
    {
        if (HasSelection)
        {
            var notes = ToolCells(createVoice: true).SelectMany(cell => cell.Notes).ToList();
            if (notes.Count == 0) return;
            var value = notes.Any(note => !note.Ghost);
            RunEdit(() => { foreach (var selectedNote in notes) selectedNote.Ghost = value; return true; });
            return;
        }
        var track = Track; var cell = CurrentCell(create: true);
        if (track is null || cell is null) return;
        RunEdit(() =>
        {
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
            if (note is null) { ApplyPendingDuration(cell); note = new TabNote { StringIndex = SelectedString, Ghost = true, Fret = 5, MidiValue = MidiOf(track, SelectedString, 5), Velocity = CurrentVelocity }; cell.Notes.Add(note); }
            else note.Ghost = !note.Ghost;
            return true;
        });
    }

    /// <summary>Backspace deletes the note under the cursor.</summary>
    public void DeleteNote()
    {
        if (ApplyToolSelection(cells =>
            {
                foreach (var selected in cells) { var had = selected.Notes.Count > 0; selected.Notes.Clear(); if (FillBars && had) selected.IsRest = true; }
                if (_c.MergeRestsOnDelete) NormaliseRests(cells);
            })) { RestoreRestSelection(); return; }
        var track = Track; var cell = CurrentCell();
        if (track is null || cell is null) return;
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        RunEdit(() =>
        {
            if (note is not null) { cell.Notes.Remove(note); if (FillBars && cell.Notes.Count == 0) cell.IsRest = true; }
            if (_c.MergeRestsOnDelete && cell.Notes.Count == 0) NormaliseRests(new[] { cell });
            return true;
        });
    }

    /// <summary>Delete: clears the beats. With the rest fill on, the cleared beats become rests (merged when the setting says so); a selection that holds only rests is reset to the merged form.</summary>
    public void DeleteBeat()
    {
        void Clear(IReadOnlyList<TabCell> cells)
        {
            var restOnly = NoNotes(cells);
            Services.EditCommands.ClearBeats(cells, FillBars);
            if (FillBars && (restOnly || _c.MergeRestsOnDelete)) NormaliseRests(cells, wholeRuns: !restOnly);
        }
        if (ApplyToolSelection(Clear)) { RestoreRestSelection(); return; }
        var cell = CurrentCell(); if (cell is null) return;
        RunEdit(() => { Clear(new[] { cell }); return true; });
    }

    /// <summary>Insert a beat at the cursor, shifting the rest of the bar right.</summary>
    public void InsertBeat()
    {
        var bar = SelectedMeasure; var at = SelectedCell;
        if (HasSelection)
        {
            // Inserting is a cursor action: one beat at the start of the range.
            var (m1, c1, m2, c2) = SelectionRange();
            (bar, at) = m2 < m1 || (m2 == m1 && c2 < c1) ? (m2, c2) : (m1, c1);
        }
        var track = Track;
        if (track is null || bar < 0 || bar >= track.Measures.Count) return;
        var cells = CellsFor(track.Measures[bar], create: true);
        var slots = SlotsFor(bar);
        RunEdit(() =>
        {
            if (FillBars) Services.BarFill.InsertBeat(cells, at, slots, NewBeat());   // consumes following rests, never drops a note
            else Services.EditCommands.InsertBeatAt(cells, at, slots, NewBeat());
            return true;
        });
    }

    private TabCell NewBeat() => new()
    {
        DurationDenominator = CurrentDurationDenominator, Dots = CurrentDots, IsTriplet = CurrentTriplet,
        TupletNumerator = CurrentTupletNumerator, TupletDenominator = CurrentTupletDenominator
    };

    /// <summary>Delete the beat(s) at the cursor, or the selected beats of every selected bar, shifting the rest of each bar left.</summary>
    public void DeleteBeats()
    {
        if (HasSelection) { EditSelectedBars((cells, first, last, slots) => Services.EditCommands.DeleteBeatsAt(cells, first, last, slots)); return; }
        var measure = CurrentMeasure(); if (measure is null) return;
        var cells = CellsFor(measure, create: true);
        var slots = SlotsFor(SelectedMeasure);
        RunEdit(() => { Services.EditCommands.DeleteBeatsAt(cells, SelectedCell, SelectedCell, slots); return true; });
    }

    /// <summary>One undo step over every selected bar: <paramref name="edit"/> gets the active voice's cells, the first and last selected cell in that bar, and the bar's slot count.</summary>
    private void EditSelectedBars(Action<List<TabCell>, int, int, int> edit)
    {
        var track = Track; if (track is null || track.Measures.Count == 0) return;
        var (m1, c1, m2, c2) = SelectionRange();
        if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
        RunEdit(() =>
        {
            for (var m = Math.Max(0, m1); m <= Math.Min(m2, track.Measures.Count - 1); m++)
            {
                var cells = CellsFor(track.Measures[m], create: _activeVoiceIndex == 1);
                var slots = SlotsFor(m);
                edit(cells, m == m1 ? c1 : 0, m == m2 ? c2 : slots - 1, slots);
            }
            return true;
        });
    }


    /// <summary>Repeat beat (C, and Edit > Copy last beat): the previous beat of the active voice onto the cursor, then step forward.</summary>
    public void CopyLastBeat()
    {
        var track = Track; var measure = CurrentMeasure();
        if (track is null || measure is null) return;
        var cells = CellsFor(measure);
        if (!Services.EditCommands.CanCopyLastBeat(cells, SelectedCell)) { _c.Say("No previous beat to copy"); return; }
        RunEdit(() => { Services.EditCommands.CopyLastBeat(cells, SelectedCell); return true; });
        MoveForwardBeat(track);
    }

    /// <summary>Empties the active voice of the selected bar (one undo step; nothing is captured when it is already empty).</summary>
    public void EmptyBar()
    {
        var measure = CurrentMeasure(); if (measure is null) return;
        var cells = CellsFor(measure);
        if (!cells.Any(c => c.Notes.Count > 0 || c.IsRest || c.IsTied)) return;
        RunEdit(() => { Services.EditCommands.EmptyBar(cells); return true; });
    }


    internal static TabCell CloneCell(TabCell src) => src.Clone();

    public void ToggleRepeatOpen()
    {
        if (_project is null || CurrentMeasure() is null) return;
        RunEdit(() => { EditCommands.ToggleRepeatOpen(_project, SelectedTrackIndex, SelectedMeasure, out _); return true; });
    }

    /// <summary>Toggles the repeat end; <paramref name="count"/> (from the menu's prompt) sets the repeat count when turning it on.</summary>
    public void ToggleRepeatClose(int? count = null)
    {
        if (_project is null || CurrentMeasure() is null) return;
        RunEdit(() => { EditCommands.ToggleRepeatClose(_project, SelectedTrackIndex, SelectedMeasure, count, out _); return true; });
    }

    /// <summary>
    /// Moves the selected note(s) (the note under the cursor, or every note of the selected beats) to the adjacent string
    /// without changing the pitch: the fret is recalculated from the tuning and capo. <paramref name="delta"/> -1 is the higher
    /// string (one up on the tab), +1 the lower. All or nothing: when any note cannot go (no such string, a fret below 0 or past
    /// the last fret, or the string is taken in that beat) nothing changes and the status line says why. One undo step.
    /// </summary>
    public bool MoveNotesToAdjacentString(int delta)
    {
        var track = Track;
        if (track is null) return false;
        void Say(string text) => _c.Say(text);
        // The rules (which notes may move, the new frets, why not) are EditCommands.PlanStringMove; the control brackets them with its edit events.
        var plan = EditCommands.PlanStringMove(track, ToolCells().Select(cell =>
            (cell, (IReadOnlyList<TabNote>)(HasSelection ? cell.Notes.ToList() : cell.Notes.Where(n => n.StringIndex == SelectedString).ToList()))), delta);
        if (plan.Refusal is { } refusal) { Say(refusal); return false; }
        var side = delta < 0 ? "higher" : "lower";
        RunEdit(() =>
        {
            EditCommands.ApplyStringMove(track, plan);
            if (!HasSelection) SelectedString = plan.Moves[0].Target;
            return true;
        });
        Say(plan.Moves.Count == 1 ? $"Moved the note to the {side} string" : $"Moved {plan.Moves.Count} notes to the {side} string");
        return true;
    }

    /// <summary>Move the note up/down in pitch by semitones (Shift+Up / Shift+Down).</summary>
    public void ShiftPitch(int semitones)
    {
        var track = Track;
        if (track is null) return;
        if (HasSelection)
        {
            var plans = new List<(TabNote Note, int String, int Fret)>();
            foreach (var selected in ToolCells(createVoice: true))
                foreach (var n in selected.Notes)
                    if (EditCommands.PlanPitchShift(track, selected, n, semitones) is var (s, f)) plans.Add((n, s, f));
            if (plans.Count == 0) return;
            RunEdit(() => { foreach (var (n, s, f) in plans) EditCommands.ApplyPitchShift(track, n, s, f); return true; });
            return;
        }
        var cell = CurrentCell();
        if (cell is null) return;
        var note = cell.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        if (note is null) return;
        // Below the open string the same pitch moves to the next lower free string (EditCommands.PlanPitchShift); null: already the lowest playable pitch.
        if (EditCommands.PlanPitchShift(track, cell, note, semitones) is not var (stringIndex, fret)) return;
        RunEdit(() =>
        {
            EditCommands.ApplyPitchShift(track, note, stringIndex, fret);
            if (stringIndex != SelectedString) SetPosition(SelectedMeasure, SelectedCell, stringIndex);
            return true;
        });
    }
}
