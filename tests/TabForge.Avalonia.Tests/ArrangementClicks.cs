using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: window-space click points on an arrangement, read from the laid-out header, so a test clicks where the rows are whatever sits above
/// them (the transport bar) instead of at fixed offsets.
/// Does not own: the arrangement's layout (ArrangementView) or the cells' geometry (TrackHeaderColumn).
/// Tests: users are ArrangementViewTests and TrackRowControlsTests.
/// </summary>
internal static class ArrangementClicks
{
    /// <summary>Window-space Y of the top of the track rows: the header's position in the arrangement, below the transport bar.</summary>
    public static double RowsTop(ArrangementView view) => view.Header.TranslatePoint(default, view)!.Value.Y;

    /// <summary>Window-space rectangle of one track-row cell.</summary>
    public static Rect CellInWindow(ArrangementView view, int row, TrackRowCells.Cell cell) =>
        view.Header.CellRect(row, cell).Translate(new Vector(0, RowsTop(view)));
}
