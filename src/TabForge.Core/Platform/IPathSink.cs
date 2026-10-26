namespace TabForge.Platform;

/// <summary>A point in DIPs.</summary>
public readonly record struct UiPoint(double X, double Y);

/// <summary>A rectangle in DIPs.</summary>
public readonly record struct UiRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    /// <summary>True when the two rectangles touch or overlap (shared edges count).</summary>
    public bool IntersectsWith(UiRect other) => other.X <= Right && other.Right >= X && other.Y <= Bottom && other.Bottom >= Y;
}

public enum UiFillRule { EvenOdd, Nonzero }

/// <summary>
/// Receives one geometry's figures, in DIPs. Every segment is stroked and joins sharply unless it asks for a smooth join.
/// Owns: the path-building contract. Does not own: drawing (<see cref="IDrawContext.Path"/>) or the geometry object (the adapter's).
/// Tests: TestDrawContextPort, AvaloniaPortsTests (Avalonia).
/// </summary>
public interface IPathSink
{
    void BeginFigure(UiPoint start, bool filled, bool closed);
    /// <param name="smoothJoin">The join into this segment is drawn smooth (Avalonia honours it; other toolkits could draw it as the pen joins).</param>
    void LineTo(UiPoint point, bool smoothJoin = false);
    void BezierTo(UiPoint control1, UiPoint control2, UiPoint end, bool smoothJoin = false);
    /// <summary>A quadratic curve; kept quadratic because a toolkit rasterises it differently from its cubic equivalent.</summary>
    void QuadraticTo(UiPoint control, UiPoint end);
    /// <summary>An elliptical arc to <paramref name="end"/>; <paramref name="rotation"/> is in degrees.</summary>
    void ArcTo(UiPoint end, double radiusX, double radiusY, double rotation, bool largeArc, bool clockwise);
}

/// <summary>
/// Builds frozen <see cref="IUiPath"/> geometries from figures given to an <see cref="IPathSink"/>.
/// Owns: geometry creation and bounds. Does not own: drawing. Tests: TestDrawContextPort, AvaloniaPortsTests.
/// </summary>
public interface IPathFactory
{
    IUiPath BuildPath(System.Action<IPathSink> figures, UiFillRule fillRule = UiFillRule.EvenOdd);
    /// <summary>The geometry's bounds without any stroke.</summary>
    UiRect Bounds(IUiPath path);
}

/// <summary>A measured single-run text: its box and the baseline's distance from the top.</summary>
public readonly record struct UiTextMetrics(double Width, double Height, double Baseline, double Extent);

/// <summary>
/// Text measuring without drawing.
/// Owns: text size and line metrics, font availability. Does not own: text caches (the callers') or drawing (<see cref="IDrawContext.Text"/>).
/// Tests: TestDrawContextPort, AvaloniaPortsTests.
/// </summary>
public interface ITextMeasure
{
    /// <param name="weight">OpenType weight, 400 normal, 700 bold.</param>
    UiTextMetrics Measure(string text, string fontFamily, double size, int weight, bool italic, double pixelsPerDip);
    bool FontExists(string fontFamily);
}
