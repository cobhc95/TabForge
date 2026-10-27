using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the arrangement (track header column beside the timeline) under headless input: row alignment, the mute and solo toggles as undoable
/// edits, the row drag that starts on the header, and the owner's icon art. Renders go to %TEMP%\tf-avalonia-tests\ArrangementViewTests.
/// Tests: ArrangementView, TrackHeaderColumn, TrackIconArt, TimelineInputController.
/// </summary>
public class ArrangementViewTests
{
    private static (Window Window, ArrangementView View, ScorePreviewEditor.PreviewDocument Doc) Open(ThemeVariant? theme = null)
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 400, Content = view, RequestedThemeVariant = theme ?? ThemeVariant.Dark };
        window.Show();
        return (window, view, doc);
    }

    private static Point RowPoint(ArrangementView v, int row, double x = 60) => new(x, ArrangementClicks.RowsTop(v) + v.Timeline.RowTop(row) + 10);

    private static void Shot(Window w, string name)
    {
        var dir = DialogTestKit.RenderDirectory(nameof(ArrangementViewTests));
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderRowsSitOnTheTimelineRows(bool light)
    {
        var (w, v, doc) = Open(light ? ThemeVariant.Light : ThemeVariant.Dark);
        Assert.Equal(doc.Project.Tracks.Count, v.Timeline.RowCount);
        Assert.Equal(v.Timeline.Height, v.Header.Bounds.Height);
        for (var row = 0; row < v.Timeline.RowCount; row++)
        {
            Assert.Equal(row, v.Header.RowAtY(v.Timeline.RowTop(row) + 1));
            Assert.Equal(v.Timeline.RowTop(row) + (v.Timeline.RowHeight - 22) / 2, v.Header.CellRect(row, TrackRowCells.Cell.Mute).Y);
        }
        Shot(w, light ? "arrangement-light" : "arrangement-dark");
        w.Close();
    }

    [AvaloniaFact]
    public void MuteAndSoloTogglesAreUndoableEdits()
    {
        var (w, v, doc) = Open();
        var track = doc.Project.Tracks[1];
        var mute = ArrangementClicks.CellInWindow(v, 1, TrackRowCells.Cell.Mute).Center;
        var solo = ArrangementClicks.CellInWindow(v, 1, TrackRowCells.Cell.Solo).Center;
        w.MouseDown(mute, MouseButton.Left);
        w.MouseUp(mute, MouseButton.Left);
        Assert.True(track.Mute);
        Assert.False(track.Solo);
        w.MouseDown(solo, MouseButton.Left);
        w.MouseUp(solo, MouseButton.Left);
        Assert.True(track.Solo);
        Shot(w, "mute-solo-dark");
        Assert.NotNull(DocumentEdits.Undo(doc));
        track = doc.Project.Tracks[1];
        Assert.False(track.Solo);
        Assert.True(track.Mute);
        w.Close();
    }

    [AvaloniaFact]
    public void DraggingAHeaderReordersTracksInOneUndoStepAndTheGridDoesNotDragRows()
    {
        var (w, v, doc) = Open();
        var first = doc.Project.Tracks[0];
        var order = doc.Project.Tracks.ToArray();
        var rowsTop = ArrangementClicks.RowsTop(v);
        w.MouseDown(new Point(v.Timeline.BarX(1) + 60 + v.Header.ColumnWidth, rowsTop + v.Timeline.RowTop(0) + 10), MouseButton.Left);
        w.MouseMove(new Point(v.Timeline.BarX(1) + 60 + v.Header.ColumnWidth, rowsTop + v.Timeline.RowTop(2) + 10));
        Assert.Equal(-1, v.Timeline.DragRow);
        w.MouseUp(new Point(v.Timeline.BarX(1) + 60 + v.Header.ColumnWidth, rowsTop + v.Timeline.RowTop(2) + 10), MouseButton.Left);
        Assert.Equal(order, doc.Project.Tracks);

        w.MouseDown(RowPoint(v, 0), MouseButton.Left);
        w.MouseMove(RowPoint(v, 1));
        w.MouseMove(RowPoint(v, 2));
        Assert.Equal(0, v.Timeline.DragRow);
        Shot(w, "header-drag-dark");
        w.MouseUp(RowPoint(v, 2), MouseButton.Left);
        Assert.Equal(2, doc.Project.Tracks.IndexOf(first));
        Assert.Equal(-1, v.Timeline.DragRow);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(first.Name, doc.Project.Tracks[0].Name);
        w.Close();
    }

    [AvaloniaFact]
    public void EveryInstrumentFamilyHasOwnerArt()
    {
        Assert.True(TrackIconArt.Has(TrackIconKeys.KeyFor(29, false)));
        Assert.True(TrackIconArt.Has(TrackIconKeys.KeyFor(0, true)));
        Assert.True(TrackIconArt.Has(TrackIconKeys.AudioKey));
        for (var program = 0; program < 128; program++) Assert.True(TrackIconArt.Has(TrackIconKeys.KeyFor(program, false)), "program " + program);
    }

    [AvaloniaFact]
    public void TransportIsOneRowAtTheDefaultListWidthSoNoBandShowsAboveTheRuler()
    {
        var (w, v, _) = Open();
        w.UpdateLayout();
        Assert.True(v.Transport.DesiredSize.Height <= TimelineView.RulerHeight,
            $"transport height {v.Transport.DesiredSize.Height} is taller than the ruler {TimelineView.RulerHeight}");
        Assert.Equal(0, v.TopBandHeight, 3);
        w.Close();
    }
}
