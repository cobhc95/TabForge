using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track header column staying level with the timeline lanes (row tops and the Add track strip) when the pane is resized.
/// Does not own: the row geometry (TimelineView), the header drawing (TrackHeaderColumn), the pane fit (MainWindowTrackListFit).
/// Tests: ArrangementView, TrackHeaderColumn, TimelineView.
/// </summary>
public class ArrangementAlignmentTests
{
    [AvaloniaFact]
    public void HeaderRowsStayLevelWithLanesWhenThePaneResizes()
    {
        var song = DemoSongFactory.Create();
        var doc = new ScorePreviewEditor.PreviewDocument(song);
        var arrangement = new ArrangementView(song, doc);
        arrangement.Show(doc);
        var window = new Window { Width = 1300, Height = 400, Content = arrangement };
        window.Show();
        foreach (var height in new[] { 400.0, 220.0 })
        {
            window.Height = height;
            Dispatcher.UIThread.RunJobs();
            var timeline = arrangement.Timeline;
            var header = arrangement.Header;
            double ScreenY(Visual control, double y) => control.TranslatePoint(new Point(0, y), window)!.Value.Y;
            Assert.True(timeline.RowCount > 0);
            for (var row = 0; row < timeline.RowCount; row++)
                Assert.Equal(ScreenY(timeline, timeline.RowTop(row)), ScreenY(header, timeline.RowTop(row)));
            Assert.Equal(ScreenY(timeline, timeline.TrackRowsBottom), ScreenY(header, timeline.TrackRowsBottom));
        }
        window.Close();
    }
}
