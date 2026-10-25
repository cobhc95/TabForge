using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track list width and its join: a drag moves the list edge within the minimum and the pane's maximum share, the header and the timeline stay
///     row-aligned, the splitter line stops at the track rows, and the gap across the Add track strip is filled with the strip's colour.
/// Does not own: the splitter's drawing (DockSplitter), the saved width (MainWindowArrangementModes), the column arithmetic (TrackRowCells).
/// </summary>
public class TrackListWidthTests
{
    [Fact]
    public void WiderNameColumnWidensTheListAndShiftsTheColumnsAfterIt()
    {
        Assert.Equal(TrackRowCells.TotalWidth, TrackRowCells.TotalWidthFor(TrackRowCells.NameWidth));
        Assert.Equal(TrackRowCells.Column("pan").Left + 20, TrackRowCells.Column("pan", TrackRowCells.NameWidth + 20).Left);
    }

    [AvaloniaFact]
    public void DraggingChangesTheListWidthWithinBoundsAndKeepsRowsAligned()
    {
        var view = new ArrangementView(DemoSongFactory.Create(), new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create()));
        var window = new Window { Width = 1000, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        var start = view.Header.ColumnWidth;

        view.BeginTrackListDrag();
        view.MoveTrackListDrag(40);
        Assert.Equal(start + 40, view.Header.ColumnWidth, 6);
        view.MoveTrackListDrag(-100000);
        Assert.Equal(TrackListWidthRules.MinListWidth, view.Header.ColumnWidth, 6);
        view.MoveTrackListDrag(100000);
        Assert.True(view.Header.ColumnWidth <= 1000 * TrackListWidthRules.MaxShareOfPane + 0.5);

        // The M box of row 2 sits inside row 2's band, the same band the timeline lane uses.
        var mute = view.Header.CellRect(2, TrackRowCells.Cell.Mute).Center.Y;
        Assert.InRange(mute, view.Timeline.RowTop(2), view.Timeline.RowTop(2) + view.Timeline.RowHeight);
    }

    [AvaloniaFact]
    public void SplitterLineStopsAtTheRowsAboveTheFullWidthStrip()
    {
        var song = DemoSongFactory.Create();
        var view = new ArrangementView(song, new ScorePreviewEditor.PreviewDocument(song));
        var window = new Window { Width = 1000, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        var splitter = view.GetVisualDescendants().OfType<DockSplitter>().Single();

        Assert.Equal(view.Timeline.TrackRowsBottom, splitter.Bounds.Height, 1);
        Assert.Equal(view.Timeline.TrackRowsBottom, view.Timeline.Bounds.Height, 1);
        Assert.True(view.AddTrackBar.IsVisible);

        view.SetCompact(true);
        window.UpdateLayout();
        Assert.False(view.AddTrackBar.IsVisible);
        Assert.Equal(0, view.AddTrackBarHeight);
    }

    [AvaloniaFact]
    public void TheListMeetsTheTimelineAtAOnePixelDividerAndTheRowBarSitsAtTheTimelinesRightEdge()
    {
        var song = DemoSongFactory.Create();
        while (song.Tracks.Count < 16) song.Tracks.Add(TabForge.Services.NewTrackFactory.CreateTrack(song, TabForge.Models.TrackKind.Audio));
        var view = new ArrangementView(song, new ScorePreviewEditor.PreviewDocument(song))
        {
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Window { Width = 1000, Height = 300, Content = view };   // short, so the rows overflow and the row bar shows
        window.Show();
        window.UpdateLayout();
        double Left(Visual control) => control.TranslatePoint(default, view)!.Value.X;
        var rowBar = view.GetVisualDescendants().OfType<TrackRowScrollBar>().Single();
        var splitter = view.GetVisualDescendants().OfType<DockSplitter>().Single();
        var listRight = Left(view.Header) + view.Header.Bounds.Width;
        var timelineLeft = Left(view.Timeline);

        Assert.True(rowBar.IsVisible);
        Assert.InRange(timelineLeft - listRight, 0, ArrangementView.DividerWidth);
        var timelineViewer = view.Timeline.FindAncestorOfType<ScrollViewer>()!;
        Assert.Equal(Left(timelineViewer) + timelineViewer.Bounds.Width, Left(rowBar), 0.5);   // the row bar is at the timeline's right edge
        // The grab area stays as wide as a dock splitter, centred on the divider.
        Assert.Equal(DockSplitter.BarThickness, splitter.Bounds.Width, 0.5);
        // The Add track strip spans the whole pane below the divider, so it covers the list and the timeline alike.
        Assert.Equal(view.Bounds.Width, view.AddTrackBar.Bounds.Width, 0.5);
        Assert.True(listRight <= view.AddTrackBar.Bounds.Width && timelineLeft < view.AddTrackBar.Bounds.Width);
    }
}
