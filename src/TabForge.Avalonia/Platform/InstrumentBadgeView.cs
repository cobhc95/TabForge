using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// One instrument badge (Assets/Instruments/SVG) as a square control: the art fits the control's smaller side, and the "background" circle is
/// painted in the instrument family's colour from the catalogue. Draws through IDrawContext.
/// Owns: the prepared badge geometry and its drawing. Does not own: reading the SVG (Core InstrumentBadge) or the catalogue (Core InstrumentCatalog).
/// Cache: prepared badges, key = the SVG path, ceiling = the catalogue size (about 150 files), UI thread only.
/// Tests: TabForge.Avalonia.Tests.InstrumentBadgeTests.
/// </summary>
public sealed class InstrumentBadgeView : Control
{
    // The badges are authored on a 512 unit square whose background circle spans nearly all of it; the circle's box is the picture's extent.
    private const double AuthoredSize = 512;

    private sealed record Prepared(IReadOnlyList<(AvaloniaPath Path, BadgeShape Shape)> Art, (AvaloniaPath Path, BadgeShape Shape)? Background,
        double Left, double Top, double Extent);

    private static readonly Dictionary<string, Prepared?> PreparedByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many badge files have been read into the cache; each file is read once, so the count only grows on a first read.</summary>
    internal static int PreparedCount => PreparedByPath.Count;

    private readonly AvaloniaDrawContext _draw = new();
    private InstrumentEntry? _entry;

    public InstrumentBadgeView(double size)
    {
        Width = size;
        Height = size;
        IsHitTestVisible = false;
    }

    /// <summary>The instrument whose badge is drawn; null or one without art draws nothing.</summary>
    public InstrumentEntry? Entry
    {
        get => _entry;
        set { _entry = value; InvalidateVisual(); }
    }

    /// <summary>True when the entry has a readable badge.</summary>
    public bool HasArt => Entry?.SvgPath is { } path && Prepare(path) is not null;

    public override void Render(DrawingContext context)
    {
        if (Entry is not { SvgPath: { } file } entry || Prepare(file) is not { } badge) return;
        var d = _draw.Use(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var scale = size / badge.Extent;
        d.PushTranslate((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        d.PushScale(scale, scale);
        d.PushTranslate(-badge.Left, -badge.Top);
        if (badge.Background is { } back)
            d.Path(ColourHex.TryParse(entry.Background, out var family) ? family.ToUi() : Palette.Get("Gray"), null, back.Path);
        foreach (var (path, shape) in badge.Art)
        {
            if (shape.Fill is { } fill) d.Path(fill, null, path);
            if (shape.Stroke is { } line) d.Path(null, new UiStroke(line, shape.StrokeWidth, shape.RoundCaps, RoundJoins: shape.RoundJoins), path);
        }
        d.Pop(); d.Pop(); d.Pop();
    }

    private static Prepared? Prepare(string path)
    {
        if (PreparedByPath.TryGetValue(path, out var hit)) return hit;
        Prepared? result = null;
        if (InstrumentBadge.Read(path) is { } badge)
        {
            var back = badge.Background is { } b ? (Geometry(b), b) : ((AvaloniaPath, BadgeShape)?)null;
            var circle = badge.Background?.Geometry as SvgEllipseGeometry;
            result = new Prepared(badge.Art.Select(shape => (Geometry(shape), shape)).ToArray(), back,
                circle is null ? 0 : circle.CentreX - circle.RadiusX, circle is null ? 0 : circle.CentreY - circle.RadiusY,
                circle is null ? AuthoredSize : 2 * circle.RadiusX);
        }
        PreparedByPath[path] = result;
        return result;
    }

    private static AvaloniaPath Geometry(BadgeShape shape)
    {
        var geometry = shape.Geometry is SvgPathGeometry data ? PathGeometry.Parse(data.Data) : AvaloniaSvgIcon.ToGeometry(shape.Geometry);
        if (geometry is PathGeometry figures) figures.FillRule = shape.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;
        var m = shape.Matrix;
        if (m != BadgeMatrix.Identity) geometry.Transform = new MatrixTransform(new Matrix(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY));
        return new AvaloniaPath(geometry);
    }
}
