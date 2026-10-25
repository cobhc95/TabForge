namespace TabForge.Platform;

/// <summary>A colour as four bytes; a value type, so passing one allocates nothing.</summary>
public readonly record struct UiColor(byte A, byte R, byte G, byte B)
{
    public uint Argb => (uint)(A << 24 | R << 16 | G << 8 | B);
}

/// <summary>An outline: colour and width in DIPs; <c>RoundCaps</c> rounds both line ends; <c>RoundJoins</c> rounds the corners between segments; <c>Dash</c> above 0 dashes it with <c>Dash</c> on and <c>Gap</c> off, in widths.</summary>
public readonly record struct UiStroke(UiColor Colour, double Width, bool RoundCaps = false, double Dash = 0, double Gap = 0, bool RoundJoins = false);

public enum UiTextAlign { Left, Centre, Right }

/// <summary>Laid-out text from <see cref="IDrawContext.Text"/>: its size, the distance from its top to its baseline, and the toolkit's own layout object for <see cref="IDrawContext.DrawText"/>.</summary>
public readonly struct UiText
{
    public UiText(double width, double height, double baseline, object layout) { Width = width; Height = height; Baseline = baseline; Layout = layout; }
    public double Width { get; }
    public double Height { get; }
    /// <summary>The distance in DIPs from the top of the text to its baseline.</summary>
    public double Baseline { get; }
    /// <summary>The toolkit's text object; only the adapter that made it reads it.</summary>
    public object Layout { get; }
}

/// <summary>A geometry made by the toolkit adapter (built once, drawn many times).</summary>
public interface IUiPath { }

/// <summary>A bitmap made by the toolkit adapter.</summary>
public interface IUiImage { }

/// <summary>
/// Drawing as a port: rectangles, lines, paths, text with measuring, images, clips and transforms, in DIPs. Colours are values; the adapter keeps
/// one frozen brush or pen per colour, so drawing a frame allocates nothing beyond the text layouts it asks for.
/// Owns: the drawing contract. Does not own: colours (theme table), layout, when a layer is redrawn.
/// Tests: TestDrawContextPort.
/// </summary>
public interface IDrawContext : IDrawHandles
{
    /// <summary>A rectangle, filled and/or outlined; <paramref name="radius"/> above 0 rounds its corners.</summary>
    void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0);
    void Line(UiStroke stroke, double x1, double y1, double x2, double y2);
    /// <summary>An ellipse with radii <paramref name="rx"/> and <paramref name="ry"/> around (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry);
    void Path(UiColor? fill, UiStroke? stroke, IUiPath path);
    /// <summary>Lays out <paramref name="text"/>; <paramref name="maxWidth"/> above 0 wraps it at that width; <paramref name="weight"/> is the CSS weight
    /// (400 normal, 600 semibold); <paramref name="outline"/> strokes the glyph outlines behind the text; <paramref name="idealLayout"/> places glyphs at their design widths instead of whole pixels.</summary>
    UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
        int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false);
    /// <summary>Device pixels per DIP of the surface being drawn, for <see cref="Text"/>.</summary>
    double PixelsPerDip { get; }
    /// <summary>Builds the frozen paths this surface draws (glyphs, arcs, flags).</summary>
    IPathFactory Paths { get; }
    void DrawText(in UiText text, double x, double y);
    void Image(IUiImage image, double x, double y, double width, double height);
    void PushClip(double x, double y, double width, double height);
    void PushTranslate(double dx, double dy);
    void PushScale(double sx, double sy, double centreX = 0, double centreY = 0);
    /// <summary>Rotates by <paramref name="degrees"/> (clockwise) around (<paramref name="centreX"/>, <paramref name="centreY"/>).</summary>
    void PushRotate(double degrees, double centreX, double centreY);
    /// <summary>Ends the last clip or transform.</summary>
    void Pop();
}

/// <summary>What makes two shaped texts the same, for an adapter's shaped-text cache: size and wrap width are quantised x8, DPI x1000.</summary>
public readonly record struct UiTextKey(string Text, string Family, int Size, UiColor Colour, int Weight, bool Italic, UiStroke? Outline, int Dpi, int MaxWidth,
    UiTextAlign Align, bool IdealLayout)
{
    public static UiTextKey Of(string text, string family, double size, UiColor colour, int weight, bool italic, UiStroke? outline, double pixelsPerDip,
        double maxWidth, UiTextAlign align, bool idealLayout) =>
        new(text, family, Quantise(size, 8), colour, weight, italic, outline, Quantise(pixelsPerDip, 1000), Quantise(maxWidth, 8), align, idealLayout);

    private static int Quantise(double value, double steps) => (int)System.Math.Round(value * steps);
}
