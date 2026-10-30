using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the arrangement's layout in the real main window at the owner's size (2000 x 1100, a ten-track demo song): the default fit shows every
/// track row on a fresh and on a saved launch, the track rows end exactly at the Add track strip, the strip spans the full pane width below both
/// the track list and the timeline, and an overflowing row list shows its scroll bar inside the window with the last row reachable.
/// Does not own: the fit maths (DockSizeRules), the rows' scroll rules (TrackListScrollTests) or the strip's drawing (AddTrackStrip).
/// Tests: TabForge.Avalonia.Tests.TrackListOwnerLayoutTests.
/// </summary>
public class TrackListOwnerLayoutTests
{
    private const double OwnerWidth = 2000;
    private const double OwnerHeight = 1100;

    /// <summary>The owner's demo song: the demo's five tracks grown to ten, as the owner's session has them.</summary>
    private const int OwnerTrackCount = 10;

    /// <summary>Window heights whose timeline pane is tall, mid-sized and short: the rows fit at the first and overflow at the others.</summary>
    private static readonly double[] PaneHeights = { OwnerHeight, 800, 520 };

    private static MainWindow OpenOwnerWindow(MainWindowSettings? settings = null)
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count < OwnerTrackCount) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var main = new MainWindow(settings, first: new SongDocument(() => null) { Project = project }) { Width = OwnerWidth, Height = OwnerHeight };
        main.Show();
        Settle(main);
        return main;
    }

    /// <summary>Runs the body on a window opened from a settings file that an earlier launch saved, as the owner's app starts after its first run.</summary>
    private static void OpenSavedOwnerWindow(Action<MainWindow> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-owner-layout-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            using (var first = MainWindowSettings.Open(path))
            {
                var launch = OpenOwnerWindow(first);
                launch.Close();
            }
            using var settings = MainWindowSettings.Open(path);
            var main = OpenOwnerWindow(settings);
            body(main);
            main.Close();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Runs the UI thread's queued jobs and lays out until the window stops changing, as the owner's first launch does.</summary>
    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    private static ArrangementView Arrangement(MainWindow main) => main.GetVisualDescendants().OfType<ArrangementView>().Single();

    private static Control AddTrackBar(MainWindow main) => main.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "AddTrackBar");

    private static TrackRowScrollBar RowBar(MainWindow main) => main.GetVisualDescendants().OfType<TrackRowScrollBar>().Single();

    /// <summary>The top of <paramref name="visual"/> in the window's coordinates.</summary>
    private static double TopIn(Visual visual, Visual window) => visual.TranslatePoint(default, window)!.Value.Y;

    /// <summary>The window-space top and bottom of the timeline's track row <paramref name="row"/>.</summary>
    private static (double Top, double Bottom) RowSpan(MainWindow main, int row)
    {
        var timeline = Arrangement(main).Timeline;
        var top = TopIn(timeline, main) + timeline.RowTop(row);
        return (top, top + timeline.RowHeight);
    }

    private static void AssertEveryRowInsideTheRows(MainWindow main)
    {
        var timeline = Arrangement(main).Timeline;
        Assert.Equal(OwnerTrackCount, timeline.RowCount);
        Assert.Equal(0, timeline.MaxRowScrollOffset, 3);
        var rowsTop = TopIn(timeline, main) + timeline.TrackRowsTop;
        var rowsBottom = TopIn(timeline, main) + timeline.TrackRowsBottom;
        for (var row = 0; row < timeline.RowCount; row++)
        {
            var (top, bottom) = RowSpan(main, row);
            Assert.True(top >= rowsTop - 0.5 && bottom <= rowsBottom + 0.5, $"row {row} spans {top}-{bottom}, the rows show {rowsTop}-{rowsBottom}");
        }
    }

    [AvaloniaFact]
    public void AfterTheFirstLayoutEveryTrackRowIsFullyVisible()
    {
        var main = OpenOwnerWindow();
        AssertEveryRowInsideTheRows(main);
        main.Close();
    }

    /// <summary>The owner's start: the window is shown and laid out once, with no further passes; the default fit must already show every row.</summary>
    [AvaloniaFact]
    public void TheFirstLayoutPassAloneShowsEveryTrackRow()
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count < OwnerTrackCount) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = OwnerWidth, Height = OwnerHeight };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        AssertEveryRowInsideTheRows(main);
        main.Close();
    }

    [AvaloniaFact]
    public void AfterASavedLaunchEveryTrackRowIsFullyVisible()
    {
        OpenSavedOwnerWindow(AssertEveryRowInsideTheRows);
    }

    [AvaloniaFact]
    public void TrackRowsEndExactlyAtTheAddTrackStripTopAtEveryPaneHeight()
    {
        var main = OpenOwnerWindow();
        var timeline = Arrangement(main).Timeline;

        foreach (var height in PaneHeights)
        {
            main.Height = height;
            Settle(main);
            var rowsBottom = TopIn(timeline, main) + timeline.TrackRowsBottom;
            var stripTop = TopIn(AddTrackBar(main), main);
            Assert.True(Math.Abs(rowsBottom - stripTop) < 0.5, $"window {height}: rows end at {rowsBottom}, the Add track strip starts at {stripTop}");
        }
        main.Close();
    }

    [AvaloniaFact]
    public void AddTrackStripSpansTheFullPaneWidthBelowTheListAndTheTimeline()
    {
        var main = OpenOwnerWindow();
        var arrangement = Arrangement(main);
        var strip = AddTrackBar(main);

        Assert.Equal(arrangement.Bounds.Width, strip.Bounds.Width, 0.5);
        Assert.Equal(arrangement.TranslatePoint(default, main)!.Value.X, strip.TranslatePoint(default, main)!.Value.X, 0.5);
        Assert.True(TopIn(strip, main) >= TopIn(arrangement.Timeline, main) + arrangement.Timeline.TrackRowsBottom - 0.5, "the strip sits below the timeline's rows");
        main.Close();
    }

    [AvaloniaFact]
    public void OverflowingRowsShowTheScrollBarInsideTheWindowAndTheLastRowIsReachable()
    {
        var main = OpenOwnerWindow();
        var timeline = Arrangement(main).Timeline;
        main.Height = PaneHeights[^1];
        Settle(main);

        Assert.True(timeline.MaxRowScrollOffset > 0);
        var bar = RowBar(main);
        Assert.True(bar.IsVisible);
        var barTop = TopIn(bar, main);
        var barBottom = barTop + bar.Bounds.Height;
        Assert.True(bar.Bounds.Height > 0 && barTop >= 0 && barBottom <= main.ClientSize.Height + 0.5, $"row bar spans {barTop}-{barBottom} in a {main.ClientSize.Height} window");

        timeline.RowScrollOffset = timeline.MaxRowScrollOffset;
        Settle(main);
        var rowsBottom = TopIn(timeline, main) + timeline.TrackRowsBottom;
        var (lastTop, lastBottom) = RowSpan(main, timeline.RowCount - 1);
        Assert.True(lastTop >= TopIn(timeline, main) + timeline.TrackRowsTop - 0.5 && lastBottom <= rowsBottom + 0.5, $"last row spans {lastTop}-{lastBottom}, the rows end at {rowsBottom}");
        Assert.True(lastBottom <= main.ClientSize.Height + 0.5, "the last row is inside the window");
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(420)]
    [InlineData(340)]
    public void TheAddTrackStripStaysInsideAShortWindowAboveTheLastRow(double height)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-short-strip-" + Guid.NewGuid().ToString("N"));
        using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
        var main = OpenOwnerWindow(settings);
        main.Height = height;
        Settle(main);
        var strip = AddTrackBar(main);
        Assert.True(strip.IsVisible && strip.Bounds.Height > 0, "the strip is shown");
        var top = TopIn(strip, main);
        Assert.True(top + strip.Bounds.Height <= main.ClientSize.Height + 0.5, $"strip spans {top}-{top + strip.Bounds.Height} in a {main.ClientSize.Height} window");
        var timeline = Arrangement(main).Timeline;
        Assert.True(TopIn(timeline, main) + timeline.TrackRowsBottom <= top + 0.5, "the rows end above the strip");
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(8, 420)]
    [InlineData(3, 420)]
    [InlineData(8, 300)]
    public void AWindowOpenedShortKeepsTheAddTrackStripInside(int tracks, double height)
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count > tracks) project.Tracks.RemoveAt(project.Tracks.Count - 1);
        while (project.Tracks.Count < tracks) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = 1400, Height = height };
        main.Show();
        Settle(main);
        var strip = AddTrackBar(main);
        var top = TopIn(strip, main);
        Assert.True(strip.IsVisible && strip.Bounds.Height > 0, "the strip is shown");
        Assert.True(top + strip.Bounds.Height <= main.ClientSize.Height + 0.5, $"strip spans {top}-{top + strip.Bounds.Height} in a {main.ClientSize.Height} window");
        var timeline = Arrangement(main).Timeline;
        Assert.True(TopIn(timeline, main) + timeline.TrackRowsBottom <= top + 0.5, "the rows end above the strip");
        foreach (var ancestor in strip.GetVisualAncestors().OfType<Control>())
        {
            if (ancestor == main || !ancestor.ClipToBounds && ancestor.GetType().Name != "ScrollViewer") continue;
            var bottom = TopIn(ancestor, main) + ancestor.Bounds.Height;
            Assert.True(top + strip.Bounds.Height <= bottom + 0.5, $"strip bottom {top + strip.Bounds.Height} is clipped by {ancestor.GetType().Name} ending at {bottom}");
        }
        main.Close();
    }
}
