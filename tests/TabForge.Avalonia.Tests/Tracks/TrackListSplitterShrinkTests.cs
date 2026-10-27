using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline pane in the default main window keeps every track row laid out: after the splitter shrinks the pane to about 1.5 rows and
/// grows it back to its cap, and after a status notice row appears under the window.
/// Does not own: the splitter clamp (DockSplitPanel), the cap rule (ArrangementView.NaturalHeight), the fit (MainWindowTrackListFit) or the rows (TimelineView).
/// Tests: TabForge.Avalonia.Tests.TrackListSplitterShrinkTests.
/// </summary>
public class TrackListSplitterShrinkTests
{
    // The window starts empty now: the row-fit tests need the demo song's ten tracks, so the test hands the window that song.
    private static MainWindow OpenDemoWindow()
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song);
        main.Show();
        return main;
    }

    [AvaloniaFact]
    public void ShrinkingTheTimelinePaneAndGrowingItBackShowsEveryTrackRow()
    {
        var main = OpenDemoWindow();
        Dispatcher.UIThread.RunJobs();
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        var split = arrangement.GetVisualAncestors().OfType<DockSplitPanel>().First();
        var timelineIsSecond = arrangement.GetVisualAncestors().Contains(split.Children[2]);
        // Dragging the splitter up grows a pane below it; down grows a pane above it.
        var growSign = timelineIsSecond ? -1.0 : 1.0;
        var cap = arrangement.MaxHeight;
        Assert.True(double.IsFinite(cap));
        var trackCount = arrangement.Document.Project.Tracks.Count;

        Drag(main, split.Splitter, growSign * 3000);
        Assert.Equal(cap, arrangement.Bounds.Height, 1);
        Assert.Equal(cap, arrangement.MaxHeight, 3);

        var oneAndAHalfRows = arrangement.TopBandHeight + TimelineView.RulerHeight + TimelineView.SectionHeight
            + 1.5 * TimelineView.DefaultRowHeight + AddTrackLane.Height;
        Drag(main, split.Splitter, -growSign * (arrangement.Bounds.Height - oneAndAHalfRows));
        Assert.Equal(cap, arrangement.MaxHeight, 3);
        Assert.True(arrangement.Bounds.Height < cap - 1, $"the pane did not shrink: {arrangement.Bounds.Height} of cap {cap}");
        AssertEveryRowLaidOut(main, arrangement, trackCount);

        Drag(main, split.Splitter, growSign * 3000);
        Assert.Equal(cap, arrangement.MaxHeight, 3);
        Assert.Equal(cap, arrangement.Bounds.Height, 1);
        AssertEveryRowLaidOut(main, arrangement, trackCount);
        main.Close();
    }

    [AvaloniaFact]
    public void ShowingAStatusNoticeKeepsEveryTrackRowLaidOut()
    {
        var main = OpenDemoWindow();
        Dispatcher.UIThread.RunJobs();
        var arrangement = main.GetVisualDescendants().OfType<ArrangementView>().Single();
        var notice = main.GetVisualDescendants().OfType<StatusNoticeBarView>().Single(n => AutomationProperties.GetName(n) == "Preview notice");
        var trackCount = arrangement.Document.Project.Tracks.Count;
        AssertEveryRowLaidOut(main, arrangement, trackCount);

        notice.Show("Live input on: Input 1");
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        AssertEveryRowLaidOut(main, arrangement, trackCount);
        main.Close();
    }

    // The timeline keeps its minimum visible rows; when the rows overflow the pane, the track row scroll bar shows and scrolling to the end reaches the last row.
    // When they all fit, every row is inside the viewport at once.
    private static void AssertEveryRowLaidOut(Window window, ArrangementView arrangement, int trackCount)
    {
        var timeline = arrangement.Timeline;
        Assert.Equal(trackCount, timeline.RowCount);
        // The pane keeps its least height (TimelineView.MinimumPaneHeight, pinned by TrackListScrollTests); the master lane takes one of its rows,
        // so the track rows at that height are fewer than MinimumVisibleRows.
        Assert.True(arrangement.Bounds.Height >= TimelineView.MinimumPaneHeight - 0.5, Describe(arrangement));
        var overflows = timeline.TrackRowsContentHeight > timeline.RowsViewportHeight + 0.5;
        var rowBar = window.GetVisualDescendants().OfType<TrackRowScrollBar>().Single();
        Assert.Equal(overflows, rowBar.IsVisible);
        timeline.RowScrollOffset = timeline.MaxRowScrollOffset;
        Assert.True(timeline.RowTop(trackCount - 1) + timeline.RowHeight <= timeline.TrackRowsBottom + 0.5, Describe(arrangement));
        timeline.RowScrollOffset = 0;
    }

    private static string Describe(ArrangementView arrangement)
    {
        var timeline = arrangement.Timeline;
        return $"pane {arrangement.Bounds.Height} natural {arrangement.NaturalHeight} max {arrangement.MaxHeight} paneHeight {timeline.PaneHeight} " +
            $"timelineH {timeline.Bounds.Height} content {timeline.ContentHeight} visible {timeline.VisibleHeight} rowsBottom {timeline.TrackRowsBottom} " +
            $"rowsContent {timeline.TrackRowsContentHeight} viewport {timeline.RowsViewportHeight} scroll {timeline.RowScrollOffset} rows {timeline.RowCount}";
    }

    // A press on the splitter's line, a move in two steps and a release, in window coordinates.
    private static void Drag(Window window, DockSplitter splitter, double distance)
    {
        var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, distance / 2), RawInputModifiers.LeftMouseButton);
        window.MouseMove(start + new Vector(0, distance), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(0, distance), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
