using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the track row FX cells of the arrangement: the FX part opens that row's track chain window, the power part switches the track between
//   its plug-in chain and Windows MIDI as one undo step and changes the cell's drawn state.
// Tests: this class.
public class MainWindowTrackFxCellsTests
{
    private const int Row = 2;

    [AvaloniaFact]
    public void FxCellOpensTheChainOfItsRowAndPowerSwitchesTheChain()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var owner = new Window();
        owner.Show();
        _ = new TabForge.Avalonia.Views.Fx.FxChainWindows(owner, () => song, null, null, new TabForge.Plugins.PluginTrust());   // the main window's registry: the chain window opens through it
        var arrangement = new ArrangementView(song.Project, song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 420, Content = arrangement };
        window.Show();
        MainWindowTrackFxCells.Attach(arrangement, tabs, owner, null, () => new PluginSettings());
        var track = song.Project.Tracks[Row];

        var fx = ArrangementClicks.CellInWindow(arrangement, Row, TrackRowCells.Cell.Fx).Center;
        window.MouseDown(fx, MouseButton.Left);
        window.MouseUp(fx, MouseButton.Left);
        var chain = Assert.Single(owner.OwnedWindows.OfType<FxChainWindow>());
        Assert.Equal(FxChainPresenter.WindowTitle(track.Name), chain.Title);

        var before = track.SoundSource;
        var power = ArrangementClicks.CellInWindow(arrangement, Row, TrackRowCells.Cell.FxPower).Center;
        window.MouseDown(power, MouseButton.Left);
        window.MouseUp(power, MouseButton.Left);
        Assert.NotEqual(before, track.SoundSource);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(before, song.Project.Tracks[Row].SoundSource);

        foreach (var open in owner.OwnedWindows.ToList()) open.Close();
        window.Close();
        owner.Close();
    }

    // The first arrangement is built by the dock before the window wires its cells; the real window must still route its FX cells.
    [AvaloniaFact]
    public void FirstArrangementOfTheMainWindowOpensTheChainAndTogglesPower()
    {
        // The window starts empty now: this test needs the demo song's tracks, so it hands the window that song.
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song);
        main.Show();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var track = main.Songs.Active.Project.Tracks[Row];
        var before = track.SoundSource;

        ClickInMain(main, arrangement, ArrangementClicks.CellInWindow(arrangement, Row, TrackRowCells.Cell.Fx));
        Assert.Single(main.OwnedWindows.OfType<FxChainWindow>());

        ClickInMain(main, arrangement, ArrangementClicks.CellInWindow(arrangement, Row, TrackRowCells.Cell.FxPower));
        Assert.NotEqual(before, track.SoundSource);

        foreach (var open in main.OwnedWindows.ToList()) open.Close();
        main.Songs.Active.MarkClean();   // the power switch edited the song; a dirty song would open the save prompt on close, which nothing answers here
        main.Close();
    }

    // A press and release at a cell rectangle given in the arrangement's coordinates, sent to the main window.
    private static void ClickInMain(Window main, ArrangementView arrangement, Rect cell)
    {
        var point = arrangement.TranslatePoint(cell.Center, main) ?? throw new System.InvalidOperationException("The arrangement is not in the main window.");
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
    }
}
