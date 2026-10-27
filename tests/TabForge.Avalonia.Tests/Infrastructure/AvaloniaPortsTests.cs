using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the Avalonia path and text-measure adapter check. Tests: a known path's bounds match the adapter's (10, 5, 130, 35, from TestDrawContextPort) within 0.5 px; a path and text render to a PNG.</summary>
public class AvaloniaPortsTests
{
    [AvaloniaFact]
    public void PathBoundsMatchWpfAndSceneRenders()
    {
        var ports = new AvaloniaPathsAndText();
        var path = ports.BuildPath(s =>
        {
            s.BeginFigure(new UiPoint(10, 10), true, true);
            s.LineTo(new UiPoint(60, 10));
            s.BezierTo(new UiPoint(80, 10), new UiPoint(80, 40), new UiPoint(60, 40));
            s.LineTo(new UiPoint(10, 40));
            s.BeginFigure(new UiPoint(100, 25), false, false);
            s.ArcTo(new UiPoint(140, 25), 20, 20, 0, false, true);
        });
        var b = ports.Bounds(path);
        Assert.InRange(b.X, 9.5, 10.5);
        Assert.InRange(b.Y, 4.5, 5.5);
        Assert.InRange(b.Width, 129.5, 130.5);
        Assert.InRange(b.Height, 34.5, 35.5);

        var m = ports.Measure("Ab", "Arial", 12, 700, false, 1);
        Assert.True(m.Width > 0 && m.Height > 0 && m.Baseline > 0 && m.Baseline <= m.Height);

        using var bitmap = new RenderTargetBitmap(new PixelSize(150, 70), new Vector(96, 96));
        var draw = new AvaloniaDrawContext();
        var ink = new UiColor(255, 0, 0, 0);
        using (var dc = bitmap.CreateDrawingContext())
        {
            draw.Use(dc);
            draw.Rect(new UiColor(255, 255, 255, 255), null, 0, 0, 150, 70);
            draw.Path(ink, new UiStroke(ink, 1), path);
            draw.DrawText(draw.Text("Ab", "Arial", 12, ink, 1), 10, 48);
        }
        var png = Path.Combine(Path.GetTempPath(), $"tf-avalonia-ports-{Guid.NewGuid():N}.png");
        bitmap.Save(png);
        Assert.True(new FileInfo(png).Length > 0);
    }
}
