using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the track row buttons and double-clicks in the real main window: a press at each drawn cell's centre reaches the track header (no overlay
//   sits above the rows), the gear, FX and power cells respond to it, and a double-click on the row body opens the track properties while one on
//   the name starts the rename.
// Does not own: the cells' geometry (TrackRowCells), the header's drawing (TrackHeaderColumn), the actions behind each cell
//   (MainWindowTrackFxCells, MainWindowTrackRowOptions), the rename (TrackNameEditor).
// Tests: this class.
public class TrackRowButtonsTests
{
    private const int Row = 2;

    // The window starts empty now: the row tests need the demo song's ten tracks, so the test hands the window that song.
    private static MainWindow OpenDemoWindow()
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song);
        main.Show();
        return main;
    }

    private static readonly TrackRowCells.Cell[] DrawnCells =
    {
        TrackRowCells.Cell.Settings, TrackRowCells.Cell.Arm, TrackRowCells.Cell.Fx, TrackRowCells.Cell.FxPower,
        TrackRowCells.Cell.Mute, TrackRowCells.Cell.Solo, TrackRowCells.Cell.Volume, TrackRowCells.Cell.Pan,
    };

    [AvaloniaFact]
    public void EveryDrawnTrackRowCellIsPressedThroughTheHeader()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var missed = new List<string>();
        foreach (var cell in DrawnCells)
        {
            var reached = false;
            EventHandler<PointerPressedEventArgs> mark = (_, _) => reached = true;
            arrangement.Header.AddHandler(InputElement.PointerPressedEvent, mark, RoutingStrategies.Tunnel, handledEventsToo: true);
            Click(main, PointInMain(main, arrangement, Row, cell));
            arrangement.Header.RemoveHandler(InputElement.PointerPressedEvent, mark);
            if (!reached) missed.Add(cell.ToString());
        }
        foreach (var open in main.OwnedWindows.ToList()) open.Close();
        main.Songs.Active.MarkClean();   // the presses edit the song; a dirty song would open the save prompt on close, which nothing answers here
        main.Close();
        Assert.True(missed.Count == 0, "Presses that did not reach the header: " + string.Join(", ", missed));
    }

    [AvaloniaFact]
    public void GearOpensTheTrackPropertiesOfItsRow()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        Click(main, PointInMain(main, arrangement, Row, TrackRowCells.Cell.Settings));
        var opened = main.OwnedWindows.OfType<TrackPropertiesWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void PowerCellTogglesTheTrackChainAndBack()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var track = main.Songs.Active.Project.Tracks[Row];
        var before = track.SoundSource;
        Click(main, PointInMain(main, arrangement, Row, TrackRowCells.Cell.FxPower));
        Assert.NotEqual(before, track.SoundSource);
        Click(main, PointInMain(main, arrangement, Row, TrackRowCells.Cell.FxPower));
        Assert.Equal(before, track.SoundSource);
        main.Songs.Active.MarkClean();   // the power switch edited the song; a dirty song would open an unanswered save prompt on close
        main.Close();
    }

    [AvaloniaFact]
    public void DoubleClickOnTheRowBodyOpensTrackProperties()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var body = BodyPointOfRow(main, arrangement, Row);
        DoubleClick(main, body, () => main.OwnedWindows.OfType<TrackPropertiesWindow>().Any());
        var opened = main.OwnedWindows.OfType<TrackPropertiesWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void DoubleClickOnTheNameStartsTheRename()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var (left, _) = TrackRowCells.Column("name", arrangement.Header.NameColumnWidth);
        var rowY = arrangement.Header.CellRect(Row, TrackRowCells.Cell.Settings).Center.Y;
        DoubleClick(main, HeaderPointInMain(main, arrangement, new Point(left + 4, rowY)), () => main.GetLogicalDescendants().OfType<TextBox>().Any(box => box.IsVisible));
        Assert.Empty(main.OwnedWindows.OfType<TrackPropertiesWindow>());
        Assert.Contains(main.GetLogicalDescendants().OfType<TextBox>(), box => box.IsVisible);
        main.Close();
    }

    /// <summary>The centre of a row's drawn cell in the main window's coordinates.</summary>
    private static Point PointInMain(Window main, ArrangementView arrangement, int row, TrackRowCells.Cell cell) =>
        HeaderPointInMain(main, arrangement, arrangement.Header.CellRect(row, cell).Center);

    /// <summary>Converts a point in the track header's coordinates to the main window's.</summary>
    private static Point HeaderPointInMain(Window main, ArrangementView arrangement, Point headerLocal)
    {
        var origin = arrangement.Header.TranslatePoint(default, arrangement) ?? default;
        var inArrangement = new Point(headerLocal.X + origin.X, headerLocal.Y + origin.Y);
        return arrangement.TranslatePoint(inArrangement, main) ?? throw new InvalidOperationException("The arrangement is not in the main window.");
    }

    /// <summary>A point on the row's body: not on the name column and not on a drawn cell (a press there is a control's, not the body's).</summary>
    private static Point BodyPointOfRow(Window main, ArrangementView arrangement, int row)
    {
        var header = arrangement.Header;
        var y = header.CellRect(row, TrackRowCells.Cell.Settings).Center.Y;
        // Starts left of the splitter: its grab area overlaps the header's right edge by half a bar, so a press there is the splitter's.
        for (var x = header.Bounds.Width - DockSplitter.BarThickness; x >= 0; x -= 2)
        {
            var local = new Point(x, y);
            if (header.NameRowAt(local) < 0 && header.CellAt(local) is null) return HeaderPointInMain(main, arrangement, local);
        }
        throw new InvalidOperationException($"Row {row} has no body point outside the name and the cells.");
    }

    private static void Click(Window main, Point point)
    {
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
    }

    // Two clicks count as a double-click only within the system interval of wall-clock time; on a loaded machine (parallel tests) the pair can
    // straddle it, so the pair is repeated until the double-click's effect shows.
    private static void DoubleClick(Window main, Point point, Func<bool> took)
    {
        for (var attempt = 0; attempt < 5 && !took(); attempt++)
        {
            Click(main, point);
            Click(main, point);
        }
    }
}
