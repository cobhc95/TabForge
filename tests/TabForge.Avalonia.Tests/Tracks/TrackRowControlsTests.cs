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
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track-row cells of the Avalonia track header: the slider drag as one undo step, the record arm toggle, the cogwheel and FX hits,
/// the column width and the dark and light renders. Renders go to %TEMP%\tf-avalonia-tests\TrackRowControlsTests.
/// Tests: TrackRowCells, TrackHeaderColumn, TimelineInputController.
/// </summary>
public class TrackRowControlsTests
{
    private static (Window Window, ArrangementView View, ScorePreviewEditor.PreviewDocument Doc) Open(ThemeVariant theme)
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 420, Content = view, RequestedThemeVariant = theme };
        window.Show();
        return (window, view, doc);
    }

    [AvaloniaFact]
    public void VolumeAndPanDragsAreOneUndoStepEach()
    {
        var (w, v, doc) = Open(ThemeVariant.Dark);
        var box = ArrangementClicks.CellInWindow(v, 0, TrackRowCells.Cell.Volume);
        var before = doc.Project.Tracks[0].Volume;
        w.MouseDown(new Point(box.X + 4, box.Center.Y), MouseButton.Left);
        w.MouseMove(new Point(box.Center.X, box.Center.Y));
        w.MouseMove(new Point(box.Right, box.Center.Y));
        Assert.Equal(before, doc.Project.Tracks[0].Volume);
        w.MouseUp(new Point(box.Right, box.Center.Y), MouseButton.Left);
        Assert.Equal(127, doc.Project.Tracks[0].Volume);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal(before, doc.Project.Tracks[0].Volume);

        var pan = ArrangementClicks.CellInWindow(v, 1, TrackRowCells.Cell.Pan);
        w.MouseDown(new Point(pan.X, pan.Center.Y), MouseButton.Left);
        w.MouseUp(new Point(pan.X, pan.Center.Y), MouseButton.Left);
        Assert.Equal(0, doc.Project.Tracks[1].Pan);
        w.Close();
    }

    [AvaloniaFact]
    public void ArmTogglesAndCogAndFxRaiseTheirRequests()
    {
        var (w, v, doc) = Open(ThemeVariant.Dark);
        var arm = ArrangementClicks.CellInWindow(v, 2, TrackRowCells.Cell.Arm).Center;
        int armedRow = -1;
        v.Input.TrackArmToggled += row => armedRow = row;
        w.MouseDown(arm, MouseButton.Left);
        w.MouseUp(arm, MouseButton.Left);
        Assert.True(doc.Project.Tracks[2].RecordArm);
        Assert.Equal(2, armedRow);
        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.False(doc.Project.Tracks[2].RecordArm);

        (int Row, TrackRowCells.Cell Cell)? asked = null;
        v.Input.TrackCellRequested += (row, cell) => asked = (row, cell);
        foreach (var cell in new[] { TrackRowCells.Cell.Settings, TrackRowCells.Cell.Fx, TrackRowCells.Cell.FxPower })
        {
            var p = ArrangementClicks.CellInWindow(v, 1, cell).Center;
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Assert.Equal((1, cell), asked);
            Assert.False(string.IsNullOrEmpty(TrackHeaderColumn.TooltipOf(0, cell)));
        }
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowsRenderTheWpfColumns(bool light)
    {
        var (w, v, _) = Open(light ? ThemeVariant.Light : ThemeVariant.Dark);
        v.Width = 1300;   // the pane share the clamp reads
        w.UpdateLayout();
        v.SetTrackListWidth(TrackRowCells.TotalWidth);   // the row list these WPF columns are written for (the product default is wider)
        w.UpdateLayout();
        Assert.Equal(TrackRowCells.TotalWidth, v.Header.ColumnWidth);
        Assert.Equal(598, TrackRowCells.TotalWidth);   // 532 before the per-track meter
        var dir = DialogTestKit.RenderDirectory(nameof(TrackRowControlsTests));
        using var frame = w.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, light ? "track-rows-light.png" : "track-rows-dark.png"));
        w.Close();
    }
}
