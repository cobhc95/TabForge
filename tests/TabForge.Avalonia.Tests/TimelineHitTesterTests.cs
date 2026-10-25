using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the precedence and boundary rules of TimelineHitTester: a header cell before the grid, the section strip before the rows, the row and
/// bar under the pointer, and the hover query keeping the bar out of the ruler and the sections.
/// Does not own: the geometry maths (TimelineView) or the gestures (TimelineInputController).
/// Tests: this class.
/// </summary>
public class TimelineHitTesterTests
{
    private static (TimelineView View, TrackHeaderColumn Header, TimelineHitTester Tester) Open()
    {
        var song = DemoSongFactory.Create();
        var view = new TimelineView(song);
        var header = new TrackHeaderColumn(view, song);
        return (view, header, new TimelineHitTester(view, header));
    }

    [AvaloniaFact]
    public void HeaderCellWinsOverTheGrid()
    {
        var (view, header, tester) = Open();
        var cell = header.CellRect(0, TrackRowCells.Cell.Mute).Center;
        var hit = tester.Hit(cell, header);
        Assert.Equal(((int Row, TrackRowCells.Cell Cell)?)(0, TrackRowCells.Cell.Mute), hit.HeaderCell);
        Assert.False(hit.OnGrid);
        Assert.False(hit.OnLanes);
        Assert.Equal((-1, -1), (hit.Row, hit.Bar));

        var gridHit = tester.Hit(cell, view);
        Assert.Null(gridHit.HeaderCell);
        Assert.True(gridHit.OnGrid);
    }

    [AvaloniaFact]
    public void SectionStripWinsOverTheRowsAndLanes()
    {
        var (view, _, tester) = Open();
        var slot = view.SectionSlots()[0];
        var strip = new Point(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2);
        var hit = tester.Hit(strip, view);
        Assert.Equal(0, hit.SectionSlot);
        Assert.Equal(view.SectionMarkerAt(slot.MarkerIndex), hit.Section);
        Assert.Equal(-1, hit.Row);
        Assert.Equal(view.BarAtX(strip.X), hit.Bar);
        Assert.False(hit.OnLanes);

        var lane = new Point(view.BarX(2) + 5, view.RowTop(1) + 10);
        var laneHit = tester.Hit(lane, view);
        Assert.Equal(-1, laneHit.SectionSlot);
        Assert.Null(laneHit.Section);
        Assert.Equal((1, 2), (laneHit.Row, laneHit.Bar));
        Assert.True(laneHit.OnGrid);
        Assert.True(laneHit.OnLanes);
    }

    [AvaloniaFact]
    public void HoverKeepsTheBarOutOfTheRulerAndTheSections()
    {
        var (view, _, tester) = Open();
        var lane = new Point(view.BarX(3) + 5, view.RowTop(0) + 10);
        Assert.Equal((3, 0), (tester.HoverAt(lane).Bar, tester.HoverAt(lane).Row));

        var ruler = tester.HoverAt(new Point(view.BarX(3) + 5, TimelineView.RulerHeight / 2));
        Assert.Equal((-1, -1), (ruler.Bar, ruler.Row));
        Assert.Null(ruler.Section);

        var slot = view.SectionSlots()[0];
        var strip = new Point(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2);
        var hover = tester.HoverAt(strip);
        Assert.Equal((-1, -1), (hover.Bar, hover.Row));
        Assert.Equal(view.SectionMarkerAt(slot.MarkerIndex), hover.Section);
    }

    [AvaloniaFact]
    public void BarAndRowBoundariesComeFromTheTimelineGeometry()
    {
        var (view, _, tester) = Open();
        var boundaryX = view.BarX(1);
        var y = view.RowTop(0) + 1;
        Assert.Equal(1, tester.Hit(new Point(boundaryX, y), view).Bar);
        Assert.Equal(0, tester.Hit(new Point(boundaryX - 0.5, y), view).Bar);
        Assert.Equal(view.BarCount - 1, tester.Hit(new Point(view.BarX(view.BarCount) + 10, y), view).Bar);

        var boundaryY = view.RowTop(1);
        var x = view.BarX(0) + 5;
        Assert.Equal(1, tester.Hit(new Point(x, boundaryY), view).Row);
        Assert.Equal(0, tester.Hit(new Point(x, boundaryY - 0.5), view).Row);
        Assert.Equal(-1, tester.Hit(new Point(x, TimelineView.RulerHeight), view).Row);
    }

    [AvaloniaFact]
    public void GridPressOnTheRulerKeepsTheBarForSeeking()
    {
        var (view, _, tester) = Open();
        var hit = tester.Hit(new Point(view.BarX(2) + 5, TimelineView.RulerHeight / 2), view);
        Assert.Equal((-1, 2), (hit.Row, hit.Bar));
        Assert.True(hit.OnGrid);
        Assert.False(hit.OnLanes);
    }
}
