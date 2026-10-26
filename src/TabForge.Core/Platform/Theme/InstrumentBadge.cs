using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Platform.Theme;

// Owns: reading one instrument badge SVG (Assets/Instruments/SVG) into a toolkit-free model: shapes with their composed 2D matrix, paints, stroke and
//       fill rule, and the circle with id "background" kept apart so the painter can colour it with the track or family colour.
// Does not own: drawing (Avalonia InstrumentBadgeView), the catalogue (InstrumentCatalog) or any cache.
// Tests: TabForge.Avalonia.Tests.InstrumentBadgeTests.
public sealed record InstrumentBadge(BadgeShape? Background, IReadOnlyList<BadgeShape> Art)
{
    private const string BackgroundId = "background";
    private static readonly string[] InheritedStyle = { "fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "fill-rule", "opacity" };

    /// <summary>Reads the badge at <paramref name="path"/>; null when the file is missing, larger than the bundled-icon limit or not XML.</summary>
    public static InstrumentBadge? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > InputLimits.MaxBundledSvgBytes) return null;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = InputLimits.MaxBundledSvgBytes, CloseInput = false };
            using var reader = XmlReader.Create(stream, settings);
            return Parse(XDocument.Load(reader, LoadOptions.None));
        }
        // A bundled badge that cannot be read leaves its tile without art, as a missing file does.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            Trace.Error(Trace.Ui, "instrument badge: " + path + ": " + ex.Message);
            return null;
        }
    }

    public static InstrumentBadge? Parse(XDocument document)
    {
        if (document.Root is not { } root) return null;
        BadgeShape? background = null;
        var art = new List<BadgeShape>();
        Walk(root, BadgeMatrix.Identity, new Dictionary<string, string>(), art, ref background);
        return new InstrumentBadge(background, art);
    }

    private static void Walk(XElement element, BadgeMatrix parent, Dictionary<string, string> inherited, List<BadgeShape> art, ref BadgeShape? background)
    {
        var name = element.Name.LocalName;
        if (name is "title" or "desc") return;
        var style = new Dictionary<string, string>(inherited);
        foreach (var attribute in element.Attributes())
            if (Array.IndexOf(InheritedStyle, attribute.Name.LocalName) >= 0) style[attribute.Name.LocalName] = attribute.Value;
        var matrix = BadgeMatrix.Parse((string?)element.Attribute("transform")) is { } own ? own.Then(parent) : parent;
        if (name is "g" or "svg")
        {
            foreach (var child in element.Elements()) Walk(child, matrix, style, art, ref background);
            return;
        }
        if (ShapeGeometry(element) is not { } geometry) return;
        var opacity = Number(style.GetValueOrDefault("opacity"), 1);
        var fill = style.TryGetValue("fill", out var fillText) ? Paint(fillText, opacity) : new UiColor(255, 0, 0, 0);
        var stroke = style.TryGetValue("stroke", out var strokeText) ? Paint(strokeText, opacity) : null;
        var shape = new BadgeShape(geometry, matrix, fill, stroke, Number(style.GetValueOrDefault("stroke-width"), 1),
            style.GetValueOrDefault("stroke-linecap") == "round", style.GetValueOrDefault("stroke-linejoin") == "round", style.GetValueOrDefault("fill-rule") == "evenodd");
        if ((string?)element.Attribute("id") == BackgroundId) background = shape;
        else if (fill is not null || stroke is not null) art.Add(shape);
    }

    private static SvgGeometry? ShapeGeometry(XElement e)
    {
        double A(string n, double d = 0) => Number((string?)e.Attribute(n), d);
        return e.Name.LocalName switch
        {
            "rect" => new SvgRectGeometry(A("x"), A("y"), Math.Max(0, A("width")), Math.Max(0, A("height"))),
            "circle" => new SvgEllipseGeometry(A("cx"), A("cy"), A("r"), A("r")),
            "ellipse" => new SvgEllipseGeometry(A("cx"), A("cy"), A("rx"), A("ry")),
            "path" when !string.IsNullOrWhiteSpace((string?)e.Attribute("d")) => new SvgPathGeometry((string)e.Attribute("d")!),
            _ => null,
        };
    }

    // Hex colours only, as the bundled badges use; "none" or anything else paints nothing. The opacity folds into the alpha.
    private static UiColor? Paint(string text, double opacity)
    {
        if (string.IsNullOrWhiteSpace(text) || !ColourHex.TryParse(text, out var rgba)) return null;
        return new UiColor((byte)Math.Round(rgba.A * Math.Clamp(opacity, 0, 1)), rgba.R, rgba.G, rgba.B);
    }

    internal static double Number(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

/// <summary>A badge shape: <see cref="Matrix"/> maps its own units into the badge's; paints are null when absent.</summary>
public sealed record BadgeShape(SvgGeometry Geometry, BadgeMatrix Matrix, UiColor? Fill, UiColor? Stroke, double StrokeWidth, bool RoundCaps, bool RoundJoins, bool EvenOdd);

/// <summary>A 2D affine matrix in the row-vector convention (x' = x*M11 + y*M21 + OffsetX), the one Avalonia's matrices use.</summary>
public readonly record struct BadgeMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static readonly BadgeMatrix Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>This matrix, then <paramref name="next"/>.</summary>
    public BadgeMatrix Then(BadgeMatrix next) => new(
        M11 * next.M11 + M12 * next.M21, M11 * next.M12 + M12 * next.M22,
        M21 * next.M11 + M22 * next.M21, M21 * next.M12 + M22 * next.M22,
        OffsetX * next.M11 + OffsetY * next.M21 + next.OffsetX, OffsetX * next.M12 + OffsetY * next.M22 + next.OffsetY);

    /// <summary>The matrix of an SVG transform list (translate, scale, rotate, matrix); null when empty. Each operation composes before the earlier ones.</summary>
    public static BadgeMatrix? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var result = Identity;
        foreach (Match op in Regex.Matches(text, @"(\w+)\s*\(([^)]*)\)"))
        {
            var v = op.Groups[2].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => InstrumentBadge.Number(x, 0)).ToArray();
            double At(int i) => i < v.Length ? v[i] : 0;
            var step = op.Groups[1].Value switch
            {
                "translate" => new BadgeMatrix(1, 0, 0, 1, At(0), At(1)),
                "scale" => new BadgeMatrix(At(0), 0, 0, v.Length > 1 ? v[1] : At(0), 0, 0),
                "rotate" => Rotation(At(0), v.Length >= 3 ? v[1] : 0, v.Length >= 3 ? v[2] : 0),
                "matrix" when v.Length == 6 => new BadgeMatrix(v[0], v[1], v[2], v[3], v[4], v[5]),
                _ => Identity,
            };
            result = step.Then(result);
        }
        return result;
    }

    private static BadgeMatrix Rotation(double degrees, double cx, double cy)
    {
        var radians = degrees * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        return new BadgeMatrix(cos, sin, -sin, cos, cx - cx * cos + cy * sin, cy - cx * sin - cy * cos);
    }
}
