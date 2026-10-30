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

// Owns: the selected track row's left-edge strip in its own colour (header and lane, single and multi).
// Does not own: the grey hover/selection overlay or the row geometry.
public class TrackRowSelectedStripTests
{
    private static (SongProject Song, TimelineView Timeline, TrackHeaderColumn Column) Open()
    {
        var song = DemoSongFactory.Create();
        while (song.Tracks.Count < 4) song.Tracks.Add(NewTrackFactory.CreateTrack(song, TrackKind.Audio));
        var timeline = new TimelineView(song) { PaneHeight = 4000 };
        return (song, timeline, new TrackHeaderColumn(timeline, song));
    }

    [AvaloniaFact]
    public void SingleSelectedRowDrawsStripWithTokenWidth()
    {
        var (_, timeline, column) = Open();
        timeline.HoverRow = -1;
        timeline.ShadeSelectedTrack = true;
        timeline.SelectedRow = 1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());

        var strips = HeaderStrips(column, timeline, light: false);
        Assert.Single(strips);
        Assert.Equal(ChromeMetrics.SelectedStripWidth, strips[0].W);
        Assert.Equal(0, strips[0].X);
        Assert.Equal(timeline.RowTop(1), strips[0].Y, 2);
        Assert.Equal(timeline.RowHeightOf(1), strips[0].H, 2);
        Assert.Equal(ChromeMetrics.SelectedStripCorner, strips[0].Radius);
    }

    [AvaloniaFact]
    public void MultiPickedRowsEachDrawStrip()
    {
        var (song, timeline, column) = Open();
        timeline.HoverRow = -1;
        timeline.SelectedRow = -1;
        timeline.FocusModel.SetRows(new[] { song.Tracks[0].Id, song.Tracks[2].Id });

        var strips = HeaderStrips(column, timeline, light: false);
        Assert.Equal(2, strips.Count);
        Assert.Contains(strips, s => System.Math.Abs(s.Y - timeline.RowTop(0)) < 1);
        Assert.Contains(strips, s => System.Math.Abs(s.Y - timeline.RowTop(2)) < 1);
        Assert.DoesNotContain(strips, s => System.Math.Abs(s.Y - timeline.RowTop(1)) < 1);
    }

    [AvaloniaFact]
    public void HoverOnlyDrawsNoStrip()
    {
        var (_, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;
        timeline.SelectedRow = -1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());
        timeline.HoverRow = 1;

        Assert.Empty(HeaderStrips(column, timeline, light: false));
        Assert.Empty(LaneStrips(timeline, light: false));
    }

    [AvaloniaFact]
    public void HoveredSelectedHasStripAndGrey()
    {
        var (_, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;
        timeline.SelectedRow = 1;
        timeline.HoverRow = 1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());

        var recorder = new FillRecorder();
        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, false), false);
        Assert.Contains(recorder.Fills, f => f.Fill.Equals(HoverShade.RowOverlay(false)));
        Assert.Single(HeaderStrips(column, timeline, light: false));
    }

    [AvaloniaFact]
    public void StripColourBrighterThanTintDiffersPerTrack()
    {
        var (song, timeline, column) = Open();
        song.Tracks[0].ColorHex = "#2248E8";
        song.Tracks[1].ColorHex = "#35B954";
        var dark0 = TrackRowTint.SelectedStrip(song.Tracks[0], light: false);
        var dark1 = TrackRowTint.SelectedStrip(song.Tracks[1], light: false);
        Assert.NotEqual(dark0, dark1);

        var tint = TrackRowTint.Of(song.Tracks[0], light: false, 40)!.Value.ToUi();
        Assert.True(ThemeColours.Luminance(dark0) > ThemeColours.Luminance(tint));

        timeline.SelectedRow = 0;
        timeline.HoverRow = -1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());
        var strips = HeaderStrips(column, timeline, light: false);
        Assert.Single(strips);
        Assert.Equal(dark0, strips[0].Fill);
    }

    [AvaloniaFact]
    public void VeryDarkTrackKeepsMinimumLightness()
    {
        var (song, _, _) = Open();
        song.Tracks[0].ColorHex = "#000000";
        var strip = TrackRowTint.SelectedStrip(song.Tracks[0], light: false);
        var (_, _, lightness) = ThemeColours.ToHsl(strip);
        Assert.True(lightness >= TrackRowTint.SelectedStripDarkMinLightness - 0.01);
    }

    [AvaloniaFact]
    public void DefaultColourUsedWhenTrackHasNone()
    {
        var (song, _, _) = Open();
        song.Tracks[0].ColorHex = "not-a-colour";
        var missing = TrackRowTint.SelectedStrip(song.Tracks[0], light: false);
        song.Tracks[0].ColorHex = NamedColourSwatches.TrackDefaultColour;
        var fallback = TrackRowTint.SelectedStrip(song.Tracks[0], light: false);
        Assert.Equal(fallback, missing);
    }

    [AvaloniaFact]
    public void LightThemeStripIsDarkerVariant()
    {
        var (song, _, _) = Open();
        var dark = TrackRowTint.SelectedStrip(song.Tracks[0], light: false);
        var light = TrackRowTint.SelectedStrip(song.Tracks[0], light: true);
        Assert.NotEqual(dark, light);
        Assert.True(ThemeColours.ToHsl(light).Lightness < ThemeColours.ToHsl(dark).Lightness);
    }

    [AvaloniaFact]
    public void StripIsInHeaderNotLane()
    {
        var (_, timeline, column) = Open();
        timeline.ShadeSelectedTrack = true;
        timeline.SelectedRow = 1;
        timeline.HoverRow = -1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());

        var header = HeaderStrips(column, timeline, light: false);
        var lane = LaneStrips(timeline, light: false);
        Assert.Single(header);
        Assert.Empty(lane);
    }

    [AvaloniaFact]
    public void CellLayoutUnchangedBySelection()
    {
        var (_, timeline, column) = Open();
        timeline.HoverRow = -1;
        timeline.FocusModel.SetRows(System.Array.Empty<System.Guid>());
        timeline.SelectedRow = -1;
        var before = AllCells(column, timeline);
        timeline.SelectedRow = 1;
        var after = AllCells(column, timeline);
        Assert.Equal(before, after);
    }

    private static List<(UiColor Fill, double X, double Y, double W, double H, double Radius)> HeaderStrips(TrackHeaderColumn column, TimelineView timeline, bool light)
    {
        var recorder = new FillRecorder();
        column.PaintCells(recorder, key => AvaloniaPalette.Get(key, light), light);
        return recorder.Fills.Where(f => System.Math.Abs(f.W - ChromeMetrics.SelectedStripWidth) < 0.01 && System.Math.Abs(f.X) < 0.01).ToList();
    }

    private static List<(UiColor Fill, double X, double Y, double W, double H, double Radius)> LaneStrips(TimelineView timeline, bool light)
    {
        var recorder = new FillRecorder();
        timeline.DrawBody(recorder, light);
        return recorder.Fills.Where(f => System.Math.Abs(f.W - ChromeMetrics.SelectedStripWidth) < 0.01).ToList();
    }

    private static List<string> AllCells(TrackHeaderColumn column, TimelineView timeline)
    {
        var boxes = new List<string>();
        for (var row = 0; row < timeline.RowCount; row++)
            foreach (TrackRowCells.Cell cell in System.Enum.GetValues<TrackRowCells.Cell>())
                boxes.Add(column.CellRect(row, cell).ToString());
        return boxes;
    }

    private sealed class FillRecorder : IDrawContext
    {
        public List<(UiColor Fill, double X, double Y, double W, double H, double Radius)> Fills { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();
        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left, int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, null!);
        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0)
        {
            if (fill is { } c) Fills.Add((c, x, y, width, height, radius));
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
