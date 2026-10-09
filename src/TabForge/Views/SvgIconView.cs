using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>Small WPF vector renderer for the bundled boxed toolbar SVGs.</summary>
public sealed class SvgIconView : FrameworkElement
{
    private static readonly Dictionary<string, SvgAsset> Assets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object AssetLock = new();
    private static Dictionary<string, string>? AssetPaths;
    private string _icon = "play";

    public static readonly DependencyProperty IconColorProperty = DependencyProperty.Register(
        nameof(IconColor), typeof(Color), typeof(SvgIconView),
        new FrameworkPropertyMetadata(Color.FromRgb(0xC7, 0xCF, 0xDA), (owner, _) => ((SvgIconView)owner).InvalidateVisual()));

    public static readonly DependencyProperty ButtonHoveredProperty = DependencyProperty.Register(
        nameof(ButtonHovered), typeof(bool), typeof(SvgIconView),
        new FrameworkPropertyMetadata(false, (owner, _) => ((SvgIconView)owner).InvalidateVisual()));

    public static readonly DependencyProperty ShowFrameProperty = DependencyProperty.Register(
        nameof(ShowFrame), typeof(bool), typeof(SvgIconView), new FrameworkPropertyMetadata(true));

    public bool ButtonHovered
    {
        get => (bool)GetValue(ButtonHoveredProperty);
        set => SetValue(ButtonHoveredProperty, value);
    }

    public bool ShowFrame
    {
        get => (bool)GetValue(ShowFrameProperty);
        set => SetValue(ShowFrameProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public string Icon
    {
        get => _icon;
        set
        {
            if (string.Equals(_icon, value, StringComparison.OrdinalIgnoreCase)) return;
            _icon = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(96, availableSize.Width), Math.Min(96, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("SvgIconView"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "SvgIconView", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
        SvgAsset? asset;
        try { asset = LoadAsset(Icon); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bundled SVG could not be loaded: {ex}");
            return;
        }
        if (asset is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        if (asset.IsPaletteIcon)
        {
            var paletteScale = Math.Min(ActualWidth / 32, ActualHeight / 32);
            dc.PushTransform(new TranslateTransform((ActualWidth - 32 * paletteScale) / 2, (ActualHeight - 32 * paletteScale) / 2));
            dc.PushTransform(new ScaleTransform(paletteScale, paletteScale));
            DrawShapes(dc, asset.Shapes, IconColor);
            dc.Pop();
            dc.Pop();
            return;
        }

        var scale = Math.Min(ActualWidth / 96, ActualHeight / 96);
        var x = (ActualWidth - 96 * scale) / 2;
        var y = (ActualHeight - 96 * scale) / 2;
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var panelBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1)
        };
        var light = TabForge.Visualization.VisualTheme.IsLight;
        panelBrush.GradientStops.Add(new GradientStop(FrameFill(asset.PanelTop, light), 0));
        panelBrush.GradientStops.Add(new GradientStop(FrameFill(asset.PanelBottom, light), 1));
        if (ShowFrame)
            dc.DrawRoundedRectangle(panelBrush, new Pen(new SolidColorBrush(light ? ForTheme(asset.Border) ?? asset.Border : asset.Border), 2),
                new Rect(5, 5, 86, 86), 18, 18);

        DrawShapes(dc, asset.Shapes, IconColor);

        dc.Pop();
        dc.Pop();
    }

    private static Pen MakePen(Color color, double width) => new(new SolidColorBrush(color), width)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round
    };

    private static SvgAsset? LoadAsset(string icon)
    {
        lock (AssetLock)
        {
            if (Assets.TryGetValue(icon, out var cached)) return cached;
            var isPaletteIcon = icon.StartsWith("tool:", StringComparison.OrdinalIgnoreCase);
            AssetPaths ??= BuildBundledAssetIndex();
            if (!AssetPaths.TryGetValue(icon, out var path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > InputLimits.MaxBundledSvgBytes) return null;
            var xmlSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = InputLimits.MaxBundledSvgBytes,
                MaxCharactersFromEntities = 0,
                CloseInput = false
            };
            using var reader = XmlReader.Create(stream, xmlSettings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null) return null;

            var css = ParseCss(root);

            var stops = root.Descendants().Where(e => e.Name.LocalName == "stop").ToArray();
            var top = stops.Length > 0 ? ReadStop(stops[0]) : Color.FromRgb(0x22, 0x22, 0x22);
            var bottom = stops.Length > 1 ? ReadStop(stops[^1]) : top;
            var rectangle = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "rect");
            var border = ParseColor((string?)rectangle?.Attribute("stroke")) ?? Colors.Gray;
            var group = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "g");
            var inherited = ResolveStyle(root, css, null);
            if (group is not null) inherited = ResolveStyle(group, css, inherited);
            var groupWidth = ParseDouble(inherited.GetValueOrDefault("stroke-width"), 1);
            var shapes = new List<SvgShape>();

            var shapeElements = group is null ? root.Elements() : group.Elements();
            foreach (var element in shapeElements)
            {
                var geometry = ParseShapeGeometry(element);
                if (geometry is null) continue;
                ApplyTransform(geometry, (string?)element.Attribute("transform"));
                var style = ResolveStyle(element, css, inherited);
                var fillText = style.GetValueOrDefault("fill");
                var strokeText = style.GetValueOrDefault("stroke");
                var fill = ParseColor(fillText);
                var stroke = ParseColor(strokeText);
                var width = ParseDouble(style.GetValueOrDefault("stroke-width"), groupWidth);
                shapes.Add(new SvgShape(geometry, fill, stroke, width,
                    IsCurrentColor(fillText),
                    IsCurrentColor(strokeText)));
            }

            foreach (var textElement in root.Descendants().Where(element => element.Name.LocalName == "text"))
            {
                var text = string.Concat(textElement.Nodes().OfType<XText>().Select(node => node.Value));
                if (string.IsNullOrWhiteSpace(text)) continue;
                var style = new Dictionary<string, string>();
                foreach (var ancestor in textElement.Ancestors().Reverse())
                    style = ResolveStyle(ancestor, css, style);
                style = ResolveStyle(textElement, css, style);
                var color = ParseColor(style.GetValueOrDefault("fill")) ?? Colors.White;
                var family = style.GetValueOrDefault("font-family")?.Split(',')[0].Trim(' ', '\'', '"') ?? "Segoe UI";
                var size = ParseDouble(style.GetValueOrDefault("font-size"), 12);
                var weight = string.Equals(style.GetValueOrDefault("font-weight"), "600", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(style.GetValueOrDefault("font-weight"), "bold", StringComparison.OrdinalIgnoreCase)
                    ? FontWeights.SemiBold : FontWeights.Normal;
                var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal),
                    size, new SolidColorBrush(color), TabForge.Visualization.Draw.PixelsPerDip);
                var x = ParseDouble((string?)textElement.Attribute("x"), 0);
                var y = ParseDouble((string?)textElement.Attribute("y"), 0);
                var anchor = style.GetValueOrDefault("text-anchor");
                if (anchor == "middle") x -= formatted.Width / 2;
                else if (anchor == "end") x -= formatted.Width;
                var geometry = formatted.BuildGeometry(new Point(x, y - formatted.Baseline));
                ApplyTransform(geometry, (string?)textElement.Attribute("transform"));
                shapes.Add(new SvgShape(geometry, color, null, 0, FillCurrentColor: true));
            }

            cached = new SvgAsset(top, bottom, border, shapes, isPaletteIcon);
            Assets[icon] = cached;
            return cached;
        }
    }

