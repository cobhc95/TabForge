using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Draws a bundled SVG icon read by the Core icon reader (SvgIcon) in Avalonia: each shape's path is built once by <see cref="Prepare"/>, then
/// each draw applies the shape's rotation and paints and draws the texts in the icon colour, the way the WPF icon does.
/// Owns: the Avalonia geometry of an icon's shapes and the draw calls for them. Does not own: reading the file (ToolIconArt, TransportIconArt),
/// the frame transform of each icon, or the caches that hold prepared icons.
/// Tests: TabForge.Avalonia.Tests.SvgIconTests.
/// </summary>
internal static class AvaloniaSvgIcon
{
    /// <summary>One shape ready to draw.</summary>
    internal sealed record Shape(AvaloniaPath Path, SvgRotation? Rotation, SvgPaint? Fill, SvgPaint? Stroke, double StrokeWidth);

    /// <summary>An icon ready to draw.</summary>
    internal sealed record Prepared(IReadOnlyList<Shape> Shapes, IReadOnlyList<SvgText> Texts);

    /// <summary>The icon with its shape geometry built.</summary>
    public static Prepared Prepare(SvgIcon icon) =>
        new(icon.Shapes.Select(shape => new Shape(new AvaloniaPath(ToGeometry(shape.Geometry)), shape.Rotation, shape.Fill, shape.Stroke,
            shape.StrokeWidth)).ToArray(), icon.Texts);

    /// <summary>
    /// Draws the shapes, then the texts, in the current coordinate space. <paramref name="current"/> is the icon colour: a
    /// <c>currentColor</c> paint takes it, and null leaves those paints out and draws no texts.
    /// </summary>
    public static void Draw(IDrawContext d, Prepared icon, UiColor? current, bool light)
    {
        foreach (var shape in icon.Shapes)
        {
            if (shape.Rotation is { } turn) d.PushRotate(turn.Degrees, turn.CentreX, turn.CentreY);
            var fill = Paint(shape.Fill, current);
            var stroke = Paint(shape.Stroke, current);
            if (fill is { } f) d.Path(SvgIcon.ForTheme(f, light), null, shape.Path);
            if (stroke is { } k) d.Path(null, new UiStroke(SvgIcon.ForTheme(k, light), shape.StrokeWidth, RoundCaps: true, RoundJoins: true), shape.Path);
            if (shape.Rotation is not null) d.Pop();
        }
        if (current is not { } colour) return;
        foreach (var text in icon.Texts) DrawText(d, text, colour, light);
    }

    private static void DrawText(IDrawContext d, SvgText text, UiColor colour, bool light)
    {
        if (text.Rotation is { } turn) d.PushRotate(turn.Degrees, turn.CentreX, turn.CentreY);
        var laid = d.Text(text.Text, text.FontFamily, text.FontSize, SvgIcon.ForTheme(colour, light), d.PixelsPerDip, weight: text.Weight);
        // The SVG y is the baseline; the anchor moves the left edge by the laid-out width.
        var left = text.X - (text.Anchor == SvgTextAnchor.Middle ? laid.Width / 2 : text.Anchor == SvgTextAnchor.End ? laid.Width : 0);
        d.DrawText(in laid, left, text.Y - laid.Baseline);
        if (text.Rotation is not null) d.Pop();
    }

    private static UiColor? Paint(SvgPaint? paint, UiColor? current) =>
        paint is { } own ? (own.IsCurrentColour ? current : own.Colour) : null;

    internal static Geometry ToGeometry(SvgGeometry geometry) => geometry switch
    {
        SvgPathGeometry path => Geometry.Parse(path.Data),
        SvgEllipseGeometry ellipse => new EllipseGeometry(new Rect(ellipse.CentreX - ellipse.RadiusX, ellipse.CentreY - ellipse.RadiusY,
            2 * ellipse.RadiusX, 2 * ellipse.RadiusY)),
        SvgRectGeometry rect => new RectangleGeometry(new Rect(rect.X, rect.Y, rect.Width, rect.Height)),
        _ => throw new ArgumentOutOfRangeException(nameof(geometry), geometry, "Unknown SVG geometry.")
    };
}
