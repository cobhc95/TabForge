using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

// Owns: the duration, dot and tuplet commands of the score editor and the bar-fit checks behind them (partial of ScoreEditCommands).
// Tests: no dedicated test class.

public sealed partial class ScoreEditCommands
{
    public void SetDuration(int denominator) => SetDuration(denominator, force: false);

    // force: +/- step the selected beat, even when the bar then over/underfills
    // (the bar is flagged instead of the key silently doing nothing).
    private void SetDuration(int denominator, bool force)
    {
        if (Array.IndexOf(MusicTime.AllDenominators, denominator) < 0) return;
        if ((!force || PreventBarOverflow) && !CanSetDuration(denominator)) return;
        if (force) RememberWritingForUndo();   // as GP5, +/- on an empty spot is an undo step of its own
        CurrentDurationDenominator = denominator;
        if (ApplyToolSelection(cells => { if (RefillSelectedRests(cells, denominator)) return; Services.EditCommands.ApplyDuration(cells, denominator); if (!CloseUpSelection(cells)) NormaliseRests(cells, resize: true); })) { RestoreRestSelection(); return; }
        var cell = CurrentCell();
        if (cell is not null && (cell.Notes.Count > 0 || cell.IsRest) && !Placeholder(cell))   // an empty slot or placeholder rest: the value is the writing duration only
        {
            RunEdit(() => { cell.DurationDenominator = denominator; FollowLengthChange(cell); return true; });
        }
        else
        {
            // Nothing under the cursor yet: the value still applies to the next note,
            // so tell the host to refresh its readouts.
            NotifyState();
            InvalidateVisual();
        }
    }

    private void NotifyState() => _c.NotifyState();

    public void Longer() => SetDuration(MusicTime.Longer(StepBaseDuration()), force: true);
    public void Shorter() => SetDuration(MusicTime.Shorter(StepBaseDuration()), force: true);

    /// <summary>The "." key: toggles the single dot (double dots have their own command).</summary>
    public void ToggleDot()
    {
        var dots = BeatDots() == 1 ? 0 : 1;
        if (!CanSetDots(dots)) return;
        RememberWritingForUndo();
        CurrentDots = dots;
        if (ApplyToolSelection(DotSelection)) { RestoreRestSelection(); return; }
        var cell = CurrentCell();
        if (cell is null || Placeholder(cell)) { NotifyState(); InvalidateVisual(); return; }   // as GP5: an empty spot's dot is the next note's
        RunEdit(() => { cell.Dots = CurrentDots; FollowLengthChange(cell); return true; });
    }

    public void SetDots(int dots)
    {
        dots = Math.Clamp(dots, 0, 2);
        if (dots > 0 && BeatDots() == dots && ToolCells().All(c => c.Dots == dots)) dots = 0;   // clicking the active dot button removes it
        if (!CanSetDots(dots)) return;
        CurrentDots = dots;
        if (ApplyToolSelection(DotSelection)) { RestoreRestSelection(); return; }
        var cell = CurrentCell();
        if (cell is null || Placeholder(cell)) { NotifyState(); InvalidateVisual(); return; }
        RunEdit(() => { cell.Dots = CurrentDots; FollowLengthChange(cell); return true; });
    }

    public void ToggleTriplet()
    {
        var beats = ToolCells().Where(MusicTime.IsBeat).ToList();   // the beat's own state, not the pending entry state
        var triplet = beats.Count > 0
            ? Services.EditCommands.NextTriplet(beats)
            : !CurrentTriplet;
        if (!CanSetTuplet(triplet ? (3, 2) : (0, 0))) return;
        RememberWritingForUndo();
        CurrentTriplet = triplet;
        CurrentTupletNumerator = triplet ? 3 : 0;
        CurrentTupletDenominator = triplet ? 2 : 0;
        if (ApplyToolSelection(cells =>
            {
                foreach (var selected in cells) Services.EditCommands.ApplyTriplet(selected, CurrentTriplet);
                foreach (var measure in Track!.Measures)   // the later beats of each bar follow on, as GP5 times them
                    if (CellsFor(measure) is var barCells && barCells.FindIndex(cells.Contains) is >= 0 and var first) Services.EditCommands.RetimeFrom(barCells, first);
            })) return;
        var cell = CurrentCell();
        if (cell is null || Placeholder(cell)) { NotifyState(); InvalidateVisual(); return; }   // as GP5: an empty spot's triplet is the next note's
        RunEdit(() =>
        {
            cell.IsTriplet = CurrentTriplet;
            cell.TupletNumerator = CurrentTriplet ? 3 : 0;
            cell.TupletDenominator = CurrentTriplet ? 2 : 0;
            if (Track is { } track && SelectedMeasure < track.Measures.Count) Services.EditCommands.RetimeFrom(CellsFor(track.Measures[SelectedMeasure]), SelectedCell);   // GP5 b05
            return true;
        });
    }

