using System;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track header column's paint path. It draws only the rows inside the track band, and its hit-test cell list is one static array.
/// Does not own: the row geometry (TimelineView) or the body's cull (TimelineView.Drawing).
/// Tests: TrackHeaderColumn.PaintCells, TrackHeaderColumn.CellAt, TrackHeaderColumn.HitTestCells.
/// </summary>
public class TrackHeaderColumnPaintTests
{
    [AvaloniaFact]
    public void HeaderDrawsOnlyTheRowsInTheBand()
    {
        var song = DemoSongFactory.Create();
        var timeline = new TimelineView(song) { PaneHeight = TimelineView.RulerHeight + TimelineView.SectionHeight + 2 * TimelineView.DefaultRowHeight };
        var column = new TrackHeaderColumn(timeline, song);
        var recorder = new TrackRowSelectionTests.RectRecorder();

        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);

        Assert.True(timeline.RowCount > 2, "The demo song should have rows outside the band.");
        Assert.Contains(recorder.Rects, rect => IsRowRect(rect, timeline.RowTop(0), column.ColumnWidth));
        Assert.DoesNotContain(recorder.Rects, rect => IsRowRect(rect, timeline.RowTop(timeline.RowCount - 1), column.ColumnWidth));
    }

    [AvaloniaFact]
    public void HitTestCellsAreOneStaticArray()
    {
        Assert.Same(TrackHeaderColumn.HitTestCells, TrackHeaderColumn.HitTestCells);
        Assert.Equal(Enum.GetValues<TrackRowCells.Cell>(), TrackHeaderColumn.HitTestCells);
    }

    private static bool IsRowRect((double X, double Y, double Width, double Height) rect, double top, double width) =>
        rect.X == 0 && Math.Abs(rect.Y - top) < 0.01 && Math.Abs(rect.Width - width) < 0.01;
}
