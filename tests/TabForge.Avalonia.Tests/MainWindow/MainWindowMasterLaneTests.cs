using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Master row's real pointer gestures in the main window: the pan drag is one undo step on the song's master pan, the volume drag sets the
//   transport's master knob, and the FX click opens the master chain while the power click bypasses it as one undo step.
// Does not own: the row's drawing (MasterLaneRowTests), the track row cells (TrackRowControlsTests), the master edits' engine sync (MainWindowEngineSync).
// Tests: this class.
public class MainWindowMasterLaneTests
{
    [AvaloniaFact]
    public void MasterPanDragIsOneUndoStepOnTheSongsMasterPan()
    {
        var main = Open(out var arrangement);
        var song = main.Songs.Active;
        var before = song.Project.Mixer.MasterPan;
        var pan = ArrangementClicks.CellInWindow(arrangement, TrackHeaderColumn.MasterRow, TrackRowCells.Cell.Pan);
        Drag(main, arrangement, pan, pan.Right);
        Assert.Equal(63, song.Project.Mixer.MasterPan);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(before, song.Project.Mixer.MasterPan);
        main.Close();
    }

    [AvaloniaFact]
    public void MasterVolumeDragSetsTheTransportKnob()
    {
        var main = Open(out var arrangement);
        var song = main.Songs.Active;
        var panBefore = song.Project.Mixer.MasterPan;
        var volume = ArrangementClicks.CellInWindow(arrangement, TrackHeaderColumn.MasterRow, TrackRowCells.Cell.Volume);
        Drag(main, arrangement, volume, volume.Center.X);
        Assert.Equal(50.0, arrangement.Transport.MasterVolumeKnob.Value);
        Assert.Equal(panBefore, song.Project.Mixer.MasterPan);
        main.Close();
    }

    [AvaloniaFact]
    public void MasterFxClickOpensTheMasterChainAndPowerBypassesItAsOneUndoStep()
    {
        var main = Open(out var arrangement);
        var song = main.Songs.Active;
        var before = song.Project.Mixer.Master.On;
        Click(main, arrangement, ArrangementClicks.CellInWindow(arrangement, TrackHeaderColumn.MasterRow, TrackRowCells.Cell.Fx));
        var chain = Assert.Single(main.OwnedWindows.OfType<FxChainWindow>());
        Assert.Equal(FxChainPresenter.WindowTitle(MixerBuses.MasterTrack(song.Project).Name), chain.Title);

        Click(main, arrangement, ArrangementClicks.CellInWindow(arrangement, TrackHeaderColumn.MasterRow, TrackRowCells.Cell.FxPower));
        Assert.NotEqual(before, song.Project.Mixer.Master.On);
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.Equal(before, song.Project.Mixer.Master.On);

        foreach (var open in main.OwnedWindows.ToList()) open.Close();
        main.Close();
    }

    // The first arrangement of a new main window, with the master lane shown so its row has cells.
    private static MainWindow Open(out ArrangementView arrangement)
    {
        var main = new MainWindow();
        main.Show();
        arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        arrangement.ShowMasterLane(true);
        main.UpdateLayout();
        return main;
    }

    // A press at the left of a slider, a move to <paramref name="endX"/> on its row and a release there, all as window pointer events.
    private static void Drag(Window main, ArrangementView arrangement, Rect slider, double endX)
    {
        var start = InMain(main, arrangement, new Point(slider.X + 4, slider.Center.Y));
        var end = InMain(main, arrangement, new Point(endX, slider.Center.Y));
        main.MouseDown(start, MouseButton.Left);
        main.MouseMove(end);
        main.MouseUp(end, MouseButton.Left);
    }

    // A press and release at the centre of a cell.
    private static void Click(Window main, ArrangementView arrangement, Rect cell)
    {
        var point = InMain(main, arrangement, cell.Center);
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
    }

    private static Point InMain(Window main, ArrangementView arrangement, Point point) =>
        arrangement.TranslatePoint(point, main) ?? throw new InvalidOperationException("The arrangement is not in the main window.");
}