    /// <summary>Whether the candidate written duration fits every selected beat in its own bar.</summary>
    public bool CanSetDuration(int denominator)
        => CanApplyRhythmicValue(denominator, null, null);

    public bool CanSetDots(int dots)
        => CanApplyRhythmicValue(null, Math.Clamp(dots, 0, 2), null);

    public bool CanSetTuplet((int Numerator, int Denominator) ratio)
        => CanApplyRhythmicValue(null, null, ratio);

    private bool CanApplyRhythmicValue(int? requestedDenominator, int? requestedDots,
        (int Numerator, int Denominator)? requestedTuplet)
    {
        if (requestedDenominator.HasValue && Array.IndexOf(MusicTime.AllDenominators, requestedDenominator.Value) < 0) return false;
        // Default: any rhythm may be entered; a bar that no longer adds up is flagged red.
        if (!PreventBarOverflow) return Track is not null && _project is not null;
        var track = Track;
        if (track is null || _project is null) return false;
        if (!SelectedBeatsFit(track, requestedDenominator, requestedDots, requestedTuplet, out var checkedTarget)) return false;
        // ToolCells normally returns the current cell even when it is empty. Its pending duration
        // still has to fit between the cursor and the next real beat (or the end of this bar).
        if (checkedTarget) return true;
        return PendingDurationFitsAtCursor(track, requestedDenominator, requestedDots, requestedTuplet);
    }

