using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Platform.Theme;

// Owns: reading one bundled icon SVG into a toolkit-free model (the frame panel's colours and border; shapes as path data, ellipses or
//       rectangles with their rotation, paints and stroke width; texts with font, size, weight, anchor and rotation), and the light-theme
//       shading every icon is drawn with (ForTheme, FrameFill).
// Does not own: drawing (WPF SvgIconView and WpfSvgGeometry; Avalonia AvaloniaSvgIcon), the lookup of an icon by name, or any cache.
// Tests: SvgIconTests (full suite) pins the WPF output of this reader; TabForge.Avalonia.Tests.SvgIconTests pins the Avalonia side.
public sealed record SvgIcon(UiColor PanelTop, UiColor PanelBottom, UiColor Border, IReadOnlyList<SvgShape> Shapes, IReadOnlyList<SvgText> Texts)
{
    // The SVG reader keeps the icon's own colours; the WPF and Avalonia painters decide what to draw with them.
    private const string SvgNone = "none";
    private const string SvgCurrentColour = "currentColor";

    /// <summary>Reads the icon file at <paramref name="path"/>; null when the file is larger than the bundled-icon limit.</summary>
    public static SvgIcon? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > InputLimits.MaxBundledSvgBytes) return null;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = InputLimits.MaxBundledSvgBytes,
            MaxCharactersFromEntities = 0,
            CloseInput = false
        };
        using var reader = XmlReader.Create(stream, settings);
        return Parse(XDocument.Load(reader, LoadOptions.None));
    }

    /// <summary>Reads an icon document; null when it has no root element.</summary>
    public static SvgIcon? Parse(XDocument document)
    {
        var root = document.Root;
        if (root is null) return null;
        var css = ParseCss(root);

        var stops = root.Descendants().Where(e => e.Name.LocalName == "stop").ToArray();
        var top = stops.Length > 0 ? ReadStop(stops[0]) : Palette.Get("SvgIconFallbackStop");
        var bottom = stops.Length > 1 ? ReadStop(stops[^1]) : top;
        var rectangle = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "rect");
        var border = ParseColour((string?)rectangle?.Attribute("stroke")) ?? Palette.Get("Gray");

        // Shapes come from the first group's children when there is a group, else from the root; the frame rectangle sits outside the group.
        var group = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "g");
        var inherited = ResolveStyle(root, css, null);
        if (group is not null) inherited = ResolveStyle(group, css, inherited);
        var groupWidth = ParseDouble(inherited.GetValueOrDefault("stroke-width"), 1);
        var shapes = new List<SvgShape>();
        foreach (var element in group is null ? root.Elements() : group.Elements())
        {
            var geometry = ShapeGeometry(element);
            if (geometry is null) continue;
            var style = ResolveStyle(element, css, inherited);
            shapes.Add(new SvgShape(geometry, ReadRotation((string?)element.Attribute("transform")),
                ReadPaint(style.GetValueOrDefault("fill")), ReadPaint(style.GetValueOrDefault("stroke")),
                ParseDouble(style.GetValueOrDefault("stroke-width"), groupWidth)));
        }

        var texts = root.Descendants().Where(e => e.Name.LocalName == "text")
            .Select(element => ReadText(element, css)).Where(text => text is not null).Select(text => text!).ToArray();
        return new SvgIcon(top, bottom, border, shapes, texts);
    }

    /// <summary>The colour of a frame panel in the light theme: an opaque pale tint of the same hue (a dark translucent tint turns muddy there).</summary>
    public static UiColor FrameFill(UiColor c, bool light) => light
        ? new UiColor(255, (byte)(c.R + (255 - c.R) * 0.78), (byte)(c.G + (255 - c.G) * 0.78), (byte)(c.B + (255 - c.B) * 0.78))
        : c;

    /// <summary>
    /// The colour an icon paints with in the given theme. The art is drawn for dark surfaces (pale strokes); on the light theme pale
    /// shapes become the light theme's icon-default token, and coloured art takes a deeper shade of its hue.
    /// </summary>
    public static UiColor ForTheme(UiColor c, bool light)
    {
        if (!light) return c;
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        var spread = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        if (spread < 48)
        {
            if (luminance < 0.55) return c;
            var ink = Palette.Get("IconDefault", light: true);   // the icon-default master token, not a charcoal of its own
            return new UiColor(c.A, ink.R, ink.G, ink.B);
        }
        if (luminance < 0.22) return c;
        var k = luminance >= 0.55 ? 0.5 : 0.72;
        return new UiColor(c.A, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    private static SvgGeometry? ShapeGeometry(XElement element) => element.Name.LocalName switch
    {
        "path" when !string.IsNullOrWhiteSpace((string?)element.Attribute("d")) => new SvgPathGeometry((string)element.Attribute("d")!),
        "ellipse" => new SvgEllipseGeometry(Number(element, "cx"), Number(element, "cy"), Number(element, "rx"), Number(element, "ry")),
        "circle" => new SvgEllipseGeometry(Number(element, "cx"), Number(element, "cy"), Number(element, "r"), Number(element, "r")),
        "rect" => new SvgRectGeometry(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height")),
        _ => null
    };

    private static SvgText? ReadText(XElement element, Dictionary<string, Dictionary<string, string>> css)
    {
        var text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value));
        if (string.IsNullOrWhiteSpace(text)) return null;
        var style = new Dictionary<string, string>();
        foreach (var ancestor in element.Ancestors().Reverse())
            style = ResolveStyle(ancestor, css, style);
        style = ResolveStyle(element, css, style);
        var family = style.GetValueOrDefault("font-family")?.Split(',')[0].Trim(' ', '\'', '"') ?? "Segoe UI";
        var fontWeight = style.GetValueOrDefault("font-weight");
        var bold = string.Equals(fontWeight, "600", StringComparison.OrdinalIgnoreCase) || string.Equals(fontWeight, "bold", StringComparison.OrdinalIgnoreCase);
        var anchor = style.GetValueOrDefault("text-anchor") switch
        {
            "middle" => SvgTextAnchor.Middle,
            "end" => SvgTextAnchor.End,
            _ => SvgTextAnchor.Start
        };
        return new SvgText(text, Number(element, "x"), Number(element, "y"), family, ParseDouble(style.GetValueOrDefault("font-size"), 12),
            bold ? SemiBoldWeight : NormalWeight, anchor, ReadRotation((string?)element.Attribute("transform")),
            ParseColour(style.GetValueOrDefault("fill")) ?? Palette.Get("White"));
    }

    // CSS weights of the two text weights the icons use: normal and semibold.
    private const int NormalWeight = 400;
    private const int SemiBoldWeight = 600;

    // The rotate(angle cx cy) of a transform attribute; null when there is none.
    private static SvgRotation? ReadRotation(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform)) return null;
        var match = Regex.Match(transform, @"rotate\(([^)]*)\)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var values = Regex.Matches(match.Groups[1].Value, @"[-+]?(?:\d*\.)?\d+")
            .Select(value => double.Parse(value.Value, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length == 0) return null;
        return new SvgRotation(values[0], values.Length > 1 ? values[1] : 0, values.Length > 2 ? values[2] : 0);
    }

    private static Dictionary<string, Dictionary<string, string>> ParseCss(XElement root)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var source = string.Join("\n", root.Descendants().Where(element => element.Name.LocalName == "style").Select(element => element.Value));
        foreach (Match match in Regex.Matches(source, @"\.([\w-]+)\s*\{([^}]*)\}"))
            result[match.Groups[1].Value] = ParseDeclarations(match.Groups[2].Value);
        return result;
    }

    // Later sources win: the inherited style, then the element's classes, its style attribute, and its presentation attributes.
    private static Dictionary<string, string> ResolveStyle(XElement element, IReadOnlyDictionary<string, Dictionary<string, string>> css,
        Dictionary<string, string>? inherited)
    {
        var values = inherited is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
        foreach (var name in ((string?)element.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (css.TryGetValue(name, out var declarations))
                foreach (var (key, value) in declarations) values[key] = value;
        if (!string.IsNullOrWhiteSpace((string?)element.Attribute("style")))
            foreach (var (key, value) in ParseDeclarations((string)element.Attribute("style")!)) values[key] = value;
        foreach (var name in new[] { "fill", "stroke", "stroke-width", "font-size", "font-family", "font-weight", "text-anchor" })
            if (element.Attribute(name) is { } attribute) values[name] = attribute.Value;
        return values;
    }

    private static Dictionary<string, string> ParseDeclarations(string source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var divider = entry.IndexOf(':');
            if (divider <= 0) continue;
            result[entry[..divider].Trim()] = entry[(divider + 1)..].Trim();
        }
        return result;
    }

    private static UiColor ReadStop(XElement stop)
    {
        var colour = ParseColour((string?)stop.Attribute("stop-color")) ?? Palette.Get("Black");
        var opacity = ParseDouble((string?)stop.Attribute("stop-opacity"), 1);
        return colour with { A = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255) };
    }

    private static SvgPaint? ReadPaint(string? text)
    {
        if (string.Equals(text?.Trim(), SvgCurrentColour, StringComparison.OrdinalIgnoreCase)) return new SvgPaint(default, true);
        return ParseColour(text) is { } colour ? new SvgPaint(colour, false) : null;
    }

    // Hex colours only ("#RGB", "#RRGGBB", "#AARRGGBB"), which is what the bundled icons use; anything else paints nothing.
    private static UiColor? ParseColour(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals(SvgNone, StringComparison.OrdinalIgnoreCase)) return null;
        if (!ColourHex.TryParse(text, out var rgba)) return null;
        return rgba.ToUi();
    }

    private static double Number(XElement element, string attribute) => ParseDouble((string?)element.Attribute(attribute), 0);

    private static double ParseDouble(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

/// <summary>A paint of an icon: a colour, or the icon colour when the SVG says <c>currentColor</c> (then <see cref="Colour"/> is unused).</summary>
public readonly record struct SvgPaint(UiColor Colour, bool IsCurrentColour);

/// <summary>A rotation by <see cref="Degrees"/> about (<see cref="CentreX"/>, <see cref="CentreY"/>).</summary>
public readonly record struct SvgRotation(double Degrees, double CentreX, double CentreY);

/// <summary>The outline of a shape, in the icon's own units.</summary>
public abstract record SvgGeometry;

/// <summary>Path data in the SVG path mini-language.</summary>
public sealed record SvgPathGeometry(string Data) : SvgGeometry;

/// <summary>An ellipse; a circle is one with equal radii.</summary>
public sealed record SvgEllipseGeometry(double CentreX, double CentreY, double RadiusX, double RadiusY) : SvgGeometry;

/// <summary>An axis-aligned rectangle.</summary>
public sealed record SvgRectGeometry(double X, double Y, double Width, double Height) : SvgGeometry;

/// <summary>A filled and/or stroked shape; a paint is null when the SVG gives none.</summary>
public sealed record SvgShape(SvgGeometry Geometry, SvgRotation? Rotation, SvgPaint? Fill, SvgPaint? Stroke, double StrokeWidth);

public enum SvgTextAnchor { Start, Middle, End }

/// <summary>
/// A text element. <see cref="Colour"/> is the colour the SVG gives it; the icon colour is drawn instead, as the WPF icon does, so it only
/// reaches the model for inspection. <see cref="Weight"/> is the CSS weight, 400 or 600.
/// </summary>
public sealed record SvgText(string Text, double X, double Y, string FontFamily, double FontSize, int Weight, SvgTextAnchor Anchor,
    SvgRotation? Rotation, UiColor Colour);
