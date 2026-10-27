using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace TabForge.Avalonia.Tests;

/// <summary>One thing the sweep found: the check that found it, how bad it is, where (shot, theme, size, state) and the PNG that shows it.</summary>
internal sealed record SweepFinding(string Check, string Severity, string Shot, string Theme, string Size, string State, string Detail, string Png);

/// <summary>The pixels of a decoded PNG as 0xAARRGGBB, row by row.</summary>
internal sealed class ShotPixels
{
    public int Width { get; }
    public int Height { get; }
    private readonly int[] _argb;

    private ShotPixels(int width, int height, int[] argb) { Width = width; Height = height; _argb = argb; }

    public static ShotPixels Load(string png)
    {
        using var stream = File.OpenRead(png);
        using var bitmap = WriteableBitmap.Decode(stream);
        using var locked = bitmap.Lock();
        var swap = locked.Format == PixelFormat.Rgba8888;
        var argb = new int[locked.Size.Width * locked.Size.Height];
        for (var y = 0; y < locked.Size.Height; y++)
            for (var x = 0; x < locked.Size.Width; x++)
            {
                var p = System.Runtime.InteropServices.Marshal.ReadInt32(locked.Address, y * locked.RowBytes + x * 4);
                if (swap) p = (p & unchecked((int)0xFF00FF00)) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
                argb[y * locked.Size.Width + x] = p;
            }
        return new ShotPixels(locked.Size.Width, locked.Size.Height, argb);
    }

    public int At(int x, int y) => _argb[y * Width + x];

    public (int X0, int Y0, int X1, int Y1) Clamp(Rect r) =>
        (Math.Max(0, (int)Math.Floor(r.X)), Math.Max(0, (int)Math.Floor(r.Y)), Math.Min(Width, (int)Math.Ceiling(r.Right)), Math.Min(Height, (int)Math.Ceiling(r.Bottom)));

