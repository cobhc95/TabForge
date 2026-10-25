using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track rows' vertical scroll inside the arrangement: rows scroll under the fixed chrome while the header and the lanes move together,
/// the wheel and the scroll bar drive the offset, the Add track strip stays inside the pane, and the timeline pane's minimum height.
/// Does not own: the pane's content cap (ArrangementNaturalHeightTests), the hover and selection (TrackRowSelectionTests).
/// Tests: TabForge.Avalonia.Tests.TrackListScrollTests.
/// </summary>
public class TrackListScrollTests
{
    /// <summary>Opens an arrangement of <paramref name="tracks"/> tracks in a window of <paramref name="height"/> DIPs.</summary>
    private static (Window Window, ArrangementView View) Open(double height, int tracks = 10)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < tracks) song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio));
        var view = new ArrangementView(song.Project, song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1000, Height = height, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    [AvaloniaFact]
    public void ShortPaneScrollsTheRowsWhileTheRulerAndHeaderStayFixed()
    {
        var (window, view) = Open(300);
        var timeline = view.Timeline;

        Assert.True(timeline.MaxRowScrollOffset > 0);
        Assert.True(view.GetVisualDescendants().OfType<TrackRowScrollBar>().Single().IsVisible);
        var cellBefore = view.Header.CellRect(3, TrackRowCells.Cell.Mute).Y;
        var laneBefore = timeline.RowTop(3);
        var rowsTop = timeline.TrackRowsTop;

        timeline.RowScrollOffset = 40;
        window.UpdateLayout();

        Assert.Equal(laneBefore - 40, timeline.RowTop(3), 6);
        Assert.Equal(cellBefore - 40, view.Header.CellRect(3, TrackRowCells.Cell.Mute).Y, 6);
        Assert.Equal(rowsTop, timeline.TrackRowsTop, 6);
    }

    [AvaloniaFact]
    public void WheelOverTheTrackListScrollsTheRows()
    {
        var (window, view) = Open(300);
        var timeline = view.Timeline;

        var overHeader = view.Header.TranslatePoint(view.Header.CellRect(2, TrackRowCells.Cell.Mute).Center, window)!.Value;
        window.MouseWheel(overHeader, new Vector(0, -1));

        Assert.Equal(RowScrollRules.WheelNotch, timeline.RowScrollOffset, 6);
    }

    [AvaloniaFact]
    public void WheelUpOverTheLanesZoomsInAndKeepsTheBarUnderThePointer()
    {
        var (window, view) = Open(300);
        var timeline = view.Timeline;
        var sideways = timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        var before = timeline.ZoomFactor;
        var pointer = timeline.TranslatePoint(new Point(timeline.BarX(6) + 4, timeline.RowTop(2) + 10), window)!.Value;
        var anchorX = window.TranslatePoint(pointer, sideways)!.Value.X;
        var barBefore = timeline.BarAtX(sideways.Offset.X + anchorX);

        window.MouseWheel(pointer, new Vector(0, 1));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.True(timeline.ZoomFactor > before);
        Assert.Equal(barBefore, timeline.BarAtX(sideways.Offset.X + anchorX));
        Assert.Equal(0, timeline.RowScrollOffset, 6);
    }

    [AvaloniaFact]
    public void StripStaysInsideThePaneAtTheTopAndAtTheBottomOfTheScroll()
    {
        var (window, view) = Open(300);
        var timeline = view.Timeline;

        foreach (var offset in new[] { 0, timeline.MaxRowScrollOffset })
        {
            timeline.RowScrollOffset = offset;
            window.UpdateLayout();
            var stripTop = view.AddTrackBar.TranslatePoint(default, window)!.Value.Y;
            var rowsBottom = timeline.TranslatePoint(new Point(0, timeline.TrackRowsBottom), window)!.Value.Y;
            Assert.True(stripTop + view.AddTrackBar.Bounds.Height <= window.ClientSize.Height + 0.5, $"strip outside the window at offset {offset}");
            Assert.Equal(rowsBottom, stripTop, 1);
        }
    }

    [AvaloniaFact]
    public void TallPaneHasNoScrollBarAndTheStripSitsUnderTheLastRow()
    {
        var (window, view) = Open(900);
        var timeline = view.Timeline;

        Assert.Equal(0, timeline.MaxRowScrollOffset);
        Assert.False(view.GetVisualDescendants().OfType<TrackRowScrollBar>().Single().IsVisible);
        var lastRowBottom = timeline.TranslatePoint(new Point(0, timeline.RowTop(timeline.RowCount)), window)!.Value.Y;
        Assert.Equal(lastRowBottom, view.AddTrackBar.TranslatePoint(default, window)!.Value.Y, 1);
    }

    [AvaloniaFact]
    public void TransportSitsInTheListColumnAboveTheHeaderLabelsAndTheStripStaysInTheWindow()
    {
        var (window, view) = Open(500);
        var transportBottom = view.Transport.TranslatePoint(new Point(0, view.Transport.Bounds.Height), window)!.Value.Y;
        var labelsTop = view.Header.TranslatePoint(new Point(0, TimelineView.RulerHeight), window)!.Value.Y;
        var stripBottom = view.AddTrackBar.TranslatePoint(new Point(0, view.AddTrackBar.Bounds.Height), window)!.Value.Y;

        Assert.True(labelsTop >= transportBottom - 0.5, "the header labels start below the transport");
        Assert.True(view.Transport.Bounds.Width <= view.Header.ColumnWidth + 0.5, "the transport is as wide as the track list, not the timeline");
        Assert.True(stripBottom <= window.ClientSize.Height + 0.5, "the Add track strip is inside the window");
    }

    [AvaloniaFact]
    public void TimelinePaneMinimumIsThreeRowsPlusTheChromeAndTheStrip()
    {
        var expected = TimelineView.RulerHeight + TimelineView.SectionHeight + 3 * TimelineView.DefaultRowHeight + AddTrackLane.Height;

        Assert.Equal(expected, TimelineView.MinimumPaneHeight, 6);
    }
}
