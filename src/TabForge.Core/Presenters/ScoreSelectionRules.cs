namespace TabForge.Presenters;

// Owns: the score editor's pure selection rules: clamping a requested range to the track, ordering a range by score position,
//   the select-all range and whether two ranges match. Bars are counted by measureCount; lastCellOf(m) is bar m's last cell.
// Does not own: the selection state and its snapping (Views/Score/EditorSelectionState.cs), events, redraws and the cursor
//   (Views/TabEditorControl.Selection.cs).
// Tests: SelectionModelTests.

public static class ScoreSelectionRules
{
    /// <summary>Clamps a requested range to the track; endCell -1 = the end of the bar. Null for an empty track.</summary>
    public static (int m1, int c1, int m2, int c2)? ClampRange(int measureCount, Func<int, int> lastCellOf, int startMeasure, int startCell, int endMeasure, int endCell)
    {
        if (measureCount <= 0) return null;
        var last = measureCount - 1;
        var m1 = Math.Clamp(startMeasure, 0, last);
        var m2 = Math.Clamp(endMeasure, m1, last);
        var c1 = Math.Clamp(startCell, 0, lastCellOf(m1));
        var lastCell = lastCellOf(m2);
        var c2 = endCell < 0 ? lastCell : Math.Clamp(endCell, 0, lastCell);
        if (m1 == m2 && c2 < c1) c2 = c1;
        return (m1, c1, m2, c2);
    }

    /// <summary>The range with its start before its end in score order.</summary>
    public static (int m1, int c1, int m2, int c2) Ordered((int m1, int c1, int m2, int c2) range)
    {
        var (m1, c1, m2, c2) = range;
        return m2 < m1 || (m2 == m1 && c2 < c1) ? (m2, c2, m1, c1) : range;
    }

    /// <summary>Every beat of the track, or null for an empty track.</summary>
    public static (int m1, int c1, int m2, int c2)? SelectAll(int measureCount, Func<int, int> lastCellOf) =>
        measureCount <= 0 ? null : (0, 0, measureCount - 1, lastCellOf(measureCount - 1));

    /// <summary>True when a shown, ordered range equals a clamped request.</summary>
    public static bool Matches((int m1, int c1, int m2, int c2) shown, (int m1, int c1, int m2, int c2)? clamped) =>
        clamped is { } c && shown == c;
}
