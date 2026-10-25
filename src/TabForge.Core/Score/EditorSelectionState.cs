using TabForge.Models;

namespace TabForge.Views.Score;

// Owns: the score selection's anchor, end and hover as plain state, its beat snapping and the Shift+arrow step of its end.
// Does not own: the shared selection model (Models/SelectionModel.cs), pointer timing (EditorInputController) or redraws.
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests.

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

    /// <summary>True for a range of more than one beat, or a dragged one-beat range.</summary>
    public bool HasSelection => Selecting && (Dragged || AnchorMeasure != EndMeasure || AnchorCell != EndCell);

    /// <summary>True when the pointer dragged out this range, so a range inside one beat counts as a one-beat selection.</summary>
    public bool Dragged { get; private set; }

    public void MarkDragged() { Dragged = true; Changed?.Invoke(); }

    /// <summary>Raised after the range, its end or its dragged flag changed (the editor compares what it shows and announces a real change).</summary>
    public event Action? Changed;

    /// <summary>
    /// True when the range was made as whole bars (a bar or timeline selection), or when it crosses a barline: a cut then takes the bars
    /// (GP5, quiet runs b02 / b09). A range of beats inside one bar is beats, even when it holds every beat of a full bar (b07).
    /// </summary>
    public bool IsWholeBars => HasSelection && (_wholeBars || Crosses());

    private bool Crosses() => AnchorMeasure != EndMeasure;

    /// <summary>True when Shift+Right took the range onto the empty spot after its end beat (GP5 selects it; the end stays on the beat).</summary>
    public bool OnEmptySpot { get; private set; }

    private bool _wholeBars;

    /// <summary>Starts a range at a beat (anchor and end are the same beat).</summary>
    public void Begin(int measure, int cell) { Set(measure, cell, measure, cell); Dragged = false; }

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
    public void Set(int anchorMeasure, int anchorCell, int endMeasure, int endCell, bool wholeBars = false)
    {
        (AnchorMeasure, AnchorCell, EndMeasure, EndCell) = Snapped(anchorMeasure, anchorCell, endMeasure, endCell);
        Selecting = true;
        Dragged = true;
        _wholeBars = wholeBars;
        OnEmptySpot = false;
        Changed?.Invoke();
    }

    /// <summary>Moves only the end of the range, onto a whole beat.</summary>
    public void SetEnd(int measure, int cell, bool onEmptySpot = false)
    {
        OnEmptySpot = onEmptySpot;
        var forward = measure > AnchorMeasure || (measure == AnchorMeasure && cell >= AnchorCell);
        EndMeasure = measure; EndCell = SnapCell(measure, cell, !forward);
        Changed?.Invoke();
    }

    /// <summary>
    /// Steps the end one cursor position (beat starts and the append slot, like the arrow keys) in <paramref name="direction"/>,
    /// crossing barlines; starts the range at the cursor first. False at the song's edge.
    /// </summary>
    public bool ExtendEnd(TrackModel track, Func<MeasureModel, List<TabCell>> cellsFor, Func<int, int> slotsFor, int direction, int cursorMeasure, int cursorCell)
    {
        if (!Selecting) Begin(cursorMeasure, cursorCell);
        var m = EndMeasure; var c = EndCell;
        if (m < 0 || m >= track.Measures.Count) return false;
        var (step, onSpot) = CursorPositions.SelectionStep(cellsFor(track.Measures[m]), c, slotsFor(m), direction, OnEmptySpot, m + 1 >= track.Measures.Count);
        if (step >= 0) c = step;
        else if (direction > 0) { if (m + 1 < track.Measures.Count) { m++; c = 0; } }
        else { m--; c = m >= 0 ? CursorPositions.Allowed(cellsFor(track.Measures[m]), slotsFor(m)).Last() : 0; }
        if (m < 0 || m >= track.Measures.Count) return false;
        SetEnd(m, c, onSpot); MarkDragged();   // Shift+arrows back onto the anchor keep that one beat selected, as GP5
        return true;
    }

    /// <summary>Drops the range.</summary>
    public void Clear()
    {
        Selecting = false;
        Dragged = false;
        _wholeBars = false;
        OnEmptySpot = false;
        AnchorMeasure = EndMeasure = -1;
        Changed?.Invoke();
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

    /// <summary>A key for the hover outline inside bars first..last: zero when the outline is not there, so a system without it keeps its drawing.</summary>
    public long HoverKeyIn(int first, int last)
    {
        if (HoverMeasure < first || HoverMeasure > last || HoverCell < 0) return 0;
        unchecked { return (HoverMeasure * 1000003L + HoverCell + 1) * 0x2545F491L; }
    }

    /// <summary>Forgets the hovered beat; true when there was one.</summary>
    public bool ClearHover()
    {
        if (HoverMeasure == -1) return false;
        HoverMeasure = HoverCell = -1;
        return true;
    }
}
