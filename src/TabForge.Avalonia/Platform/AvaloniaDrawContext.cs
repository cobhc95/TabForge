using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// <see cref="IDrawContext"/> over an Avalonia <see cref="DrawingContext"/>, the counterpart of the WPF adapter. One instance per view, reused for
/// every frame through <see cref="Use"/>; it keeps one immutable brush per colour, one immutable pen per colour, width and caps and one typeface
/// per family, so frames allocate nothing beyond new text layouts (shaped once per key in this instance's cache) and the pushed-state stack entries.
/// Avalonia pushes return a state to dispose; this class keeps them on a stack so <see cref="Pop"/> ends the last push, as in WPF.
/// Owns: the Avalonia drawing mapping and its brush, pen and typeface caches. Does not own: the drawing context's lifetime (the caller's render pass).
/// Tests: AvaloniaDrawContextTests (TabForge.Avalonia.Tests).
/// </summary>
public sealed class AvaloniaDrawContext : IDrawContext
{
    // Brush cache. Key: ARGB; ceiling: CacheLimit entries, least recently drawn dropped first; thread: the view's UI thread only.
    private readonly BoundedLruCache<uint, ImmutableSolidColorBrush> _brushes = new(CacheLimit);
    // Pen cache. Key: the whole UiStroke; ceiling: CacheLimit entries, least recently drawn dropped first; thread: the view's UI thread only.
    private readonly BoundedLruCache<UiStroke, ImmutablePen> _pens = new(CacheLimit);
    // Typeface cache. Key: (family, weight, italic); ceiling: CacheLimit entries, least recently drawn dropped first; thread: the view's UI thread only.
    private readonly BoundedLruCache<(string, int, bool), Typeface> _faces = new(CacheLimit);
    private const int CacheLimit = 256;
    private readonly Stack<DrawingContext.PushedState> _pushed = new();
    private DrawingContext? _dc;

    /// <summary>Draws into <paramref name="dc"/> until the next call; throws when the last frame left pushes open (a missing <see cref="Pop"/>).</summary>
    public AvaloniaDrawContext Use(DrawingContext dc)
    {
        _dc = dc;
        if (_pushed.Count == 0) return this;
        var open = _pushed.Count;
        _pushed.Clear();
        throw new InvalidOperationException($"AvaloniaDrawContext: the last frame left {open} push(es) without a Pop.");
    }

    private DrawingContext Dc => _dc ?? throw new InvalidOperationException("AvaloniaDrawContext.Use was not called.");

    /// <summary>The cached brush of the colour.</summary>
    public ImmutableSolidColorBrush Brush(UiColor c)
    {
        if (_brushes.TryGet(c.Argb, out var b)) return b;
        b = new ImmutableSolidColorBrush(AvaloniaPalette.ColorOf(c));
        _brushes.Add(c.Argb, b);
        return b;
    }

    /// <summary>The cached pen of the stroke.</summary>
    public ImmutablePen Pen(UiStroke s)
    {
        if (_pens.TryGet(s, out var p)) return p;
        var dashes = s.Dash > 0 ? new ImmutableDashStyle(new[] { s.Dash, s.Gap }, 0) : null;
        p = new ImmutablePen(Brush(s.Colour), s.Width, dashes, s.RoundCaps ? PenLineCap.Round : PenLineCap.Flat, s.RoundJoins ? PenLineJoin.Round : PenLineJoin.Miter);
        _pens.Add(s, p);
        return p;
    }

