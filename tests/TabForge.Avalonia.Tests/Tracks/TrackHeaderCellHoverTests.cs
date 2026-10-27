using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the hover of the drawn track-list cells: the cell under the pointer is tracked by row and cell, and the M box reports the id "mute".
/// Does not own: the cell drawing (TrackHeaderColumn.DrawRow) or the tooltips (TrackHeaderColumn.TooltipOf).
/// </summary>
public class TrackHeaderCellHoverTests
{
    [AvaloniaFact]
    public void PointerOverTheMuteCellHoversMute()
    {
        var song = DemoSongFactory.Create();
        var timeline = new TimelineView(song);
        var column = new TrackHeaderColumn(timeline, song);
        var box = column.CellRect(0, TrackRowCells.Cell.Mute);

        column.UpdateCellHover(box.Center);

        Assert.Equal(0, column.HoverCell!.Value.Row);
        Assert.Equal("mute", TrackHeaderColumn.CellId(column.HoverCell!.Value.Cell));
        column.UpdateCellHover(new global::Avalonia.Point(-100, -100));
        Assert.Null(column.HoverCell);
    }
}
