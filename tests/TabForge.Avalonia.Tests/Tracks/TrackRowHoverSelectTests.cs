using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the track list's single grey hover/selection overlay: hover and selected share one translucent grey,
//   no blue, both themes, no stacking, multi-picked rows all get it, and the lane matches the header.
// Does not own: the row geometry or the name weight.
public class TrackRowHoverSelectTests
{
    private static (SongProject Song, TimelineView Timeline, TrackHeaderColumn Column) Open()
    {
        var song = DemoSongFactory.Create();
        while (song.Tracks.Count < 4) song.Tracks.Add(NewTrackFactory.CreateTrack(song, TrackKind.Audio));
        var timeline = new TimelineView(song) { PaneHeight = 4000 };
        return (song, timeline, new TrackHeaderColumn(timeline, song));
    }

    [Fact]
    public void RowOverlayIsGreyInBothThemesAndLeavesTrackColourVisible()
    {
        var dark = HoverShade.RowOverlay(light: false);
        var light = HoverShade.RowOverlay(light: true);

        Assert.Equal(dark.R, dark.G);
        Assert.Equal(dark.G, dark.B);
        Assert.Equal(light.R, light.G);
        Assert.Equal(light.G, light.B);
        Assert.Equal(255, dark.R);
        Assert.Equal(0, light.R);
        Assert.True(dark.A > 0 && dark.A < 255);
        Assert.True(light.A > 0 && light.A < 255);
        Assert.Equal((byte)System.Math.Round(255 * HoverShade.RowOverlayOpacity), dark.A);
        Assert.Equal(dark.A, light.A);
        Assert.NotEqual(AvaloniaPalette.Get("Accent", false), dark);
        Assert.NotEqual(AvaloniaPalette.Get("SelectedTint", false), dark);
    }

    [AvaloniaFact]
    public void SelectedRowUsesTheSameOverlayAsHoverRow()
    {
        var (_, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;

        timeline.SelectedRow = 1;
        timeline.HoverRow = -1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());
        var selected = Overlays(column, timeline, light: false);

        timeline.SelectedRow = -1;
        timeline.HoverRow = 1;
        var hovered = Overlays(column, timeline, light: false);

        Assert.Single(selected);
        Assert.Single(hovered);
        Assert.Equal(selected[0], hovered[0]);
    }

    [AvaloniaFact]
    public void HoveredAndSelectedRowDrawsTheOverlayOnlyOnce()
    {
        var (_, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;
        timeline.SelectedRow = 1;
        timeline.HoverRow = 1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());

        var overlays = Overlays(column, timeline, light: false);
        Assert.Single(overlays);
        Assert.Equal(timeline.RowTop(1), overlays[0].Y, 2);
    }

    [AvaloniaFact]
    public void MultiPickedRowsAllGetTheOverlay()
    {
        var (song, timeline, column) = Open();
        timeline.ShadeSelectedTrack = false;
        timeline.SelectedRow = -1;
        timeline.HoverRow = -1;
        timeline.FocusModel.SetRows(new[] { song.Tracks[0].Id, song.Tracks[2].Id });

        var overlays = Overlays(column, timeline, light: false);
        Assert.Equal(2, overlays.Count);
        Assert.Contains(overlays, o => System.Math.Abs(o.Y - timeline.RowTop(0)) < 1);
        Assert.Contains(overlays, o => System.Math.Abs(o.Y - timeline.RowTop(2)) < 1);
        Assert.DoesNotContain(overlays, o => System.Math.Abs(o.Y - timeline.RowTop(1)) < 1);
    }

    [AvaloniaFact]
    public void TimelineLaneHasNoOverlayButHeaderRowDoes()
    {
        var (_, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;
        timeline.SelectedRow = 1;
        timeline.HoverRow = -1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());

        var header = Overlays(column, timeline, light: false);
        var lane = LaneOverlays(timeline, light: false);

        Assert.Single(header);
        Assert.Empty(lane);
    }

    [AvaloniaFact]
    public void ClickingAnotherTracksLaneReplacesTheSelectionAndRepaintsTheHeader()
    {
        var (song, timeline, _) = Open();
        var repaints = 0;
        timeline.PickedRowsChanged += () => repaints++;
        timeline.FocusModel.Tracks.Click(song.Tracks, 1, TrackPick.Plain);
        timeline.FocusModel.Tracks.Click(song.Tracks, 2, TrackPick.Plain);
        Assert.Equal(new[] { 2 }, timeline.FocusModel.Tracks.Rows(song.Tracks));
        Assert.Equal(2, repaints);

        timeline.FocusModel.Tracks.Click(song.Tracks, 0, TrackPick.Toggle);
        Assert.Equal(new[] { 0, 2 }, timeline.FocusModel.Tracks.Rows(song.Tracks));
        timeline.FocusModel.Tracks.Click(song.Tracks, 0, TrackPick.Plain);
        timeline.FocusModel.Tracks.Click(song.Tracks, 3, TrackPick.Range);
        Assert.Equal(new[] { 0, 1, 2, 3 }, timeline.FocusModel.Tracks.Rows(song.Tracks));
    }

    [AvaloniaFact]
    public void OnlyTheNewlySelectedHeaderRowHasOverlay()
    {
        var (song, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;
        timeline.HoverRow = -1;
        timeline.FocusModel.Tracks.Click(song.Tracks, 1, TrackPick.Plain);
        timeline.FocusModel.Tracks.Click(song.Tracks, 2, TrackPick.Plain);
        timeline.SelectedRow = 2;
        var overlays = Overlays(column, timeline, light: false);
        Assert.Single(overlays);
        Assert.Equal(timeline.RowTop(2), overlays[0].Y, 2);
        Assert.True(column.IsNameBold(2));
        Assert.False(column.IsNameBold(1));
    }

    private static List<(UiColor Fill, double X, double Y, double W, double H)> Overlays(TrackHeaderColumn column, TimelineView timeline, bool light)
    {
        var recorder = new FillRecorder();
        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, light), light);
        var want = HoverShade.RowOverlay(light);
        return recorder.Fills.Where(f => f.Fill.Equals(want) && System.Math.Abs(f.W - column.ColumnWidth) < 0.01).ToList();
    }

    private static List<(UiColor Fill, double X, double Y, double W, double H)> LaneOverlays(TimelineView timeline, bool light)
    {
        var recorder = new FillRecorder();
        timeline.DrawBody(recorder, light);
        var want = HoverShade.RowOverlay(light);
        return recorder.Fills.Where(f => f.Fill.Equals(want)).ToList();
    }

    private sealed class FillRecorder : IDrawContext
    {
        public List<(UiColor Fill, double X, double Y, double W, double H)> Fills { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();
        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left, int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, null!);
        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0)
        {
            if (fill is { } c) Fills.Add((c, x, y, width, height));
        }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
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
