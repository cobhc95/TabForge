using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

// Owns: the duration that note and rest entry writes: the writing duration of an empty slot or placeholder rest, the duration keys, and the base duration for steps.
// Does not own: the rest-run merge (ScoreEditCommands.Rests.cs) or the marks, ties and beams (ScoreEditCommands.Marks.cs).
// Tests: no dedicated test class.

// ScoreEditCommands: which duration note and rest entry writes, as in GP5 on top of the rest fill. An empty slot or a placeholder rest
// (see WritingDuration) takes the writing duration and duration keys on it change only the writing duration; a note or a written rest keeps
// its own length. After undo / redo the writing duration follows the beat under the cursor, so an undone value does not leak into new notes.
public sealed partial class ScoreEditCommands
{
    /// <summary>The cursor's beat stands for the writing duration: an empty slot or a rest with no note after it in its bar.</summary>
    private bool Placeholder(TabCell cell)
        => !ReferenceEquals(cell, _restJustWritten) && (CurrentMeasure() is not { } bar || WritingDuration.Applies(_c.CellsFor(bar), cell));

    // The rest R last wrote on an empty spot: a real beat (as GP5, duration keys and "." change it), though the rest fill's rests look the same.
    // ponytail: remembered by reference, so undo, reload or another edit that replaces the cell makes it a placeholder again; a saved
    // "written rest" flag on TabCell is the upgrade if that matters.
    private TabCell? _restJustWritten;

    /// <summary>
    /// Inside an edit: with the rest fill on, the bar around a note just written becomes gap-free (the tail and every gap get real rest cells).
    /// The plain fill leaves bars with tuplets or free positions alone, and never moves, drops or shortens an existing note.
    /// </summary>
    private void FillBarAfterWrite()
    {
        if (!_c.Tools.FillBarsWithRests || CurrentMeasure() is not { } measure) return;
        if (measure.FreeTime || measure.Anacrusis || measure.SimileOneBar || measure.SimileTwoBar) return;
        var cells = _c.CellsFor(measure);
        if (!BarFill.IsPlain(cells)) return;
        BarFill.FillCells(cells, _c.SlotsFor(_c.SelectedMeasure), always: true);
    }

    private void ApplyPendingDuration(TabCell cell)
    {
        ForgetWritingMarks();   // the note takes the dot / triplet: an undo now undoes the note
        // A note or written rest keeps its own length; an empty slot or placeholder rest takes the writing duration (the rest fill refills the remainder).
        if (!Placeholder(cell))
        {
            cell.IsRest = false;
            return;
        }
        cell.DurationDenominator = _c.Tools.DurationDenominator;
        cell.Dots = _c.Tools.Dots;
        cell.IsTriplet = _c.Tools.Triplet;
        cell.TupletNumerator = _c.Tools.TupletNumerator;
        cell.TupletDenominator = _c.Tools.TupletDenominator;
        cell.IsRest = false;
    }

    // The beat whose own value +/- and "." start from: the first selected beat, or the cursor beat unless it is a placeholder (then the writing duration).
    private TabCell? ValueBeat()
    {
        if (_c.HasSelection) return ToolCells().FirstOrDefault(c => c.Notes.Count > 0 || c.IsRest);
        var cell = CurrentCell();
        return cell is not null && (cell.Notes.Count > 0 || cell.IsRest) && !Placeholder(cell) ? cell : null;
    }

    // +/- step from the selected beat's own value (not a stale toolbar value).
    private int StepBaseDuration() => ValueBeat()?.DurationDenominator ?? _c.Tools.DurationDenominator;

    // The dots the selected beat actually has (the toolbar's CurrentDots can be stale).
    private int BeatDots() => ValueBeat()?.Dots ?? _c.Tools.Dots;

    /// <summary>
    /// R without a selection: an empty slot or placeholder rest becomes a rest of the writing duration (the fill refills the rest of the bar); a note
    /// becomes a written rest and a written rest stays one (quiet GP5, work/gp5diff/i4micro4 i23, i24: R R leaves a rest that End and Right treat as a beat).
    /// </summary>
    private void EnterRestAtCursor()
    {
        var cell = CurrentCell(create: true); if (cell is null) return;
        if (Placeholder(cell))
        {
            RunEdit(() => { ApplyPendingDuration(cell); cell.IsRest = true; cell.WrittenRest = true; FillBarAfterWrite(); return true; });
            _restJustWritten = cell;
            return;
        }
        RunEdit(() =>
        {
            if (cell.IsRest && cell.WrittenRest) return false;
            cell.IsRest = true; cell.WrittenRest = true; cell.Notes.Clear();
            FillBarAfterWrite();
            return true;
        });
    }

