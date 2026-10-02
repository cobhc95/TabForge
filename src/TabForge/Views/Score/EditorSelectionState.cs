using TabForge.Models;

namespace TabForge.Views.Score;

/// <summary>
/// The editor's score selection and hover as plain state: an anchor and an end (bar, cell), whether a range is being selected,
/// and the hovered beat. The shared selection model stays the source of truth; this is how the editor holds what it shows.
/// </summary>
internal sealed class EditorSelectionState
{
    public int AnchorMeasure { get; private set; } = -1;
    public int AnchorCell { get; private set; } = -1;
    public int EndMeasure { get; private set; } = -1;
    public int EndCell { get; private set; } = -1;
    public bool Selecting { get; private set; }
    public int HoverMeasure { get; set; } = -1;
    public int HoverCell { get; set; } = -1;

    /// <summary>True for a range of more than one beat.</summary>
    public bool HasSelection => Selecting && (AnchorMeasure != EndMeasure || AnchorCell != EndCell);

    /// <summary>Starts a range at a beat (anchor and end are the same beat).</summary>
    public void Begin(int measure, int cell) => Set(measure, cell, measure, cell);

    /// <summary>The cells of a bar of the active voice (null: unknown bar); lets a range snap to whole beats.</summary>
    public Func<int, IReadOnlyList<TabCell>?>? CellsOf { get; set; }

    /// <summary>Both ends of a range moved onto whole beats: the start onto the beat it falls in (or the next one), the end onto the beat it falls in (or the previous one). Ends in a bar without cells stay as they are.</summary>
    public (int m1, int c1, int m2, int c2) Snapped(int anchorMeasure, int anchorCell, int endMeasure, int endCell)
    {
        var forward = anchorMeasure < endMeasure || (anchorMeasure == endMeasure && anchorCell <= endCell);
        return (anchorMeasure, SnapCell(anchorMeasure, anchorCell, forward), endMeasure, SnapCell(endMeasure, endCell, !forward));
    }

    private int SnapCell(int measure, int cell, bool preferNext) => CellsOf?.Invoke(measure) is { } cells ? CursorPositions.SnapToBeat(cells, cell, preferNext) : cell;

    /// <summary>Sets both ends (snapped to whole beats) and turns range selection on.</summary>
    public void Set(int anchorMeasure, int anchorCell, int endMeasure, int endCell)
    {
        (AnchorMeasure, AnchorCell, EndMeasure, EndCell) = Snapped(anchorMeasure, anchorCell, endMeasure, endCell);
        Selecting = true;
    }

    /// <summary>Moves only the end of the range, onto a whole beat.</summary>
    public void SetEnd(int measure, int cell)
    {
        var forward = measure > AnchorMeasure || (measure == AnchorMeasure && cell >= AnchorCell);
        EndMeasure = measure; EndCell = SnapCell(measure, cell, !forward);
    }

    /// <summary>Drops the range.</summary>
    public void Clear()
    {
        Selecting = false;
        AnchorMeasure = EndMeasure = -1;
    }

    /// <summary>The anchor and the end as (bar, cell, bar, cell), in the order they were set.</summary>
    public (int m1, int c1, int m2, int c2) Range() => (AnchorMeasure, AnchorCell, EndMeasure, EndCell);

    /// <summary>
    /// Keeps a range inside a song of <paramref name="barCount"/> bars: a range whose bars are all gone is dropped, an end past the last bar
    /// moves to that bar's last cell (<paramref name="lastCell"/> is that cell's index).
    /// </summary>
    public void Coerce(int barCount, int lastCell)
    {
        if (!Selecting) return;
        var last = barCount - 1;
        if (last < 0 || Math.Min(AnchorMeasure, EndMeasure) > last) { Clear(); return; }
        if (AnchorMeasure > last) { AnchorMeasure = last; AnchorCell = lastCell; }
        if (EndMeasure > last) { EndMeasure = last; EndCell = lastCell; }
    }

    /// <summary>Forgets the hovered beat; true when there was one.</summary>
    public bool ClearHover()
    {
        if (HoverMeasure == -1) return false;
        HoverMeasure = HoverCell = -1;
        return true;
    }
}
