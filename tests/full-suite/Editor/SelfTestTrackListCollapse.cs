using System.Windows;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>The track list can be dragged down to about 3 rows; its headers and lanes then scroll together, it cannot grow past its rows and the height is saved.</summary>
public static partial class SelfTest
{
    private static void TestTrackListCollapse()
    {
        var song = new SongProject();
        for (var i = 0; i < 20; i++) song.Tracks.Add(MixerTestTrack("T" + i, TrackKind.Guitar, 30));
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var workspace = new DockWorkspace(new Window());
        var window = new FitStage(workspace, 1000, 1700);
        workspace.SetEditorContent(new System.Windows.Controls.Border());
        workspace.RegisterPanel("timeline", "Arrangement", panel, 440, 112, "timeline", "score-editor");
        workspace.RestoreLayout(null);
        var host = new FitHost { Arrangement = panel, Dock = workspace };
        var fit = new TrackListFitController(host, workspace);
        using var alive = KeepAlive();
        try
        {
            window.UpdateLayout(); PumpUi();
            fit.FitToTracks(); PumpUi();
            double Pane() { PumpUi(); workspace.UpdateLayout(); return panel.ActualHeight + TrackListFitController.Chrome; }
            var rows = panel.PreferredHeight();

            workspace.SimulateSplitterDrag("timeline", 40, complete: true); PumpUi();
            Check("track list collapse: the pane can be dragged down to about 3 rows", Math.Abs(Pane() - ArrangementPanel.CollapsedPaneHeight) < 1.5 && Pane() < rows - 100, $"{Pane():0.0}px of {rows:0.0}");
            Check("track list collapse: the chosen height is saved", Math.Abs(host.Timeline.TrackListHeight - ArrangementPanel.CollapsedPaneHeight) < 1.5, $"{host.Timeline.TrackListHeight:0.0}");

            if (panel.AddLaneRow is { } addRow)
            {
                var laneY = addRow.TranslatePoint(new Point(0, addRow.ActualHeight), panel.RowsScroll).Y;
                Check("track list collapse: the Add track row stays inside the viewport at the top", panel.RowScrollOffset == 0 && laneY <= ((System.Windows.Controls.ScrollViewer)panel.RowsScroll).ViewportHeight + 1 && laneY > 0, $"bottom {laneY:0.0}");
            }
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            panel.LanesScroll.RaiseEvent(wheel); PumpUi();
            Check("track list collapse: the wheel over the lanes zooms the timeline and leaves the rows where they are",
                panel.RowScrollOffset == 0 && Math.Abs(panel.RowScrollOffset - panel.LaneScrollOffset) < 0.01, $"rows {panel.RowScrollOffset:0.0}, lanes {panel.LaneScrollOffset:0.0}");
            var before = panel.RowScrollOffset;
            var wheel2 = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            panel.RowsScroll.RaiseEvent(wheel2); PumpUi();
            Check("track list collapse: the wheel over the track list scrolls too", panel.RowScrollOffset > before && Math.Abs(panel.RowScrollOffset - panel.LaneScrollOffset) < 0.01, $"{before:0.0} -> {panel.RowScrollOffset:0.0}");

            var tl = panel.TimelineForTest;
            var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
            byte[] Band()
            {
                var w = (int)Math.Min(300, tl.ActualWidth);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, (int)tl.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(tl);
                var px = new byte[w * (int)gridTop * 4];
                bmp.CopyPixels(new Int32Rect(0, 0, w, (int)gridTop), px, w * 4, 0);
                return px;
            }
            panel.RowsScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 5000) { RoutedEvent = UIElement.PreviewMouseWheelEvent }); PumpUi();
            var top = Band();
            panel.RowsScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -5000) { RoutedEvent = UIElement.PreviewMouseWheelEvent }); PumpUi();
            Check("track list collapse: the ruler and section strip stay pinned while the rows scroll", panel.RowScrollOffset > 0 && top.AsSpan().SequenceEqual(Band()), $"offset {panel.RowScrollOffset:0.0}");
            Check("track list collapse: ruler hit-tests to no track and the first row starts below the pinned strips", tl.TrackAt(gridTop - 1) == -1 && tl.TrackAt(gridTop + 1) >= 0 && Math.Abs(panel.RowScrollOffset - panel.LaneScrollOffset) < 0.01, $"{tl.TrackAt(gridTop + 1)}");

            workspace.RestoreLayout(workspace.CaptureLayout()); window.UpdateLayout(); PumpUi();
            fit.FitToTracks(); PumpUi();
            Check("track list collapse: the height survives a layout save and restore", Math.Abs(Pane() - ArrangementPanel.CollapsedPaneHeight) < 1.5, $"{Pane():0.0}px");

            workspace.SimulateSplitterDrag("timeline", 4000, complete: true); PumpUi();
            Check("track list collapse: growing again stops at the content (no empty gap)", Pane() - panel.PreferredHeight() < 2.5 && Pane() > rows - 2.5, $"{Pane():0.0}px vs {panel.PreferredHeight():0.0}");
            Check("track list collapse: growing back clears the saved height", host.Timeline.TrackListHeight == 0, $"{host.Timeline.TrackListHeight:0.0}");
        }
        finally { window.Close(); }
    }
}
