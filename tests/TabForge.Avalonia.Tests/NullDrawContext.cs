using System;
using TabForge.Platform;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a draw context that draws nothing and allocates nothing per call, so a test can measure the drawing code itself.
/// Tests: FretboardPlaybackFrameTests (allocations per playback frame), TimelineBodyCullingTests (bars drawn per viewport).
/// </summary>
internal sealed class NullDrawContext : IDrawContext
{
    public double PixelsPerDip => 1;
    // Paths are empty: the measurement covers the view's own work, not the geometry a real path factory builds.
    public IPathFactory Paths { get; } = new EmptyPathFactory();

    public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) { }
    public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
    public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
    public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
    public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
        int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false) => new(text.Length * 6, 12, 10, null!);
    public void DrawText(in UiText text, double x, double y) { }
    public void Image(IUiImage image, double x, double y, double width, double height) { }
    public void PushClip(double x, double y, double width, double height) { }
    public void PushTranslate(double dx, double dy) { }
    public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
    public void PushRotate(double degrees, double centreX, double centreY) { }
    public void Pop() { }

    public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
    public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
    public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
    public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
    public void Drawing(IUiDrawing drawing) { }
    public void PushClip(IUiPath clip) { }
    public void PushTransform(IUiTransform transform) { }
    public void PushOpacity(double opacity) { }

    private sealed class EmptyPathFactory : IPathFactory
    {
        public IUiPath BuildPath(Action<IPathSink> figures, UiFillRule fillRule = UiFillRule.EvenOdd) => new EmptyPath();
        public UiRect Bounds(IUiPath path) => default;
    }

    private sealed class EmptyPath : IUiPath { }
}