    private Typeface Face(string family, int weight, bool italic)
    {
        var key = (family, weight, italic);
        if (_faces.TryGet(key, out var f)) return f;
        f = new Typeface(new FontFamily(family), italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)weight);
        _faces.Add(key, f);
        return f;
    }

    /// <summary>Draws a rectangle.</summary>
    public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) =>
        Dc.DrawRectangle(fill is { } f ? Brush(f) : null, stroke is { } s ? Pen(s) : null, new Rect(x, y, width, height), radius, radius);

    /// <summary>Draws a line.</summary>
    public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) => Dc.DrawLine(Pen(stroke), new Point(x1, y1), new Point(x2, y2));

    /// <summary>Draws an ellipse.</summary>
    public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) =>
        Dc.DrawEllipse(fill is { } f ? Brush(f) : null, stroke is { } s ? Pen(s) : null, new Point(cx, cy), rx, ry);

    /// <summary>Draws a path geometry.</summary>
    public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) =>
        Dc.DrawGeometry(fill is { } f ? Brush(f) : null, stroke is { } s ? Pen(s) : null, ((AvaloniaPath)path).Geometry);

    /// <summary>
    /// Lays out text, shaped once per <see cref="UiTextKey"/> in this instance's cache. <paramref name="pixelsPerDip"/> is unused because Avalonia scales
    /// text with the render target, and <paramref name="idealLayout"/> is ignored because Avalonia has one text formatting mode (ideal, with no
    /// pixel-snapped display mode); both stay in the key so the cache matches the WPF one.
    /// </summary>
    public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
        int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
    {
        var key = UiTextKey.Of(text, fontFamily, size, colour, weight, italic, outline, pixelsPerDip, maxWidth, align, idealLayout);
        if (!_texts.TryGet(key, out var shaped))
        {
            var t = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(fontFamily, weight, italic), size, Brush(colour));
            if (maxWidth > 0) t.MaxTextWidth = maxWidth;
            if (align != UiTextAlign.Left) t.TextAlignment = align == UiTextAlign.Centre ? TextAlignment.Center : TextAlignment.Right;
            shaped = new AvaloniaText(t, outline is { } o ? Pen(o) : null);
            _texts.Add(key, shaped);
        }
        return new UiText(shaped.Text.Width, shaped.Text.Height, shaped.Text.Baseline, shaped);
    }

    // Shaped text, reused because shaping is the costliest per-frame step and the same strings repeat; callers never mutate it. Per instance,
    // not shared, because Avalonia text holds native resources of the platform that made it. Key: UiTextKey; ceiling: TextCacheLimit entries,
    // least recently drawn dropped first; thread: the view's UI thread only.
    private readonly BoundedLruCache<UiTextKey, AvaloniaText> _texts = new(TextCacheLimit);
    private const int TextCacheLimit = 2048;

    /// <summary>Avalonia lays text out in DIPs; the scale is 1.</summary>
    public double PixelsPerDip => 1.0;
    // Geometry cache of this instance's paths; one instance per draw context, so one per view on its UI thread.
    private readonly AvaloniaPathsAndText _paths = new();
    /// <summary>This instance's path factory.</summary>
    public IPathFactory Paths => _paths;

    // Set only inside WithClipsSkipped, per logical flow, so a normal render running meanwhile still clips.
    private static readonly AsyncLocal<bool> ClipsSkipped = new();

    /// <summary>
    /// Runs <paramref name="render"/> with every clip push a no-op when <paramref name="skip"/> is true. The capture uses it while it renders a bitmap at a scale
    /// other than 1: there Avalonia drops (or mis-scales) the text, paths and images drawn after a clip push, so clip names, waveforms and section titles vanish.
    /// A real window at 150% or 200% draws them correctly (verified headlessly), so the app never sets it. Scoped to this logical flow, not a process switch.
    /// </summary>
    public static void WithClipsSkipped(bool skip, Action render)
    {
        using (WithClipsSkippedScope(skip)) render();
    }

    /// <summary>The same scope as a disposable, for async code (<c>using var scope = ...</c> around awaits): dispose it in the flow that opened it.</summary>
    public static IDisposable WithClipsSkippedScope(bool skip)
    {
        var before = ClipsSkipped.Value;
        ClipsSkipped.Value = skip;
        return new ClipsScope(before);
    }

    private sealed class ClipsScope : IDisposable
    {
        private readonly bool _before;
        private bool _done;
        public ClipsScope(bool before) => _before = before;
        public void Dispose() { if (_done) return; _done = true; ClipsSkipped.Value = _before; }
    }

    /// <summary>Whether clip pushes are no-ops in this logical flow (see <see cref="WithClipsSkipped"/>).</summary>
    internal static bool ClipsAreSkipped => ClipsSkipped.Value;

    /// <summary>Draws laid-out text at the point.</summary>
    public void DrawText(in UiText text, double x, double y)
    {
        var t = (AvaloniaText)text.Layout;
        if (t.OutlinePen is { } pen && t.OutlineGeometry is { } glyphs)
            using (Dc.PushTransform(Matrix.CreateTranslation(x, y))) Dc.DrawGeometry(null, pen, glyphs);
        Dc.DrawText(t.Text, new Point(x, y));
    }

    /// <summary>Draws an image scaled to the rectangle.</summary>
    public void Image(IUiImage image, double x, double y, double width, double height) => Dc.DrawImage(((AvaloniaImage)image).Source, new Rect(x, y, width, height));

    /// <summary>Clips later drawing to the rectangle.</summary>
    public void PushClip(double x, double y, double width, double height) =>
        _pushed.Push(ClipsAreSkipped ? Dc.PushTransform(Matrix.Identity) : Dc.PushClip(new Rect(x, y, width, height)));
    /// <summary>Moves later drawing by the offset.</summary>
    public void PushTranslate(double dx, double dy) => _pushed.Push(Dc.PushTransform(Matrix.CreateTranslation(dx, dy)));
    /// <summary>Scales later drawing about the centre.</summary>
    public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) =>
        _pushed.Push(Dc.PushTransform(Matrix.CreateTranslation(-centreX, -centreY) * Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(centreX, centreY)));
    /// <summary>Rotates later drawing about the centre.</summary>
    public void PushRotate(double degrees, double centreX, double centreY) =>
        _pushed.Push(Dc.PushTransform(Matrix.CreateTranslation(-centreX, -centreY) * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(centreX, centreY)));
    /// <summary>Ends the last push.</summary>
    public void Pop() => _pushed.Pop().Dispose();

    /// <summary>Draws a rectangle with a handle pen.</summary>
    public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) =>
        Dc.DrawRectangle(UiHandles.Own<AvaloniaPaint>(fill)?.Brush, UiHandles.Own<AvaloniaPen>(pen)?.Pen, new Rect(x, y, width, height), radiusX, radiusY);
    /// <summary>Draws a line with a handle pen.</summary>
    public void Line(IUiPen pen, double x1, double y1, double x2, double y2) => Dc.DrawLine(((AvaloniaPen)pen).Pen, new Point(x1, y1), new Point(x2, y2));
    /// <summary>Draws an ellipse with a handle pen.</summary>
    public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) =>
        Dc.DrawEllipse(UiHandles.Own<AvaloniaPaint>(fill)?.Brush, UiHandles.Own<AvaloniaPen>(pen)?.Pen, new Point(cx, cy), rx, ry);
    /// <summary>Draws a path geometry with a handle pen.</summary>
    public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) => Dc.DrawGeometry(UiHandles.Own<AvaloniaPaint>(fill)?.Brush, UiHandles.Own<AvaloniaPen>(pen)?.Pen, ((AvaloniaPath)path).Geometry);
    /// <summary>Draws a retained drawing.</summary>
    public void Drawing(IUiDrawing drawing) => ((AvaloniaDrawing)drawing).Drawing.Draw(Dc);
    /// <summary>Clips later drawing to the path.</summary>
    public void PushClip(IUiPath clip) => _pushed.Push(ClipsAreSkipped ? Dc.PushTransform(Matrix.Identity) : Dc.PushGeometryClip(((AvaloniaPath)clip).Geometry));
    /// <summary>Applies the transform to later drawing.</summary>
    public void PushTransform(IUiTransform transform) => _pushed.Push(Dc.PushTransform(((AvaloniaTransform)transform).Transform.Value));
    /// <summary>Multiplies later drawing by the opacity.</summary>
    public void PushOpacity(double opacity) => _pushed.Push(Dc.PushOpacity(opacity));
}

