using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: how the bar highlight draws (spec barhighlight): grey fill and grey frame alone, the frame only where the blue selection already covers the cells (so the blue shows under the grey frame), and the two theme tokens.
// Does not own: where the highlight sits (TimelineView) or the gestures that move it (TimelineBarHighlightInputTests).
// Tests: this class.
public sealed class TimelineBarHighlightDrawingTests
{
    private static readonly UiColor Fill = new(0x40, 0xB4, 0xBA, 0xC4);
    private static readonly UiColor Frame = new(0xFF, 0xB4, 0xBA, 0xC4);

    private sealed record Drawn(UiColor? Fill, UiStroke? Stroke, double X, double Y, double Width, double Height);

    private sealed class Recorder : IDrawContext
    {
        public List<Drawn> Rects { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths => throw new NotSupportedException();
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) => Rects.Add(new Drawn(fill, stroke, x, y, width, height));
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) => throw new NotSupportedException();
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, null!);
        public void DrawText(in UiText text, double x, double y) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void Drawing(IUiDrawing drawing) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Pop() { }
    }

    private static Recorder Draw(params HighlightRow[] rows)
    {
        var draw = new Recorder();
        TimelineHighlightPainter.Draw(draw, Fill, Frame, 100, 300, rows);
        return draw;
    }

    [Fact]
    public void AloneTheHighlightIsAGreyFillWithAGreyFrameOnTop()
    {
        var draw = Draw(new HighlightRow(10, 30));

        Assert.Equal(2, draw.Rects.Count);
        Assert.Equal((Fill, null, 100.0, 10.0, 200.0, 30.0), (draw.Rects[0].Fill, draw.Rects[0].Stroke, draw.Rects[0].X, draw.Rects[0].Y, draw.Rects[0].Width, draw.Rects[0].Height));
        Assert.Null(draw.Rects[1].Fill);
        Assert.Equal(Frame, draw.Rects[1].Stroke!.Value.Colour);
        Assert.Equal(TimelineHighlightPainter.FrameWidth, draw.Rects[1].Stroke!.Value.Width);
    }

    [Fact]
    public void TheHighlightLookIsTwoGreyThemeTokensForDarkAndLight()
    {
        foreach (var light in new[] { false, true })
        {
            var fill = Palette.Get("TimelineHighlightFill", light);
            var frame = Palette.Get("TimelineHighlightFrame", light);
            Assert.True(fill.A is > 0 and < 0xFF);   // a see-through fill
            Assert.Equal(0xFF, frame.A);
            Assert.True(Math.Abs(frame.R - frame.B) < 0x20);   // grey, not the blue of the selection
        }
        Assert.NotEqual(Palette.Get("TimelineHighlightFrame", false), Palette.Get("TimelineHighlightFrame", true));
    }
}
