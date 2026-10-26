using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;

namespace TabForge.Avalonia;

// Owns: the track row's properties entries in the Avalonia main window: the gear cell opens the Track properties window of its row, and a double-click
// on the row's body (anywhere but the name and the drawn controls) opens the same window. The instrument icon opens the
//   Choose instrument catalogue for its row with that instrument's family expanded: each tile sounds live on the track (InstrumentChoice),
//   Select commits as one "Change instrument" undo step, Cancel restores the track exactly with no step. An audio track's icon
//   keeps the row press (it is no cell there). The name's double-click stays the rename (ArrangementView), and a double-click on a drawn control
//   does nothing, as its press already acted.
// Does not own: the properties window and its opener (TrackPropertiesWindow, MainWindowTrackMenu), the choice (InstrumentChoice, InstrumentPickerFlow),
//   the cells' drawing and hit boxes (TrackRowCells, TrackHeaderColumn), the rename (TrackNameEditor).
// Tests: TabForge.Avalonia.Tests.TrackRowButtonsTests, TabForge.Avalonia.Tests.TrackIconInstrumentPickerTests.
public static class MainWindowTrackRowOptions
{
    /// <summary>Attaches to every arrangement the panes make, and to the first one (the dock builds it before this runs).</summary>
    public static void Register(MainWindowDocumentPanes panes, MainWindowTabs songs, Window owner, InstrumentPickerFlow picker)
    {
        if (panes.Arrangement is { } existing) Attach(existing, songs, owner, picker);
        panes.ArrangementCreated += arrangement => Attach(arrangement, songs, owner, picker);
    }

    /// <summary>Routes the gear and instrument icon cells and the body double-click of one arrangement's rows to the shown song's track of that row.</summary>
    public static void Attach(ArrangementView arrangement, MainWindowTabs songs, Window owner, InstrumentPickerFlow picker)
    {
        arrangement.Input.TrackCellRequested += (row, cell) =>
        {
            if (cell == TrackRowCells.Cell.Settings) _ = MainWindowTrackMenu.ShowTrackPropertiesAsync(songs.Active, owner, row, picker);
            else if (cell == TrackRowCells.Cell.Kind) _ = ShowInstrumentPickerAsync(songs.Active, owner, row, arrangement, picker);
        };
        arrangement.Header.DoubleTapped += (_, e) =>
        {
            var header = arrangement.Header;
            var point = e.GetPosition(header);
            if (header.NameRowAt(point) >= 0 || header.CellAt(point) is not null) return;
            var row = header.RowAtY(point.Y);
            if (row >= 0) _ = MainWindowTrackMenu.ShowTrackPropertiesAsync(songs.Active, owner, row, picker);
        };
    }

    /// <summary>The icon run: the catalogue over <paramref name="owner"/> with the track's family expanded, live sounds while open, one undo step on Select,
    /// an exact restore on Cancel. The transport keeps playing throughout; the header repaints on every preview and reloads when the run ends.</summary>
    private static async Task ShowInstrumentPickerAsync(SongDocument song, Window owner, int row, ArrangementView arrangement, InstrumentPickerFlow picker)
    {
        if (row < 0 || row >= song.Project.Tracks.Count) return;
        await picker.ChooseAsync(owner, song, song.Project, song.Project.Tracks[row], () => arrangement.Header.InvalidateVisual(), () => arrangement.Header.Reload(song.Project));
    }
}