    /// <summary>Whether every selected beat, with the requested value, still fits its bar and overlaps no other beat. <paramref name="checkedTarget"/> says whether any selected beat was checked.</summary>
    private bool SelectedBeatsFit(TrackModel track, int? requestedDenominator, int? requestedDots, (int Numerator, int Denominator)? requestedTuplet, out bool checkedTarget)
    {
        var selected = ToolCells();
        var selectedSet = selected.ToHashSet();
        checkedTarget = false;
        for (var measureIndex = 0; measureIndex < track.Measures.Count; measureIndex++)
        {
            var measure = track.Measures[measureIndex];
            var cells = CellsFor(measure, create: _activeVoiceIndex == 1);
            var barSlots = MusicTime.BarSlots(_project!, measureIndex);
            var beats = cells.Select((cell, index) => (cell, index))
                .Where(item => item.cell.Notes.Count > 0 || item.cell.IsRest || item.cell.HasAnnotation)
                .Select(item => (item.cell, item.index, Start: CursorPositions.BeatStart(item.cell, item.index)))
                .ToList();

            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (!selectedSet.Contains(cell)) continue;
                checkedTarget = true;
                var start = CursorPositions.BeatStart(cell, index);
                var hasBeat = cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation;
                var denominator = requestedDenominator ?? (hasBeat ? cell.DurationDenominator : CurrentDurationDenominator);
                var dots = requestedDots ?? (hasBeat ? cell.Dots : CurrentDots);
                var tuplet = requestedTuplet ?? (hasBeat ? cell.Tuplet : CurrentTupletNumerator > 0
                    ? (CurrentTupletNumerator, CurrentTupletDenominator) : CurrentTriplet ? (3, 2) : (0, 0));
                var duration = DurationSlots(denominator, dots, tuplet);
                var end = start + duration;
                if (start < 0 || end > barSlots + 0.001) return false;
                foreach (var other in beats)
                {
                    if (ReferenceEquals(other.cell, cell) || Math.Abs(other.Start - start) <= 0.001) continue;
                    var otherDuration = MusicTime.CellSlots(other.cell);
                    if (selectedSet.Contains(other.cell))
                    {
                        var otherTuplet = requestedTuplet ?? other.cell.Tuplet;
                        var otherDenominator = requestedDenominator ?? other.cell.DurationDenominator;
                        var otherDots = requestedDots ?? other.cell.Dots;
                        otherDuration = DurationSlots(otherDenominator, otherDots, otherTuplet);
                    }
                    if (start < other.Start + otherDuration - 0.001 && other.Start < end - 0.001)
                        return false;
                }
            }
        }
        return true;
    }

    /// <summary>The pending entry duration still has to fit between the cursor and the next real beat (or the end of the bar).</summary>
    private bool PendingDurationFitsAtCursor(TrackModel track, int? requestedDenominator, int? requestedDots, (int Numerator, int Denominator)? requestedTuplet)
    {
        var currentMeasure = CurrentMeasure();
        if (currentMeasure is null || SelectedMeasure < 0 || SelectedMeasure >= track.Measures.Count) return false;
        var currentCells = CellsFor(currentMeasure);
        if (currentCells.Count == 0)
        {
            var pendingTupletOnly = requestedTuplet ?? (CurrentTupletNumerator > 0
                ? (CurrentTupletNumerator, CurrentTupletDenominator) : CurrentTriplet ? (3, 2) : (0, 0));
            var pendingOnlyDuration = DurationSlots(requestedDenominator ?? CurrentDurationDenominator,
                requestedDots ?? CurrentDots, pendingTupletOnly);
            return pendingOnlyDuration <= MusicTime.BarSlots(_project!, SelectedMeasure) + 0.001;
        }
        var cursorCell = Math.Clamp(SelectedCell, 0, Math.Max(0, currentCells.Count - 1));
        var cursor = currentCells[cursorCell];
        var cursorStart = CursorPositions.BeatStart(cursor, cursorCell);
        var pendingDenominator = requestedDenominator ?? CurrentDurationDenominator;
        var pendingDots = requestedDots ?? CurrentDots;
        var pendingTuplet = requestedTuplet ?? (CurrentTupletNumerator > 0
            ? (CurrentTupletNumerator, CurrentTupletDenominator) : CurrentTriplet ? (3, 2) : (0, 0));
        var pendingDuration = DurationSlots(pendingDenominator, pendingDots, pendingTuplet);
        var pendingBarSlots = MusicTime.BarSlots(_project!, SelectedMeasure);
        var beatsAtCursorBar = currentCells.Select((cell, index) => (cell, index))
            .Where(item => item.cell.Notes.Count > 0 || item.cell.IsRest || item.cell.HasAnnotation)
            .Select(item => (item.cell, Start: CursorPositions.BeatStart(item.cell, item.index), Duration: MusicTime.CellSlots(item.cell)))
            .ToList();
        var cursorEnd = cursorStart + pendingDuration;
        if (cursorStart < 0 || cursorEnd > pendingBarSlots + 0.001) return false;
        return beatsAtCursorBar.All(beat => Math.Abs(beat.Start - cursorStart) <= 0.001 ||
            !(cursorStart < beat.Start + beat.Duration - 0.001 && beat.Start < cursorEnd - 0.001));
    }

    private static double DurationSlots(int denominator, int dots, (int Numerator, int Denominator) tuplet)
    {
        denominator = Math.Clamp(denominator <= 0 ? 16 : denominator, 1, 64);
        dots = Math.Clamp(dots, 0, 2);
        var slots = 16.0 / denominator;
        if (dots == 1) slots *= 1.5;
        else if (dots >= 2) slots *= 1.75;
        if (tuplet.Numerator > 0) slots *= tuplet.Denominator / (double)tuplet.Numerator;
        return Math.Clamp(slots, 0.25, 64);
    }
}
