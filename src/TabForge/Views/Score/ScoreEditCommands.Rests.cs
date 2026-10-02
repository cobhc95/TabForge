using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

// ScoreEditCommands: with the rest fill on, a duration change or a delete leaves the touched bars' rests merged into the fewest standard rests
// (the model is BarFill.MergeRestRuns) and keeps the selection over the result.
public sealed partial class ScoreEditCommands
{
    private (int M1, int C1, int M2, int C2)? _restSelection;

    /// <summary>Inside an edit: merges the rest runs at <paramref name="touched"/> (only the span they cover when <paramref name="wholeRuns"/> is false) in the edited bars of the active voice; remembers the selection that covers the result.</summary>
    private void NormaliseRests(IReadOnlyCollection<TabCell> touched, bool wholeRuns = true, bool resize = false)
    {
        var track = Track;
        if (track is null || !FillBars) return;
        var set = new HashSet<TabCell>(touched);
        var (m1, c1, m2, c2) = HasSelection ? SelectionRangeOrdered() : (SelectedMeasure, SelectedCell, SelectedMeasure, SelectedCell);
        for (var m = Math.Max(0, m1); m <= Math.Min(m2, track.Measures.Count - 1); m++)
        {
            var barCells = CellsFor(track.Measures[m]);
            var resized = resize ? BarFill.ResizeRests(barCells, SlotsFor(m), set) : null;   // rests keep the value they were given
            if ((resized ?? BarFill.MergeRestRuns(barCells, SlotsFor(m), set, wholeRuns)) is not var (first, last)) continue;
            if (m == m1) c1 = Math.Min(c1, first);
            if (m == m2) c2 = resized is null ? Math.Max(c2, last) : last;   // after a resize the selection ends on the resized rests, not the filler
        }
        if (HasSelection) _restSelection = (m1, c1, m2, c2);
    }

    /// <summary>Inside an edit: a selection of only rests takes the new duration over its whole time span (the span is refilled with rests of that value). False: not applicable, the caller edits the beats instead.</summary>
    private bool RefillSelectedRests(IReadOnlyList<TabCell> selected, int denominator)
    {
        var track = Track;
        if (track is null || !FillBars || !HasSelection || !selected.Any(cell => cell.IsRest) || !selected.All(cell => cell.Notes.Count == 0 && !cell.HasAnnotation)) return false;
        var (m1, c1, m2, c2) = SelectionRangeOrdered();
        m1 = Math.Max(0, m1); m2 = Math.Min(m2, track.Measures.Count - 1);
        var done = new List<(int M, int First, int Last)>();
        for (var m = m1; m <= m2; m++)
        {
            var cells = CellsFor(track.Measures[m], create: _activeVoiceIndex == 1);
            var slots = SlotsFor(m);
            var result = BarFill.RefillRestSpan(cells, slots, m == m1 ? c1 : 0, m == m2 ? c2 : slots - 1, denominator);
            if (result is { } r) done.Add((m, r.First, r.Last));
        }
        if (done.Count == 0) return false;
        _restSelection = (done[0].M, done[0].First, done[^1].M, done[^1].Last);
        return true;
    }

    /// <summary>After the edit: the selection covers the cells the merge produced, so they can be edited or deleted again.</summary>
    private void RestoreRestSelection()
    {
        if (_restSelection is not var (m1, c1, m2, c2)) return;
        _restSelection = null;
        _c.SelectRange(m1, c1, m2, c2);
    }

    private (int M1, int C1, int M2, int C2) SelectionRangeOrdered()
    {
        var (m1, c1, m2, c2) = SelectionRange();
        return m2 < m1 || (m2 == m1 && c2 < c1) ? (m2, c2, m1, c1) : (m1, c1, m2, c2);
    }

    private static bool NoNotes(IReadOnlyList<TabCell> cells) => cells.All(cell => cell.Notes.Count == 0);
}
