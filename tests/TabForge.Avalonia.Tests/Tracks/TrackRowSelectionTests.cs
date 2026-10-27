using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track list's hover (the row and the cell under the pointer, over the cells and the lanes alike), the selection (a press and release
/// on a row selects its track; the selected row draws its tint and bold name), and the cell hover state (the lighter overlay drawn over the body).
/// Does not own: the scroll (TrackListScrollTests), the cell geometry (TrackRowControlsTests).
/// Tests: TabForge.Avalonia.Tests.TrackRowSelectionTests.
/// </summary>
public class TrackRowSelectionTests
{
    /// <summary>Opens a ten-track arrangement in a window; the song is a SongDocument so a selection lands on its TrackIndex.</summary>
    private static (Window Window, ArrangementView View, SongDocument Song) Open()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < 10) song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio));
        var view = new ArrangementView(song.Project, song) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view, song);
    }

    [AvaloniaFact]
    public void PointerOverTheCellsOfAnyRowHoversThatRow()
    {
        var (window, view, _) = Open();

        foreach (var row in new[] { 0, 4, 9 })
            foreach (var cell in new[] { TrackRowCells.Cell.Volume, TrackRowCells.Cell.Mute, TrackRowCells.Cell.Pan })
            {
                var point = view.Header.TranslatePoint(view.Header.CellRect(row, cell).Center, window)!.Value;
                window.MouseMove(point);
                Assert.Equal(row, view.Timeline.HoverRow);
                Assert.Equal(row, view.Header.HoverCell!.Value.Row);
                Assert.Equal(cell, view.Header.HoverCell!.Value.Cell);
            }
    }

    [AvaloniaFact]
    public void PressAndReleaseOnARowSelectsItsTrackAndDoesNotSwallowIt()
    {
        var (window, view, song) = Open();
        var nameX = TrackRowCells.Column("name", view.Header.NameColumnWidth).Left + 20;

        var point = view.Header.TranslatePoint(new Point(nameX, view.Timeline.RowTop(2) + 10), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(2, song.TrackIndex);
        Assert.Equal(2, view.Timeline.SelectedRow);

        var lane = view.Timeline.TranslatePoint(new Point(view.Timeline.BarX(1) + 5, view.Timeline.RowTop(5) + 10), window)!.Value;
        window.MouseDown(lane, MouseButton.Left);
        window.MouseUp(lane, MouseButton.Left);
        Assert.Equal(5, song.TrackIndex);
    }

    [AvaloniaFact]
    public void SelectedRowDrawsItsTintOnlyWhileTheShadeSettingIsOn()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var timeline = new TimelineView(song.Project);
        var column = new TrackHeaderColumn(timeline, song.Project);
        var before = DrawnRects(column);
        var top = timeline.RowTop(1);

        // Off by default: selecting the row adds no shade to the list.
        timeline.SelectedRow = 1;
        Assert.Equal(CountRowRects(before, top, column.ColumnWidth), CountRowRects(DrawnRects(column), top, column.ColumnWidth));

        timeline.ShadeSelectedTrack = true;
        Assert.Equal(CountRowRects(before, top, column.ColumnWidth) + 1, CountRowRects(DrawnRects(column), top, column.ColumnWidth));
    }

    [AvaloniaFact]
    public void HoveredMuteCellDrawsItsOverlayAfterItsBody()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var timeline = new TimelineView(song.Project);
        var column = new TrackHeaderColumn(timeline, song.Project);
        var box = column.CellRect(0, TrackRowCells.Cell.Mute);

        var idle = DrawnRects(column).Where(r => r == Box(box)).ToList();
        column.UpdateCellHover(box.Center);
        var hovered = DrawnRects(column).Select((r, i) => (Rect: r, Index: i)).Where(t => t.Rect == Box(box)).ToList();

        Assert.Single(idle);
        Assert.Equal(2, hovered.Count);
        Assert.True(hovered[1].Index > hovered[0].Index);
    }

    private static (double X, double Y, double Width, double Height) Box(Rect r) => (r.X, r.Y, r.Width, r.Height);

    private static int CountRowRects(IEnumerable<(double X, double Y, double Width, double Height)> rects, double top, double width) =>
        rects.Count(r => r.X == 0 && Math.Abs(r.Y - top) < 0.01 && Math.Abs(r.Width - width) < 0.01);

    /// <summary>Draws the header column into a recorder and returns the rectangles it drew, in order.</summary>
    private static List<(double X, double Y, double Width, double Height)> DrawnRects(TrackHeaderColumn column)
    {
        var recorder = new RectRecorder();
        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);
        return recorder.Rects;
    }

    /// <summary>Records every rectangle drawn; text and paths are measured but not drawn.</summary>
    internal sealed class RectRecorder : IDrawContext
    {
        public List<(double X, double Y, double Width, double Height)> Rects { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, null!);
        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) => Rects.Add((x, y, width, height));
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) => Rects.Add((x, y, width, height));
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Pop() { }
    }
}