    /// <summary>The most common colour (5-bit buckets) in the rectangle, leaving out <paramref name="hole"/>; null when no pixel is left.</summary>
    public Color? Modal(Rect rect, Rect? hole = null)
    {
        var (x0, y0, x1, y1) = Clamp(rect);
        var counts = new Dictionary<int, (int N, int Sample)>();
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                if (hole is { } h && h.Contains(new Point(x, y))) continue;
                var p = At(x, y);
                var key = (p >> 3) & 0x1F1F1F;
                counts[key] = counts.TryGetValue(key, out var e) ? (e.N + 1, e.Sample) : (1, p);
            }
        if (counts.Count == 0) return null;
        var best = counts.Values.OrderByDescending(v => v.N).First().Sample;
        return Color.FromRgb((byte)(best >> 16), (byte)(best >> 8), (byte)best);
    }

    public static Color ColourOf(int p) => Color.FromRgb((byte)(p >> 16), (byte)(p >> 8), (byte)p);

    public static double Lum(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    private static double Lin(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }

    public static double Contrast(Color a, Color b)
    {
        var la = Lum(a); var lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static Color Over(Color fg, double alpha, Color bg)
    {
        byte Mix(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return Color.FromRgb(Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }

    /// <summary>The number of pixels in two rectangles (one in each frame) whose colours differ by more than rounding.</summary>
    public static int Differing(ShotPixels a, Rect ra, ShotPixels b, Rect rb)
    {
        var (ax, ay, ax1, ay1) = a.Clamp(ra);
        var (bx, by, bx1, by1) = b.Clamp(rb);
        var w = Math.Min(ax1 - ax, bx1 - bx); var h = Math.Min(ay1 - ay, by1 - by);
        var n = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var p = a.At(ax + x, ay + y); var q = b.At(bx + x, by + y);
                if (Math.Abs(((p >> 16) & 255) - ((q >> 16) & 255)) + Math.Abs(((p >> 8) & 255) - ((q >> 8) & 255)) + Math.Abs((p & 255) - (q & 255)) > 6) n++;
            }
        return n;
    }
}

/// <summary>
/// Owns: the automatic checks over one rendered shot (its PNG pixels and the visual tree it came from): text and icon contrast, invisible
/// icons, dark regions in the light theme and clipped text. Does not own: the shots (ShotSweepTests) or the report.
/// </summary>
internal static class ShotAnalysis
{
    private const int MaxPerCheck = 25;

    public static List<SweepFinding> Run(Visual root, ShotPixels px, bool light, bool darkPatches, string shot, string theme, string size, string state, string png, Dictionary<string, int> checkedCounts)
    {
        var list = new List<SweepFinding>();
        void Add(SweepFinding f)
        {
            if (list.Count(x => x.Check == f.Check) < MaxPerCheck && !list.Any(x => x.Check == f.Check && x.Detail == f.Detail)) list.Add(f);
        }
        void Count(string check) => checkedCounts[check] = checkedCounts.GetValueOrDefault(check) + 1;
        SweepFinding F(string check, string sev, string detail) => new(check, sev, shot, theme, size, state, detail, png);

        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is not Control c || !c.IsEffectivelyVisible || c.Bounds.Width < 1 || c.Bounds.Height < 1) continue;
            var rect = RectIn(c, root);
            if (rect is not { } r || r.Right < 0 || r.Bottom < 0 || r.X > px.Width || r.Y > px.Height) continue;
            // Exempt: a control scrolled or squeezed out of its clipping ancestor is not on screen (nothing of it can be read), and one only partly
            // visible has a backdrop the pixels cannot tell apart from what covers it, so it is judged for clipping but not for contrast.
            var share = VisibleShare(c, root, r);
            if (share < 0.5) continue;
            var whole = share >= 0.9;
            var effOpacity = EffectiveOpacity(c, root);
            if (effOpacity < 0.05) continue;
            var enabled = c.IsEffectivelyEnabled;
            if (c is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                if (whole && tb.Foreground is ISolidColorBrush brush && r.Width * r.Height >= 16 && px.Modal(r) is { } bg)
                {
                    var ink = ShotPixels.Over(brush.Color, brush.Color.A / 255.0 * brush.Opacity * effOpacity, bg);
                    var ratio = ShotPixels.Contrast(ink, bg);
                    var large = tb.FontSize >= 18.66 || (tb.FontSize >= 14 && tb.FontWeight >= FontWeight.SemiBold);
                    var need = !enabled ? 1.5 : large ? 3.0 : 4.5;
                    Count("text-contrast");
                    if (ratio < need)
                        Add(F("text-contrast", !enabled ? "Low" : ratio < 3.0 ? "High" : "Medium", $"\"{Trim(tb.Text)}\" {ratio:0.0}:1 (needs {need:0.0}) at {Describe(r)}{(enabled ? "" : " [disabled]")}"));
                }
                Count("clipped-text");
                if (ClippedBy(tb, root, r) is { } clip) Add(F("clipped-text", "Medium", $"\"{Trim(tb.Text)}\" {clip} at {Describe(r)}"));
            }
            else if (c is Shape shape && whole && enabled && IsIconShape(shape) && !InToggleTemplate(shape))
            {
                var fillBrush = shape.Fill ?? shape.Stroke;
                if (fillBrush is ISolidColorBrush sb && sb.Color.A * sb.Opacity * effOpacity >= 12 && px.Modal(r.Inflate(3), r) is { } bg)
                {
                    var ratio = ShotPixels.Contrast(ShotPixels.Over(sb.Color, sb.Color.A / 255.0 * sb.Opacity * effOpacity, bg), bg);
                    Count("icon-contrast");
                    if (ratio < 1.3) Add(F("icon-invisible", "High", $"{shape.GetType().Name} {ratio:0.00}:1 against its surround at {Describe(r)}"));
                    else if (ratio < 3.0) Add(F("icon-contrast", "Medium", $"{shape.GetType().Name} {ratio:0.0}:1 (needs 3.0) at {Describe(r)}"));
                }
            }
            else if (c is Button button && whole && enabled && c.Bounds.Width <= 80 && c.Bounds.Height <= 80 && !HasContentVisuals(button))
            {
                // A button that draws its own icon (no text or shape inside): the strongest pixel of its middle against the middle's main colour.
                var inner = r.Deflate(4);
                if (inner.Width < 4 || inner.Height < 4 || px.Modal(inner) is not { } bg) continue;
                var (x0, y0, x1, y1) = px.Clamp(inner);
                double max = 1; var strong = 0;
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        var q = ShotPixels.Contrast(ShotPixels.ColourOf(px.At(x, y)), bg);
                        if (q > max) max = q;
                        if (q >= 1.3) strong++;
                    }
                Count("icon-contrast");
                if (IsColourSwatch(button)) { }   // exempt: a swatch is filled with the colour it shows, which may equal its surround; it has no icon to see
                else if (strong < 4 && px.Modal(r.Inflate(3), r) is { } ring && ShotPixels.Contrast(bg, ring) >= 1.3) { }  // a colour swatch: filled, not an icon
                else if (strong < 4) Add(F("icon-invisible", "High", $"{button.GetType().Name} \"{button.Name}\" draws nothing visible at {Describe(r)}"));
                else if (max < 3.0) Add(F("icon-contrast", "Medium", $"{button.GetType().Name} \"{button.Name}\" icon {max:0.0}:1 (needs 3.0) at {Describe(r)}"));
            }
        }
        // Exempt: the instrument panel draws a wood-brown fretboard in both themes, a dark region by design.
        var instruments = root.GetVisualDescendants().OfType<global::TabForge.Avalonia.Platform.InstrumentPanelView>().Where(v => v.IsEffectivelyVisible).Select(v => RectIn(v, root)).OfType<Rect>().ToList();
        if (darkPatches && light) DarkPatches(px, list, instruments, shot, theme, size, state, png, checkedCounts);
        return list;
    }

    private static bool IsIconShape(Shape s) =>
        (s is global::Avalonia.Controls.Shapes.Path { Data: not null } && s.Bounds.Width <= 64 && s.Bounds.Height <= 64)
        || (s is Ellipse or Rectangle && s.Bounds.Width is >= 6 and <= 32 && s.Bounds.Height is >= 6 and <= 32 && !IsLegendDot(s));

    // Exempt: a dot of 6-12 px (a status or legend dot such as the recent-note dot) is a coloured key, not an icon; its dimmed fill is by design.
    private static bool IsLegendDot(Shape s) => s is Ellipse && s.Bounds.Width <= 12 && s.Bounds.Height <= 12;

    // Radio dots, check ticks and their outlines are template parts whose fill follows state (a fill the same as the surface is by design), not icons.
    private static bool InToggleTemplate(Shape s) => s.GetVisualAncestors().OfType<ToggleButton>().Any();

    private static bool IsColourSwatch(Button b) =>
        (b.Content is null or "—") && b.Background is ISolidColorBrush { Color.A: > 0 }
        || b.Content is Border { Background: ISolidColorBrush { Color.A: > 0 } };

    private static bool HasContentVisuals(Button b) => b.GetVisualDescendants().Any(v => v is TextBlock { Text.Length: > 0 } or Shape or Image or PathIcon);

    private static void DarkPatches(ShotPixels px, List<SweepFinding> list, List<Rect> instruments, string shot, string theme, string size, string state, string png, Dictionary<string, int> counts)
    {
        const int Cell = 24;
        counts["dark-in-light"] = counts.GetValueOrDefault("dark-in-light") + 1;
        var cols = px.Width / Cell; var rows = px.Height / Cell;
        var dark = new bool[cols, rows];
        for (var cy = 0; cy < rows; cy++)
            for (var cx = 0; cx < cols; cx++)
            {
                double sum = 0;
                for (var y = 0; y < Cell; y += 2)
                    for (var x = 0; x < Cell; x += 2) sum += ShotPixels.Lum(ShotPixels.ColourOf(px.At(cx * Cell + x, cy * Cell + y)));
                dark[cx, cy] = sum / (Cell * Cell / 4) < 0.08;
            }
        var seen = new bool[cols, rows];
        for (var cy = 0; cy < rows; cy++)
            for (var cx = 0; cx < cols; cx++)
            {
                if (!dark[cx, cy] || seen[cx, cy]) continue;
                var stack = new Stack<(int, int)>(); stack.Push((cx, cy)); seen[cx, cy] = true;
                int n = 0, minX = cx, maxX = cx, minY = cy, maxY = cy;
                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop(); n++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || seen[nx, ny] || !dark[nx, ny]) continue;
                        seen[nx, ny] = true; stack.Push((nx, ny));
                    }
                }
                if (n < 8 || list.Count(f => f.Check == "dark-in-light") >= MaxPerCheck) continue;
                var region = new Rect(minX * Cell, minY * Cell, (maxX - minX + 1) * Cell, (maxY - minY + 1) * Cell);
                if (instruments.Any(i => i.Inflate(Cell).Contains(region))) continue;
                var share = n * Cell * Cell / (double)(px.Width * px.Height);
                list.Add(new SweepFinding("dark-in-light", share >= 0.08 ? "High" : "Medium", shot, theme, size, state,
                    $"dark region {(maxX - minX + 1) * Cell}x{(maxY - minY + 1) * Cell} px at ({minX * Cell},{minY * Cell}), {share:P0} of the shot", png));
            }
    }

    private static string? ClippedBy(TextBlock tb, Visual root, Rect r)
    {
        if (tb.TextTrimming != TextTrimming.None || string.IsNullOrEmpty(tb.Text)) return null;
        var ft = new FormattedText(tb.Text.Replace("_", ""), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black);
        if (tb.TextWrapping == TextWrapping.NoWrap)
        {
            if (ft.Width > tb.Bounds.Width + 1.5) return $"is {ft.Width:0} px wide in a {tb.Bounds.Width:0} px box";
            for (var a = tb.GetVisualParent(); a is not null && !ReferenceEquals(a, root); a = a.GetVisualParent())
                if (a is Control { ClipToBounds: true } ac && a is not ScrollViewer && RectIn(ac, root) is { } ar && r.X + ft.Width > ar.Right + 1.5)
                    return $"runs {r.X + ft.Width - ar.Right:0} px past its clipping {a.GetType().Name}";
            return null;
        }
        ft.MaxTextWidth = Math.Max(1, tb.Bounds.Width);
        return tb.MaxLines == 0 && ft.Height > tb.Bounds.Height + 2 ? $"needs {ft.Height:0} px height in a {tb.Bounds.Height:0} px box" : null;
    }

    // The share (0 to 1) of the control's box that lies inside every clipping ancestor.
    private static double VisibleShare(Control c, Visual root, Rect r)
    {
        var share = 1.0;
        for (var a = c.GetVisualParent(); a is not null && !ReferenceEquals(a, root); a = a.GetVisualParent())
        {
            if (a is not Control { ClipToBounds: true } ac || RectIn(ac, root) is not { } ar) continue;
            var overlap = ar.Intersect(r);
            share = Math.Min(share, r.Width * r.Height <= 0 ? 1 : overlap.Width * overlap.Height / (r.Width * r.Height));
        }
        return share;
    }

    public static Rect? RectIn(Control c, Visual root)
    {
        var origin = c.TranslatePoint(new Point(0, 0), root);
        return origin is { } o ? new Rect(o, c.Bounds.Size) : null;
    }

    private static double EffectiveOpacity(Visual v, Visual root)
    {
        var o = 1.0;
        for (Visual? a = v; a is not null; a = a.GetVisualParent())
        {
            o *= a.Opacity;
            if (ReferenceEquals(a, root)) break;
        }
        return o;
    }

    private static string Trim(string s) => (s.Length > 40 ? s[..40] + "..." : s).Replace('\n', ' ');

    private static string Describe(Rect r) => $"({r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0})";
}
