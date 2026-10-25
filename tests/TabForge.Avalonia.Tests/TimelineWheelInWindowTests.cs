using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the mouse wheel in the real main window: a routed wheel event raised over a timeline lane zooms the timeline one step in, and a wheel
//   over the track list column does not zoom.
// Does not own: the zoom steps (TimelineZoomRules), the bar anchoring inside the arrangement (TrackListScrollTests) or the row scroll (TrackListScrollTests).
// Tests: this class.
public class TimelineWheelInWindowTests
{
    // The window starts empty now: the wheel tests need the demo song's lanes and rows, so the test hands the window that song.
    private static MainWindow OpenDemoWindow()
    {
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song);
        main.Show();
        main.UpdateLayout();
        return main;
    }

    [AvaloniaFact]
    public void WheelOverTheLanesOfTheMainWindowZoomsInOneStep()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var timeline = arrangement.Timeline;
        var before = timeline.ZoomFactor;
        var lanePoint = timeline.TranslatePoint(new Point(timeline.BarX(6) + 4, timeline.RowTop(2) + 10), main)
            ?? throw new System.InvalidOperationException("The timeline is not in the main window.");

        main.MouseWheel(lanePoint, new Vector(0, 1));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.True(timeline.ZoomFactor > before, $"zoom stayed at {before} after a wheel over the lanes");
        main.Close();
    }

    [AvaloniaFact]
    public void WheelOverTheTrackListOfTheMainWindowLeavesTheZoomAlone()
    {
        var main = OpenDemoWindow();
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().First();
        var timeline = arrangement.Timeline;
        var before = timeline.ZoomFactor;
        var listPoint = arrangement.Header.TranslatePoint(arrangement.Header.CellRect(2, TrackRowCells.Cell.Mute).Center, main)
            ?? throw new System.InvalidOperationException("The track list is not in the main window.");

        main.MouseWheel(listPoint, new Vector(0, -1));

        Assert.Equal(before, timeline.ZoomFactor, 6);
        main.Close();
    }
}