    private static Dictionary<string, string> BuildBundledAssetIndex()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");
        AddBundledAssets(Path.Combine(assetsRoot, "TransportIcons"), "", recursive: false, paths);
        AddBundledAssets(Path.Combine(assetsRoot, "ToolIcons"), "tool:", recursive: true, paths);
        return paths;
    }

    private static void AddBundledAssets(string root, string prefix, bool recursive, Dictionary<string, string> result)
    {
        if (!Directory.Exists(root)) return;
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((fullRoot, 0));
        var entriesVisited = 0;
        while (pending.Count > 0)
        {
            if (result.Count >= InputLimits.MaxBundledSvgAssets || entriesVisited >= InputLimits.MaxBundledSvgIndexEntries) break;
            var (directory, depth) = pending.Pop();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (++entriesVisited > InputLimits.MaxBundledSvgIndexEntries) break;
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (recursive && depth < InputLimits.MaxBundledSvgDirectoryDepth)
                                pending.Push((entry, depth + 1));
                            continue;
                        }
                        if (!string.Equals(Path.GetExtension(entry), ".svg", StringComparison.OrdinalIgnoreCase)) continue;

                        var relative = Path.GetRelativePath(fullRoot, entry).Replace('\\', '/');
                        var identifier = prefix + relative[..^4];
                        if (!IsAssetIdentifier(identifier, prefix.StartsWith("tool:", StringComparison.Ordinal))) continue;
                        var canonicalPath = Path.GetFullPath(entry);
                        if (!canonicalPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        result.TryAdd(identifier, canonicalPath);
                        if (result.Count >= InputLimits.MaxBundledSvgAssets) break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
    }

    /// <summary>Key prefix of the tool-palette icons (Assets/ToolIcons).</summary>
    private const string ToolIconPrefix = "tool:";

    private static bool IsAssetIdentifier(string icon, bool palette)
    {
        if (palette)
        {
            if (!icon.StartsWith(ToolIconPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            icon = icon[ToolIconPrefix.Length..];
        }
        if (icon.Length == 0 || icon[0] == '/' || icon[^1] == '/') return false;
        foreach (var part in icon.Split('/'))
        {
            if (part.Length == 0 || part is "." or "..") return false;
            foreach (var character in part)
                if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) return false;
        }
        return true;
    }

    private static Geometry? ParseShapeGeometry(XElement element) => element.Name.LocalName switch
    {
        "path" when !string.IsNullOrWhiteSpace((string?)element.Attribute("d")) => ParsePath((string)element.Attribute("d")!),
        "ellipse" => new EllipseGeometry(new Point(ParseDouble((string?)element.Attribute("cx"), 0), ParseDouble((string?)element.Attribute("cy"), 0)),
            ParseDouble((string?)element.Attribute("rx"), 0), ParseDouble((string?)element.Attribute("ry"), 0)),
        "circle" => Circle(element),
        "rect" => new RectangleGeometry(new Rect(ParseDouble((string?)element.Attribute("x"), 0),
            ParseDouble((string?)element.Attribute("y"), 0), ParseDouble((string?)element.Attribute("width"), 0),
            ParseDouble((string?)element.Attribute("height"), 0))),
        _ => null
    };

    private static Dictionary<string, Dictionary<string, string>> ParseCss(XElement root)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var source = string.Join("\n", root.Descendants().Where(element => element.Name.LocalName == "style").Select(element => element.Value));
        foreach (Match match in Regex.Matches(source, @"\.([\w-]+)\s*\{([^}]*)\}"))
            result[match.Groups[1].Value] = ParseDeclarations(match.Groups[2].Value);
        return result;
    }

    private static Dictionary<string, string> ResolveStyle(XElement element,
        IReadOnlyDictionary<string, Dictionary<string, string>> css, Dictionary<string, string>? inherited)
    {
        var values = inherited is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
        var classes = ((string?)element.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in classes)
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

    private static Color ReadStop(XElement stop)
    {
        var color = ParseColor((string?)stop.Attribute("stop-color")) ?? Colors.Black;
        var opacity = ParseDouble((string?)stop.Attribute("stop-opacity"), 1);
        color.A = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        return color;
    }

    private static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        return TabForge.Visualization.ColourText.TryParse(text, out var colour) ? colour : null;
    }

    private static bool IsCurrentColor(string? text) =>
        string.Equals(text?.Trim(), "currentColor", StringComparison.OrdinalIgnoreCase);

    private static double ParseDouble(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static Geometry Circle(XElement element)
    {
        var radius = ParseDouble((string?)element.Attribute("r"), 0);
        return new EllipseGeometry(new Point(ParseDouble((string?)element.Attribute("cx"), 0),
            ParseDouble((string?)element.Attribute("cy"), 0)), radius, radius);
    }

    private static void ApplyTransform(Geometry geometry, string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform)) return;
        var match = Regex.Match(transform, @"rotate\(([^)]*)\)", RegexOptions.IgnoreCase);
        if (!match.Success) return;
        var values = Regex.Matches(match.Groups[1].Value, @"[-+]?(?:\d*\.)?\d+")
            .Select(value => double.Parse(value.Value, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length == 0) return;
        geometry.Transform = new RotateTransform(values[0], values.Length > 1 ? values[1] : 0, values.Length > 2 ? values[2] : 0);
    }

    // A frame's translucent dark tint turns a muddy mid-grey over the light theme; there it becomes an opaque pale tint of the same hue.
    internal static Color FrameFill(Color c, bool light) =>
        light ? Color.FromRgb((byte)(c.R + (255 - c.R) * 0.78), (byte)(c.G + (255 - c.G) * 0.78), (byte)(c.B + (255 - c.B) * 0.78)) : c;

    // The icon art is drawn for dark surfaces (pale strokes). On the light grey theme, pale shapes
    // would vanish, so they are darkened: greys become charcoal, tints keep their hue.
    internal static Color? ForTheme(Color? color)
    {
        if (color is not { } c || !TabForge.Visualization.VisualTheme.IsLight) return color;
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        var spread = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        if (spread < 48) return luminance < 0.55 ? c : Color.FromArgb(c.A, 0x2B, 0x30, 0x36);
        if (luminance < 0.22) return c;
        // Coloured art (transport buttons etc.) is tuned for dark chrome: use a deeper shade of the same hue.
        var k = luminance >= 0.55 ? 0.5 : 0.72;
        return Color.FromArgb(c.A, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    private void DrawShapes(DrawingContext dc, IReadOnlyList<SvgShape> shapes, Color iconColor)
    {
        foreach (var shape in shapes)
        {
            var fill = ForTheme(shape.FillCurrentColor ? iconColor : shape.Fill);
            var stroke = ForTheme(shape.StrokeCurrentColor ? iconColor : shape.Stroke);
            if (fill is { } fillColor)
            {
                if (ButtonHovered && !shape.FillCurrentColor)
                {
                    var glowColor = Color.FromArgb(72, fillColor.R, fillColor.G, fillColor.B);
                    dc.DrawGeometry(null, MakePen(glowColor, 9), shape.Geometry);
                }
                dc.DrawGeometry(new SolidColorBrush(fillColor), null, shape.Geometry);
            }
            if (stroke is { } strokeColor)
            {
                if (ButtonHovered && !shape.StrokeCurrentColor)
                {
                    var glowColor = Color.FromArgb(64, strokeColor.R, strokeColor.G, strokeColor.B);
                    dc.DrawGeometry(null, MakePen(glowColor, shape.StrokeWidth + 6), shape.Geometry);
                }
                dc.DrawGeometry(null, MakePen(strokeColor, shape.StrokeWidth), shape.Geometry);
            }
        }
    }

    private static Geometry ParsePath(string data)
    {
        var tokens = Regex.Matches(data, @"[MmLlHhVvCcSsQqZz]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?")
            .Select(match => match.Value).ToArray();
        var geometry = new PathGeometry();
        var index = 0;
        var command = ' ';
        var current = new Point();
        var previousControl = new Point();
        var start = new Point();
        PathFigure? figure = null;

        double Number() => double.Parse(tokens[index++], CultureInfo.InvariantCulture);
        Point Pair(bool relative)
        {
            var point = new Point(Number(), Number());
            return relative ? new Point(current.X + point.X, current.Y + point.Y) : point;
        }
        void Begin(Point point)
        {
            figure = new PathFigure { StartPoint = point, IsClosed = false, IsFilled = true };
            geometry.Figures.Add(figure);
            current = start = point;
        }

        while (index < tokens.Length)
        {
            if (tokens[index].Length == 1 && char.IsLetter(tokens[index][0])) command = tokens[index++][0];
            var relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var point = Pair(relative);
                    Begin(point);
                    command = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    var point = Pair(relative);
                    figure?.Segments.Add(new LineSegment(point, true));
                    current = point;
                    break;
                }
                case 'H':
                {
                    var x = Number();
                    var point = new Point(relative ? current.X + x : x, current.Y);
                    figure?.Segments.Add(new LineSegment(point, true));
                    current = point;
                    break;
                }
                case 'V':
                {
                    var y = Number();
                    var point = new Point(current.X, relative ? current.Y + y : y);
                    figure?.Segments.Add(new LineSegment(point, true));
                    current = point;
                    break;
                }
                case 'C':
                {
                    var control1 = Pair(relative);
                    var control2 = Pair(relative);
                    var point = Pair(relative);
                    figure?.Segments.Add(new BezierSegment(control1, control2, point, true));
                    previousControl = control2;
                    current = point;
                    break;
                }
                case 'S':
                {
                    var control1 = char.ToUpperInvariant(command) == 'S'
                        ? new Point(2 * current.X - previousControl.X, 2 * current.Y - previousControl.Y)
                        : current;
                    var control2 = Pair(relative);
                    var point = Pair(relative);
                    figure?.Segments.Add(new BezierSegment(control1, control2, point, true));
                    previousControl = control2;
                    current = point;
                    break;
                }
                case 'Q':
                {
                    var control = Pair(relative);
                    var point = Pair(relative);
                    figure?.Segments.Add(new QuadraticBezierSegment(control, point, true));
                    previousControl = control;
                    current = point;
                    break;
                }
                case 'Z':
                    if (figure is not null) figure.IsClosed = true;
                    current = start;
                    command = ' ';
                    break;
                default:
                    throw new FormatException($"Unsupported SVG path command '{command}'.");
            }
        }
        return geometry;
    }

    private sealed record SvgAsset(Color PanelTop, Color PanelBottom, Color Border, IReadOnlyList<SvgShape> Shapes, bool IsPaletteIcon);
    private sealed record SvgShape(Geometry Geometry, Color? Fill, Color? Stroke, double StrokeWidth,
        bool FillCurrentColor = false, bool StrokeCurrentColor = false);
}
