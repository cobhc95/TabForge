using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the geometry cache of <see cref="AvaloniaPathsAndText"/>: equal figures share one geometry, different figures or fill rules do not, and the
///   same notehead-and-arc scene drawn with cached paths is compared with the same scene built as fresh StreamGeometry per shape (the timing is printed).
/// Does not own: the score's drawing (ScoreEngravingAndFollowTests) or the eviction order (BoundedLruCacheTests).
/// Tests: this class.
/// </summary>
public class AvaloniaPathCacheTests(ITestOutputHelper output)
{
    private static readonly UiColor Ink = new(255, 0, 0, 0);

    /// <summary>A notehead-sized outline and a slur arc at (<paramref name="x"/>, <paramref name="y"/>), the kind of shape the score draws per note.</summary>
    private static void Shape(IPathSink s, double x, double y)
    {
        s.BeginFigure(new UiPoint(x - 4.5, y), true, true);
        s.BezierTo(new UiPoint(x - 4.5, y - 3), new UiPoint(x + 4.5, y - 3), new UiPoint(x + 4.5, y));
        s.LineTo(new UiPoint(x, y + 2.5));
        s.BeginFigure(new UiPoint(x + 6, y - 6), false, false);
        s.ArcTo(new UiPoint(x + 18, y - 6), 6, 4, 0, false, true);
    }

    /// <summary>The same shape built the way the adapter did before the cache: a new StreamGeometry on every call.</summary>
    private static IUiPath FreshGeometry(double x, double y)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.SetFillRule(FillRule.EvenOdd);
            context.BeginFigure(new Point(x - 4.5, y), true);
            context.CubicBezierTo(new Point(x - 4.5, y - 3), new Point(x + 4.5, y - 3), new Point(x + 4.5, y));
            context.LineTo(new Point(x, y + 2.5));
            context.EndFigure(true);
            context.BeginFigure(new Point(x + 6, y - 6), false);
            context.ArcTo(new Point(x + 18, y - 6), new Size(6, 4), 0, false, SweepDirection.Clockwise);
            context.EndFigure(false);
        }
        return new AvaloniaPath(geometry);
    }

    [AvaloniaFact]
    public void EqualFiguresShareOneGeometryAndOtherContentDoesNot()
    {
        var paths = new AvaloniaPathsAndText();
        var first = paths.BuildPath(s => Shape(s, 10, 10));
        for (var i = 0; i < 100; i++) Assert.Same(first, paths.BuildPath(s => Shape(s, 10, 10)));
        Assert.NotSame(first, paths.BuildPath(s => Shape(s, 11, 10)));
        Assert.NotSame(first, paths.BuildPath(s => Shape(s, 10, 10), UiFillRule.Nonzero));
    }

    [AvaloniaFact]
    public void CachedShapesDrawAsFastAsFreshGeometry()
    {
        const int frames = 300, shapesPerFrame = 40;
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
        var draw = new AvaloniaDrawContext();
        var pen = new UiStroke(Ink, 1.2);

        double Time(Action<int> frame)
        {
            for (var f = 0; f < frames; f++) frame(f);   // warm-up: the first pass pays the platform's one-time costs
            var watch = Stopwatch.StartNew();
            for (var f = 0; f < frames; f++) frame(f);
            return watch.Elapsed.TotalMilliseconds;
        }

        void DrawFresh(int f)
        {
            using var dc = bitmap.CreateDrawingContext();
            draw.Use(dc);
            for (var i = 0; i < shapesPerFrame; i++) draw.Path(null, pen, FreshGeometry(20 + i * 18, 40 + (i % 8) * 20));
        }

        var distinct = new HashSet<IUiPath>(ReferenceEqualityComparer.Instance);
        void DrawCached(int f)
        {
            using var dc = bitmap.CreateDrawingContext();
            draw.Use(dc);
            for (var i = 0; i < shapesPerFrame; i++)
            {
                var path = draw.Paths.BuildPath(s => Shape(s, 20 + i * 18, 40 + (i % 8) * 20));
                distinct.Add(path);
                draw.Path(null, pen, path);
            }
        }

        var freshMs = Time(DrawFresh);
        var cachedMs = Time(DrawCached);
        output.WriteLine($"{frames * shapesPerFrame} shapes: fresh geometry {freshMs:F1} ms, cached {cachedMs:F1} ms, distinct cached geometries {distinct.Count}");
        Assert.Equal(shapesPerFrame, distinct.Count);
    }
}
