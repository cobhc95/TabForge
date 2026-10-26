using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: an armed track's sub-row in the arrangement: arming opens the sub-row under the track's row (the rows below move and the timeline lane
/// grows with them), its monitor toggle and input picker are present and edit the song, its meter shows the input level, disarming closes it,
/// and the track list and the timeline stay aligned row by row.
/// Does not own: the sub-row's drawing and sizes (ArmedRowPainter), the arm cell's pointer handling (TimelineInputController), the engine sync.
/// Tests: this class.
/// </summary>
public class ArmedRowTests
{
    private const int ArmedRow = 2;

    /// <summary>Opens an arrangement of at least four tracks in a window; returns the window, the view and the song.</summary>
    private static (Window Window, ArrangementView View, SongProject Project) Open()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < 4) song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio));
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view, song.Project);
    }

    private static void SetArm(ArrangementView view, int row, bool armed) =>
        DocumentEdits.Run(view.Document, project => { project.Tracks[row].RecordArm = armed; return true; });

    [AvaloniaFact]
    public void ArmingATrackOpensItsSubRowWithAMonitorAndAnInputPicker()
    {
        var (window, view, project) = Open();
        var rowBefore = view.Timeline.RowTop(ArmedRow + 1) - view.Timeline.RowTop(ArmedRow);

        SetArm(view, ArmedRow, true);
        window.UpdateLayout();

        Assert.Equal(rowBefore + ArmedRowPainter.SubRowHeight, view.Timeline.RowTop(ArmedRow + 1) - view.Timeline.RowTop(ArmedRow), 6);
        Assert.Single(view.ArmedRows.Children.OfType<TrackInputPicker>());
        var monitor = Assert.Single(view.ArmedRows.Children.OfType<ToggleButton>());
        Assert.Equal(project.Tracks[ArmedRow].MonitorInput, monitor.IsChecked == true);
        window.Close();
    }

    [AvaloniaFact]
    public void UndoOfAnArmChangeRemovesAndRestoresTheControlsOfTheLayer()
    {
        var (window, view, _) = Open();
        SetArm(view, ArmedRow, true);
        Assert.Single(view.ArmedRows.Children.OfType<TrackInputPicker>());

        Assert.NotNull(DocumentEdits.Undo(view.Document));
        Assert.Empty(view.ArmedRows.Children.OfType<TrackInputPicker>());

        Assert.NotNull(DocumentEdits.Redo(view.Document));
        Assert.Single(view.ArmedRows.Children.OfType<TrackInputPicker>());
        window.Close();
    }

    [AvaloniaFact]
    public void TheMonitorToggleEditsTheTrackAndTheInputPickerShowsItsInput()
    {
        var (window, view, project) = Open();
        SetArm(view, ArmedRow, true);
        window.UpdateLayout();
        var monitor = view.ArmedRows.Children.OfType<ToggleButton>().Single();
        var picker = view.ArmedRows.Children.OfType<TrackInputPicker>().Single();

        monitor.IsChecked = !(monitor.IsChecked == true);

        Assert.Equal(monitor.IsChecked == true, project.Tracks[ArmedRow].MonitorInput);
        // The closed button shows the input's label.
        Assert.Contains(AudioInputs.Label(project.Tracks[ArmedRow].AudioInput), Assert.IsType<TextBlock>(picker.Content).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void DisarmingCollapsesTheSubRowAndRemovesItsControls()
    {
        var (window, view, _) = Open();
        var rowBefore = view.Timeline.RowTop(ArmedRow + 1) - view.Timeline.RowTop(ArmedRow);
        SetArm(view, ArmedRow, true);
        window.UpdateLayout();

        SetArm(view, ArmedRow, false);
        window.UpdateLayout();

        Assert.Equal(rowBefore, view.Timeline.RowTop(ArmedRow + 1) - view.Timeline.RowTop(ArmedRow), 6);
        Assert.Empty(view.ArmedRows.Children);
        window.Close();
    }

    [AvaloniaFact]
    public void TrackListAndTimelineStayAlignedBelowTheArmedRow()
    {
        var (window, view, _) = Open();
        var header = view.Header;
        var mutePosition = header.CellRect(ArmedRow + 1, TrackRowCells.Cell.Mute).Y - view.Timeline.RowTop(ArmedRow + 1);
        var rowsBelowBefore = view.Timeline.RowTop(ArmedRow + 2);

        SetArm(view, ArmedRow, true);
        window.UpdateLayout();

        Assert.Equal(mutePosition, header.CellRect(ArmedRow + 1, TrackRowCells.Cell.Mute).Y - view.Timeline.RowTop(ArmedRow + 1), 6);
        Assert.Equal(rowsBelowBefore + ArmedRowPainter.SubRowHeight, view.Timeline.RowTop(ArmedRow + 2), 6);
        window.Close();
    }

    [AvaloniaFact]
    public void ArmedTrackKeepsItsMainRowAtTopAndItsSubRowControlsInTheBandBelow()
    {
        var (window, view, project) = Open();
        SetArm(view, ArmedRow, true);
        window.UpdateLayout();
        var mainTop = view.Timeline.RowTop(ArmedRow);
        var mainHeight = view.Timeline.BaseBandHeightOf(ArmedRow);
        var subTop = mainTop + mainHeight;

        // The main row's controls stay centred in the normal row height, not in the row plus the sub-row.
        var fx = view.Header.CellRect(ArmedRow, TrackRowCells.Cell.Fx);
        Assert.InRange(fx.Center.Y, mainTop + mainHeight / 2 - 2, mainTop + mainHeight / 2 + 2);

        // The sub-row's controls sit in the band below the main row, the input picker shows the track's input.
        var picker = view.ArmedRows.Children.OfType<TrackInputPicker>().Single();
        var monitor = view.ArmedRows.Children.OfType<ToggleButton>().Single();
        Assert.InRange(Canvas.GetTop(picker) + view.Timeline.TrackRowsTop, subTop, subTop + ArmedRowPainter.SubRowHeight - picker.Height);
        Assert.InRange(Canvas.GetTop(monitor) + view.Timeline.TrackRowsTop, subTop, subTop + ArmedRowPainter.SubRowHeight - monitor.Height);
        Assert.Contains(AudioInputs.Label(project.Tracks[ArmedRow].AudioInput), Assert.IsType<TextBlock>(picker.Content).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheSubRowMeterShowsTheInputLevelOfTheArmedTrack()
    {
        var (window, view, project) = Open();
        SetArm(view, ArmedRow, true);
        window.UpdateLayout();

        view.Header.ShowInputLevel(project.Tracks[ArmedRow], 0.5);
        var recorder = new TrackRowSelectionTests.RectRecorder();
        view.Header.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);

        Assert.Contains(recorder.Rects, r => Math.Abs(r.X - ArmedRowPainter.MeterLeft) < 0.01 && Math.Abs(r.Width - 0.5 * ArmedRowPainter.MeterWidth) < 0.01);
        window.Close();
    }
}
