using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: after the timeline pane in the real main window shrinks to its minimum and grows back, the track list column and the timeline are
/// both as tall as the pane's rows and a rendered frame draws every track row in both, with the row scroll offset in range.
/// Does not own: the splitter clamp (DockSplitPanel), the row geometry (TimelineView), the cell drawing (TrackHeaderColumn.PaintCells).
/// Tests: TabForge.Avalonia.Tests.TrackRowsRegrowTests.
/// </summary>
[Collection(TimingCollection.Name)]   // renders a frame and counts the rows it drew: run alone so the headless renderer is not contended by another test's window
public class TrackRowsRegrowTests
{
    [AvaloniaFact]
    public void GrowingTheTimelinePaneAfterTheMinimumDrawsEveryTrackRow()
    {
        // The window starts empty now: the row-fit test needs the demo song's ten tracks, so the test hands the window that song.
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song);
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        var split = arrangement.GetVisualAncestors().OfType<DockSplitPanel>().First();
        var growSign = arrangement.GetVisualAncestors().Contains(split.Children[2]) ? -1.0 : 1.0;
        var trackCount = arrangement.Document.Project.Tracks.Count;

        Drag(main, split.Splitter, -growSign * 3000);
        AssertEveryRowDrawn(main, arrangement, trackCount);
        Drag(main, split.Splitter, growSign * 3000);
        AssertEveryRowDrawn(main, arrangement, trackCount);
        main.Close();
    }

    private static void AssertEveryRowDrawn(MainWindow main, ArrangementView arrangement, int trackCount)
    {
        var timeline = arrangement.Timeline;
        Assert.InRange(timeline.RowScrollOffset, 0, timeline.MaxRowScrollOffset);
        var state = $"timeline {timeline.Bounds.Height} of {timeline.Height}, header {arrangement.Header.Bounds.Height}, pane {timeline.PaneHeight}";
        Assert.True(System.Math.Abs(timeline.Bounds.Height - timeline.VisibleHeight) < 0.5, state);
        Assert.True(System.Math.Abs(arrangement.Header.Bounds.Height - timeline.Bounds.Height) < 0.5, state);
        var shown = Enumerable.Range(0, trackCount).Count(row => timeline.RowTop(row) + timeline.RowHeight > timeline.TrackRowsTop && timeline.RowTop(row) < timeline.TrackRowsBottom);
        var before = timeline.DrawnRowCount;
        var drawn = 0;
        // The headless renderer can answer a capture from a window another test just left (no draw, DrawnRowCount unchanged); retry until this window
        // has actually rendered. The assertion is unchanged: one rendered frame must draw every row the geometry says is shown.
        for (var attempt = 0; attempt < RenderAttempts && drawn == 0; attempt++)
        {
            timeline.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            using (main.CaptureRenderedFrame()) { }
            Dispatcher.UIThread.RunJobs();
            drawn = timeline.DrawnRowCount - before;
            if (drawn == 0) Thread.Sleep(RenderRetryPauseMs);
        }
        Assert.Equal(shown, drawn);
        if (timeline.MaxRowScrollOffset == 0) Assert.Equal(trackCount, shown);
    }

    // How many captured frames to try before the row-count assertion, and the pause between tries, when the headless renderer answers a frame
    // that drew nothing for this window.
    private const int RenderAttempts = 5, RenderRetryPauseMs = 10;

    private static void Drag(MainWindow window, DockSplitter splitter, double distance)
    {
        var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, distance / 2));
        window.MouseMove(start + new Vector(0, distance));
        window.MouseUp(start + new Vector(0, distance), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
