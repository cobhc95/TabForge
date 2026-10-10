using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Renders the instrument badges (Assets/Instruments/SVG) as frozen vector drawings. The badge's
/// background circle takes a chosen colour (the track colour); everything else is drawn as authored.
/// Parsed once per file and cached per colour, so menus and lists stay cheap.
/// </summary>
public static class InstrumentIcon
{
    private sealed record Shape(Geometry Geometry, Brush? Fill, Pen? Stroke);
    private sealed record Parsed(Geometry? Background, IReadOnlyList<Shape> Art);

    private static readonly Dictionary<string, Parsed?> ParsedCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, uint), DrawingImage> ImageCache = new();

    public static ImageSource? Get(InstrumentEntry? entry, Color? background = null)
    {
        if (entry?.SvgPath is not { } path) return null;
        var bg = background ?? (ColourChooser.TryParse(entry.Background, out var c) ? c : Colors.Gray);
        var key = (path, (uint)(bg.A << 24 | bg.R << 16 | bg.G << 8 | bg.B));
        if (ImageCache.TryGetValue(key, out var cached)) return cached;
        var parsed = Parse(path);
        if (parsed is null) return null;
        var group = new DrawingGroup();
        if (parsed.Background is not null) group.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null, parsed.Background));
        foreach (var s in parsed.Art) group.Children.Add(new GeometryDrawing(s.Fill, s.Stroke, s.Geometry));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        if (ImageCache.Count > 2048) ImageCache.Clear();
        ImageCache[key] = image;
        return image;
    }

    public static Image Element(InstrumentEntry? entry, double size, Color? background = null) =>
        new() { Source = Get(entry, background), Width = size, Height = size, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };

    private static Parsed? Parse(string path)
    {
        if (ParsedCache.TryGetValue(path, out var hit)) return hit;
        Parsed? result = null;
        try
        {
            if (File.Exists(path))
            {
                var root = XDocument.Load(path).Root!;
                Geometry? background = null;
                var shapes = new List<Shape>();
                void Walk(XElement element, Transform transform, IReadOnlyDictionary<string, string> inherited)
                {
                    var attrs = new Dictionary<string, string>(inherited);
                    foreach (var a in element.Attributes())
                        if (a.Name.LocalName is "fill" or "stroke" or "stroke-width" or "stroke-linecap" or "stroke-linejoin" or "fill-rule" or "opacity")
                            attrs[a.Name.LocalName] = a.Value;
                    var local = ParseTransform((string?)element.Attribute("transform"));
                    var combined = local is null ? transform : new MatrixTransform(local.Value * transform.Value);
                    var name = element.Name.LocalName;
                    if (name is "title" or "desc") return;
                    if (name is "g" or "svg")
                    {
                        foreach (var child in element.Elements()) Walk(child, combined, attrs);
                        return;
                    }
                    var geometry = ShapeGeometry(element, attrs);
                    if (geometry is null) return;
                    if (!combined.Value.IsIdentity) { geometry = geometry.Clone(); geometry.Transform = combined; }
                    geometry.Freeze();
                    if ((string?)element.Attribute("id") == "background") { background = geometry; return; }
                    var opacity = Num(attrs.GetValueOrDefault("opacity"), 1);
                    var fill = attrs.TryGetValue("fill", out var f) ? BrushOf(f, opacity) : Brushes.Black;
                    Pen? pen = null;
                    if (attrs.TryGetValue("stroke", out var st) && BrushOf(st, opacity) is { } strokeBrush)
                    {
                        pen = new Pen(strokeBrush, Num(attrs.GetValueOrDefault("stroke-width"), 1))
                        {
                            StartLineCap = Cap(attrs.GetValueOrDefault("stroke-linecap")),
                            EndLineCap = Cap(attrs.GetValueOrDefault("stroke-linecap")),
                            LineJoin = attrs.GetValueOrDefault("stroke-linejoin") == "round" ? PenLineJoin.Round : PenLineJoin.Miter,
                        };
                        pen.Freeze();
                    }
                    if (fill is null && pen is null) return;
                    shapes.Add(new Shape(geometry, fill, pen));
                }
                Walk(root, Transform.Identity, new Dictionary<string, string>());
                result = new Parsed(background, shapes);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Instrument icon failed: {path}: {ex.Message}"); } // Not logged: bundled icon: the fallback icon is drawn
        ParsedCache[path] = result;
        return result;
    }

    private static Geometry? ShapeGeometry(XElement e, IReadOnlyDictionary<string, string> attrs)
    {
        double A(string n, double d = 0) => Num((string?)e.Attribute(n), d);
        switch (e.Name.LocalName)
        {
            case "rect":
                return new RectangleGeometry(new Rect(A("x"), A("y"), Math.Max(0, A("width")), Math.Max(0, A("height"))), A("rx", A("ry")), A("ry", A("rx")));
            case "circle":
                return new EllipseGeometry(new Point(A("cx"), A("cy")), A("r"), A("r"));
            case "ellipse":
                return new EllipseGeometry(new Point(A("cx"), A("cy")), A("rx"), A("ry"));
            case "path":
                var d = (string?)e.Attribute("d");
                if (string.IsNullOrWhiteSpace(d)) return null;
                var g = Geometry.Parse(d);
                if (g is PathGeometry pg && attrs.GetValueOrDefault("fill-rule") == "evenodd") pg.FillRule = FillRule.EvenOdd;
                else if (g is StreamGeometry sg && attrs.GetValueOrDefault("fill-rule") != "evenodd")
                {
                    var p = PathGeometry.CreateFromGeometry(sg);
                    p.FillRule = FillRule.Nonzero;
                    return p;
                }
                return g;
            default:
                return null;
        }
    }

    private static Brush? BrushOf(string value, double opacity = 1)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "none") return null;
        if (!TabForge.Visualization.ColourText.TryParseHex(value, out var c) &&
            !TabForge.Visualization.ColourText.TryParse(value, out c)) return null;
        var b = new SolidColorBrush(c) { Opacity = Math.Clamp(opacity, 0, 1) };
        b.Freeze();
        return b;
    }

    private static PenLineCap Cap(string? v) => v switch { "round" => PenLineCap.Round, "square" => PenLineCap.Square, _ => PenLineCap.Flat };

    private static double Num(string? s, double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;

    // translate / scale / rotate / matrix, applied left to right as in SVG.
    private static Matrix? ParseTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Matrix.Identity;
        foreach (System.Text.RegularExpressions.Match op in System.Text.RegularExpressions.Regex.Matches(text, @"(\w+)\s*\(([^)]*)\)"))
        {
            var v = op.Groups[2].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Num(x, 0)).ToArray();
            var t = Matrix.Identity;
            switch (op.Groups[1].Value)
            {
                case "translate": t.Translate(v.ElementAtOrDefault(0), v.ElementAtOrDefault(1)); break;
                case "scale": t.Scale(v.ElementAtOrDefault(0), v.Length > 1 ? v[1] : v.ElementAtOrDefault(0)); break;
                case "rotate":
                    if (v.Length >= 3) t.RotateAt(v[0], v[1], v[2]); else t.Rotate(v.ElementAtOrDefault(0));
                    break;
                case "matrix" when v.Length == 6: t = new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]); break;
            }
            m = t * m;
        }
        return m;
    }
}