/// <summary>An Avalonia geometry as an <see cref="IUiPath"/>.</summary>
public sealed record AvaloniaPath(Geometry Geometry) : IUiPath;

/// <summary>Laid-out Avalonia text and the outline drawn behind it; the outline's glyph geometry is built once, at the origin.</summary>
public sealed class AvaloniaText(FormattedText text, IPen? outlinePen)
{
    /// <summary>The laid-out text.</summary>
    public FormattedText Text { get; } = text;
    /// <summary>The outline pen, if any.</summary>
    public IPen? OutlinePen { get; } = outlinePen;
    /// <summary>The outline glyph geometry at the origin, if any.</summary>
    public Geometry? OutlineGeometry { get; } = outlinePen is null ? null : text.BuildGeometry(new Point(0, 0));
}

/// <summary>An Avalonia brush as an <see cref="IUiPaint"/>.</summary>
public sealed record AvaloniaPaint(IBrush Brush) : IUiPaint;

/// <summary>An Avalonia pen as an <see cref="IUiPen"/>.</summary>
public sealed record AvaloniaPen(IPen Pen) : IUiPen;

/// <summary>An Avalonia transform as an <see cref="IUiTransform"/>.</summary>
public sealed record AvaloniaTransform(Transform Transform) : IUiTransform;

/// <summary>An Avalonia drawing as an <see cref="IUiDrawing"/>.</summary>
public sealed record AvaloniaDrawing(Drawing Drawing) : IUiDrawing;

/// <summary>An Avalonia image as an <see cref="IUiImage"/>.</summary>
public sealed record AvaloniaImage(IImage Source) : IUiImage;
