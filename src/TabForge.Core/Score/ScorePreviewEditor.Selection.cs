using TabForge.Controllers;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Views.Score;

// Owns: the preview editor's side of the shared selection: the ISelectionEditor members (clamped SelectRange that moves the cursor, ordered cell
//   range, match test, clear), Select all, and the SelectionChanged event that fires when the range the score shows really changed.
// Does not own: the range state (EditorSelectionState), the shared model and its sync (SelectionModel, SelectionSync) or the views' repaint.
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests.
internal sealed partial class ScorePreviewEditor
{
    // What the last SelectionChanged announced: whether a range showed, and its ordered cell bounds. UI thread only.
    private (bool Has, (int, int, int, int) Range) _announced;

    /// <summary>Raised after the range the score shows changed (mouse, Shift+arrows, Select all, a clear or an edit that ended it).</summary>
    internal event EventHandler? SelectionChanged;

    private void AnnounceSelection()
    {
        var now = (Sel.HasSelection, Sel.HasSelection ? ScoreSelectionRules.Ordered(Sel.Range()) : (-1, -1, -1, -1));
        if (now == _announced) return;
        _announced = now;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inclusive cell bounds of the range, ordered by score position; the cursor cell when nothing is selected.</summary>
    public (int StartMeasure, int StartCell, int EndMeasure, int EndCell) SelectionCellRange => SelectionRange();

    /// <summary>Selects from the start cell to the end cell (inclusive, end cell -1 = the end of the bar), clamped to the track and snapped to beats; the cursor goes to the start.</summary>
    public void SelectRange(int startMeasure, int startCell, int endMeasure, int endCell)
    {
        if (ClampedRange(startMeasure, startCell, endMeasure, endCell) is not var (m1, c1, m2, c2)) return;
        _probe.Cursor = (m1, c1, SelectedString);
        Sel.Set(m1, c1, m2, c2, wholeBars: startCell <= 0 && endCell < 0);
    }

    /// <summary>True when the score already shows exactly this range (clamped as <see cref="SelectRange"/> clamps it).</summary>
    public bool SelectionMatches(int startMeasure, int startCell, int endMeasure, int endCell) =>
        Sel.HasSelection && ScoreSelectionRules.Matches(SelectionRange(), ClampedRange(startMeasure, startCell, endMeasure, endCell));

    /// <summary>Raised after the loop tint changed, so the view repaints.</summary>
    internal event EventHandler? LoopTintChanged;

    /// <summary>Lights whole bars like selected notes without selecting them (display only: no loop, no edit target); null or empty removes them.</summary>
    public void SetMirrorBars(IReadOnlyList<(int First, int Last)>? bars)
    {
        if (bars is { Count: 0 }) bars = null;
        if (bars is null ? _probe.MirrorBars is null : _probe.MirrorBars is { } now && now.SequenceEqual(bars)) return;
        _probe.MirrorBars = bars;
        LoopTintChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True while scattered bars are lit by <see cref="SetMirrorBars"/>.</summary>
    public bool HasMirrorBars => _probe.MirrorBars is not null;

    /// <summary>Tints the notes of a loop region lightly (not selected); null removes the tint.</summary>
    public void ShowLoopTint((int StartMeasure, int StartCell, int EndMeasure, int EndCell)? range)
    {
        var tint = range is { } r && ClampedRange(r.StartMeasure, r.StartCell, r.EndMeasure, r.EndCell) is var (m1, c1, m2, c2) ? (m1, c1, m2, c2) : ((int, int, int, int)?)null;
        if (_probe.LoopTint == tint) return;
        _probe.LoopTint = tint;
        LoopTintChanged?.Invoke(this, EventArgs.Empty);
    }

    // The editor announces every change itself, so the notify flag has nothing to switch.
    void ISelectionEditor.ClearSelection(bool notify) => Sel.Clear();

    /// <summary>Selects every beat of the track.</summary>
    internal void SelectAll()
    {
        if (ScoreSelectionRules.SelectAll(TrackNow.Measures.Count, LastCellOf) is not var (m1, c1, m2, c2)) return;
        Sel.Set(m1, c1, m2, c2);
    }

    private (int, int, int, int)? ClampedRange(int startMeasure, int startCell, int endMeasure, int endCell) =>
        ScoreSelectionRules.ClampRange(TrackNow.Measures.Count, LastCellOf, startMeasure, startCell, endMeasure, endCell) is var (m1, c1, m2, c2)
            ? Sel.Snapped(m1, c1, m2, c2)
            : null;

    private int LastCellOf(int measure) => CursorPositions.LastCell(VoiceCells(TrackNow.Measures[measure]), _probe.SlotsFor(measure));
}
