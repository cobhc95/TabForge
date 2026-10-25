namespace TabForge.Platform.Testing;

/// <summary>One recorded draw call: its kind, its numbers (points, rect, path bounds or text box) and its text when it draws text.</summary>
public sealed record DrawRecord(string Kind, double[] Values, string? Text = null);

/// <summary>
/// An <see cref="IDrawContext"/> that logs every draw call to <see cref="Records"/> and forwards it to an inner context.
/// Owns: the call log (paths as their bounds, text as its string, position and laid-out size). Does not own: drawing or measuring (the inner context).
/// Tests: ScoreGeometryProofTests (Avalonia), --score-geometry (WPF).
/// Diagnostic tool, used by --score-geometry and the Avalonia proof.
/// </summary>
public sealed class GeometryRecorder(IDrawContext inner) : IDrawContext
{
    // Text layouts made through this recorder, to name the string at DrawText; key: the adapter's layout object; one draw pass, one thread.
    private readonly Dictionary<object, string> _strings = new(ReferenceEqualityComparer.Instance);

    public List<DrawRecord> Records { get; } = new();

    private void Log(string kind, params double[] values) => Records.Add(new DrawRecord(kind, values));

    public double PixelsPerDip => inner.PixelsPerDip;
    public IPathFactory Paths => inner.Paths;

    public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0)
    { Log("Rect", x, y, width, height); inner.Rect(fill, stroke, x, y, width, height, radius); }

    public void Line(UiStroke stroke, double x1, double y1, double x2, double y2)
    { Log("Line", x1, y1, x2, y2); inner.Line(stroke, x1, y1, x2, y2); }

    public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry)
    { Log("Ellipse", cx, cy, rx, ry); inner.Ellipse(fill, stroke, cx, cy, rx, ry); }

    public void Path(UiColor? fill, UiStroke? stroke, IUiPath path)
    { LogPath(path); inner.Path(fill, stroke, path); }

    public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
        int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
    {
        var laid = inner.Text(text, fontFamily, size, colour, pixelsPerDip, maxWidth, align, weight, italic, outline, idealLayout);
        _strings[laid.Layout] = text;
        return laid;
    }

    public void DrawText(in UiText text, double x, double y)
    {
        Records.Add(new DrawRecord("Text", [x, y, text.Width, text.Height], _strings.GetValueOrDefault(text.Layout, "?")));
        inner.DrawText(text, x, y);
    }

    public void Image(IUiImage image, double x, double y, double width, double height) { Log("Image", x, y, width, height); inner.Image(image, x, y, width, height); }
    public void PushClip(double x, double y, double width, double height) { Log("PushClip", x, y, width, height); inner.PushClip(x, y, width, height); }
    public void PushTranslate(double dx, double dy) { Log("PushTranslate", dx, dy); inner.PushTranslate(dx, dy); }
    public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { Log("PushScale", sx, sy, centreX, centreY); inner.PushScale(sx, sy, centreX, centreY); }
    public void PushRotate(double degrees, double centreX, double centreY) { Log("PushRotate", degrees, centreX, centreY); inner.PushRotate(degrees, centreX, centreY); }
    public void Pop() { Log("Pop"); inner.Pop(); }

    // ---- handle form ----
    public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0)
    { Log("Rect", x, y, width, height); inner.Rect(fill, pen, x, y, width, height, radiusX, radiusY); }
    public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { Log("Line", x1, y1, x2, y2); inner.Line(pen, x1, y1, x2, y2); }
    public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { Log("Ellipse", cx, cy, rx, ry); inner.Ellipse(fill, pen, cx, cy, rx, ry); }
    public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { LogPath(path); inner.Path(fill, pen, path); }
    public void Drawing(IUiDrawing drawing) { Log("Drawing"); inner.Drawing(drawing); }
    public void PushClip(IUiPath clip) { var b = inner.Paths.Bounds(clip); Log("PushClip", b.X, b.Y, b.Width, b.Height); inner.PushClip(clip); }
    public void PushTransform(IUiTransform transform) { Log("PushTransform"); inner.PushTransform(transform); }
    public void PushOpacity(double opacity) { Log("PushOpacity", opacity); inner.PushOpacity(opacity); }

    private void LogPath(IUiPath path) { var b = inner.Paths.Bounds(path); Log("Path", b.X, b.Y, b.Width, b.Height); }
}
