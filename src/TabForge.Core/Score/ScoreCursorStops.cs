using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Views.Score;

// Owns: the editor's per-bar cursor stops for one track, as CursorPositions gives them, for ScoreCursorNavigator.
// Does not own: the stop rules (CursorPositions.cs), the move rules (ScoreCursorNavigator.cs).
// Tests: no dedicated test class.
internal sealed class ScoreCursorStops(TrackModel track, Func<MeasureModel, List<TabCell>> cellsFor, Func<int, int> slotsFor) : IBarCursorStops
{
    public int MeasureCount => track.Measures.Count;
    private List<TabCell> Bar(int measure) => cellsFor(track.Measures[measure]);
    public IReadOnlyList<TabCell> Cells(int measure) => Bar(measure);
    public int Snap(int measure, int cell) => measure >= 0 && measure < MeasureCount ? CursorPositions.Snap(Bar(measure), cell, slotsFor(measure)) : 0;
    public int RightTarget(int measure, int cell) => CursorPositions.RightTarget(Bar(measure), cell, slotsFor(measure), pad: true);
    public bool AtEnd(int measure, int cell) => CursorPositions.AtEnd(Bar(measure), cell, slotsFor(measure));
    public int Previous(int measure, int cell) => CursorPositions.Previous(Bar(measure), cell, slotsFor(measure));
    public int BarEndCursor(int measure) => CursorPositions.BarEndCursor(Bar(measure), slotsFor(measure));
    public int EndTarget(int measure, int cell) => CursorPositions.EndTarget(Bar(measure), cell, slotsFor(measure));
    public int LastWritten(int measure) => CursorPositions.LastWritten(Bar(measure), slotsFor(measure));
    public int LastCell(int measure) => CursorPositions.LastCell(Bar(measure), slotsFor(measure));
}
