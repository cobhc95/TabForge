using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the stacked chord glow rule (ScorePlayedChip): stacked pills tile at the same width instead of overlapping, a lone note keeps its whole pill.</summary>
public class ScorePlayedChipTests
{
    /// <summary>Two sounding notes on adjacent strings tile edge to edge at the same width instead of overprinting.</summary>
    [Fact]
    public void StackedChipsTileWithoutOverlapAtTheSameWidth()
    {
        var draw = new RectRecordingDraw();
        var colour = new UiColor(255, 0, 200, 0);
        ScorePlayedChip.Draw(draw, colour, false, 100, 0, 16, 14, null, 6);
        ScorePlayedChip.Draw(draw, colour, false, 100, 12, 16, 14, 6, null);

        Assert.Equal(2, draw.Rects.Count);
        var (first, second) = (draw.Rects[0], draw.Rects[1]);
        Assert.Equal(first.Width, second.Width);
        Assert.True(first.Y + first.Height <= second.Y + 0.001, "stacked pills tile instead of overlapping");
    }

    /// <summary>A sounding note with no sounding neighbour keeps its whole pill.</summary>
    [Fact]
    public void LoneChipKeepsItsWholePill()
    {
        var draw = new RectRecordingDraw();
        ScorePlayedChip.Draw(draw, new UiColor(255, 0, 200, 0), false, 100, 50, 16, 14);

        var rect = Assert.Single(draw.Rects);
        Assert.Equal(100 - 8 - 2, rect.X);
        Assert.Equal(50 - 7 - 2, rect.Y);
        Assert.Equal(20, rect.Width);
        Assert.Equal(18, rect.Height);
    }

    private sealed class RectRecordingDraw : IDrawContext
    {
        public List<(double X, double Y, double Width, double Height)> Rects { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths => throw new System.NotSupportedException();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, new object());
        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) => Rects.Add((x, y, width, height));
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
