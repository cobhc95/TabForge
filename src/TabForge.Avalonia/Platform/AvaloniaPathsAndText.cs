using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// <see cref="IPathFactory"/> and <see cref="ITextMeasure"/> over Avalonia: StreamGeometry and FormattedText.
/// Owns: the Avalonia geometry and text-measure mapping, and the geometry cache of the paths it builds. Does not own: the callers' own caches.
/// Tests: AvaloniaPortsTests, AvaloniaPathCacheTests (TabForge.Avalonia.Tests).
/// </summary>
public sealed class AvaloniaPathsAndText : IPathFactory, ITextMeasure
{
    // Geometry cache. Key: the path's content (PathShapeKey: fill rule and every figure's exact bits), so equal content shares one geometry;
    // ceiling: PathCacheLimit geometries AND PathCacheBytes estimated bytes, LRU; thread: the owner's UI thread (one instance per draw context).
    // Callers never mutate a built path, so sharing one is safe.
    private const int PathCacheLimit = 1024;
    private const long PathCacheBytes = 4 * 1024 * 1024;
    // Includes the exact managed key plus conservative geometry/entry overhead. Native allocator/GPU overhead is not a measured byte count.
    private readonly BoundedLruCache<PathShapeKey, AvaloniaPath> _geometries = new(PathCacheLimit, PathCacheBytes, static (key, _) => 256L + key.Words.LongLength * sizeof(long) * 3);
    private readonly PathRecorder _recorder = new();
    internal long CachedPathBytes => _geometries.Weight;
    internal int CachedPaths => _geometries.Count;

    public IUiPath BuildPath(Action<IPathSink> figures, UiFillRule fillRule = UiFillRule.EvenOdd)
    {
        var key = _recorder.Record(figures, fillRule);
        if (_geometries.TryGet(key, out var cached)) return cached;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(fillRule == UiFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
            var sink = new Sink(context);
            PathRecorder.Replay(key.Words, sink);
            sink.EndOpenFigure();
        }
        var path = new AvaloniaPath(geometry);
        _geometries.Add(key, path);
        return path;
    }

    public IUiPath BuildUncachedPath(Action<IPathSink> figures, UiFillRule fillRule = UiFillRule.EvenOdd)
    {
        // Direct construction: no coordinate-array key, hash, replay or cache entry for an owner-cached waveform.
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(fillRule == UiFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
            var sink = new Sink(context);
            figures(sink);
            sink.EndOpenFigure();
        }
        return new AvaloniaPath(geometry);
    }

    public UiRect Bounds(IUiPath path)
    {
        var b = ((AvaloniaPath)path).Geometry.Bounds;
        return new UiRect(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary><paramref name="pixelsPerDip"/> is unused: Avalonia scales text with the render target.</summary>
    private readonly FontProvider _fonts = AvaloniaFonts.Provider;

    public UiTextMetrics Measure(string text, string fontFamily, double size, int weight, bool italic, double pixelsPerDip)
    {
        var face = new Typeface(AvaloniaFonts.Family(fontFamily, _fonts), italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)weight);
        var t = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(AvaloniaPalette.ColorOf(UiTokens.MeasureInk)));
        return new UiTextMetrics(t.Width, t.Height, t.Baseline, t.Extent);
    }

    public bool FontExists(string fontFamily) =>
        FontManager.Current.SystemFonts.Any(f => string.Equals(f.Name, fontFamily, StringComparison.OrdinalIgnoreCase));

    /// <summary>Avalonia ends each figure explicitly; the sink ends the open one when the next begins or the build finishes.</summary>
    private sealed class Sink(StreamGeometryContext context) : IPathSink
    {
        private bool _open, _closed;
        private static Point P(UiPoint p) => new(p.X, p.Y);

        public void EndOpenFigure() { if (_open) context.EndFigure(_closed); _open = false; }

        public void BeginFigure(UiPoint start, bool filled, bool closed)
        {
            EndOpenFigure();
            context.BeginFigure(P(start), filled);
            _open = true;
            _closed = closed;
        }

        public void LineTo(UiPoint point, bool smoothJoin = false) => context.LineTo(P(point));
        public void BezierTo(UiPoint control1, UiPoint control2, UiPoint end, bool smoothJoin = false) => context.CubicBezierTo(P(control1), P(control2), P(end));
        public void QuadraticTo(UiPoint control, UiPoint end) => context.QuadraticBezierTo(P(control), P(end));
        public void ArcTo(UiPoint end, double radiusX, double radiusY, double rotation, bool largeArc, bool clockwise) =>
            context.ArcTo(P(end), new Size(radiusX, radiusY), rotation, largeArc, clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
    }
}