    /// <summary>Inside an edit, after a duration change on a selection: each touched bar closes up (the beats after a changed one move, no rests
    /// open between notes) and the selection covers the moved beats. False when a bar has tuplets or free positions (the rests are resized instead).</summary>
    private bool CloseUpSelection(IReadOnlyList<TabCell> selected)
    {
        var track = _c.Track;
        if (track is null || !_c.Tools.FillBarsWithRests || selected.Count == 0) return false;
        var (m1, _, m2, _) = _c.HasSelection ? SelectionRangeOrdered() : (_c.SelectedMeasure, 0, _c.SelectedMeasure, 0);
        m1 = Math.Max(0, m1); m2 = Math.Min(m2, track.Measures.Count - 1);
        var bars = Enumerable.Range(m1, Math.Max(0, m2 - m1 + 1)).Select(m => (M: m, Cells: _c.CellsFor(track.Measures[m], create: _c.ActiveVoiceIndex == 1))).ToList();
        if (bars.Count == 0 || !bars.All(b => BarFill.IsPlain(b.Cells) || ShortNoteFill.Packed(b.Cells))) return false;
        var keep = selected.ToHashSet();
        foreach (var (m, cells) in bars) Services.EditCommands.CloseUp(cells, _c.SlotsFor(m), keep);
        if (!_c.HasSelection) { _c.SelectedCell = Math.Max(0, bars[0].Cells.IndexOf(selected[0])); return true; }   // the cursor stays on its beat
        _restSelection = (m1, Math.Max(0, bars[0].Cells.IndexOf(selected[0])), m2, Math.Max(0, bars[^1].Cells.IndexOf(selected[^1])));
        return true;
    }

    /// <summary>Inside an edit, after the length of the beat under the cursor changed: a note's bar closes up (the beats after it follow, the freed time
    /// goes to the fill rests at the bar end); a rest is resized in place.</summary>
    private void FollowLengthChange(TabCell cell)
    {
        var touched = new[] { cell };
        if (cell.Notes.Count == 0 || !CloseUpSelection(touched)) NormaliseRests(touched, resize: true);
    }

    /// <summary>The duration a note on a free slot takes, set as a plain or dotted value (a tuplet mark is cleared); no song edit.</summary>
    public void SetWritingDuration(int denominator, int dots)
    {
        if (Array.IndexOf(MusicTime.AllDenominators, denominator) < 0) return;
        dots = Math.Clamp(dots, 0, 2);
        if (_c.Tools.DurationDenominator == denominator && _c.Tools.Dots == dots && !_c.Tools.Triplet
            && _c.Tools.TupletNumerator == 0 && _c.Tools.TupletDenominator == 0) return;
        _c.Tools.DurationDenominator = denominator;
        _c.Tools.Dots = dots;
        _c.Tools.Triplet = false;
        _c.Tools.TupletNumerator = 0;
        _c.Tools.TupletDenominator = 0;
    }

    // As GP5, "." or the triplet (or +/-) on an empty spot is an undo step of its own: the writing duration before it, while the cursor stays on that spot.
    // Each mark is one step (Shorter, Dot, Undo takes back only the dot: quiet GP5 micro6 x07). Any song edit ends them (RunEdit); Redo after such
    // an undo puts the mark back (_writingRedo).
    private readonly Stack<(int Measure, int Cell, int Denominator, int Dots, bool Triplet, int TupletNumerator, int TupletDenominator)> _writingUndo = new(), _writingRedo = new();

    private void ForgetWritingMarks() { _writingUndo.Clear(); _writingRedo.Clear(); }

    private (int, int, int, int, bool, int, int) WritingNow() => (_c.SelectedMeasure, _c.SelectedCell, _c.Tools.DurationDenominator, _c.Tools.Dots, _c.Tools.Triplet, _c.Tools.TupletNumerator, _c.Tools.TupletDenominator);

    private void RememberWritingForUndo()
    {
        _writingRedo.Clear();
        if (_c.HasSelection || CurrentCell() is { } cell && !Placeholder(cell)) return;
        _writingUndo.Push(WritingNow());
    }

    /// <summary>As GP5, the first beat of an inserted bar copies the last beat before it (a plain quarter when none comes before).</summary>
    public void WriteLikeBeatBefore(int bar) => WriteLikeBeatBefore(bar, 0);

    /// <summary>The writing duration becomes that of the last note before cell <paramref name="cell"/> of bar <paramref name="bar"/> (a quarter when there is none).</summary>
    public void WriteLikeBeatBefore(int bar, int cell)
    {
        var before = _c.Track?.Measures.Take(Math.Max(0, bar) + 1).SelectMany((m, i) => i < bar ? _c.CellsFor(m) : _c.CellsFor(m).Take(Math.Max(0, cell))).LastOrDefault(c => c.Notes.Count > 0);
        (_c.Tools.DurationDenominator, _c.Tools.Dots, _c.Tools.Triplet, _c.Tools.TupletNumerator, _c.Tools.TupletDenominator) = before is null ? (4, 0, false, 0, 0)
            : (before.DurationDenominator, before.Dots, before.IsTriplet, before.TupletNumerator, before.TupletDenominator);
        ForgetWritingMarks();
    }

}
