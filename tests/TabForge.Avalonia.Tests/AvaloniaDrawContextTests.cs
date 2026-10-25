using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the Avalonia drawing adapter check. Tests: a fixed scene (line, rect, text, clip) renders with Skia at the expected pixels.</summary>
public class AvaloniaDrawContextTests
{
    private static readonly UiColor Red = new(255, 255, 0, 0);
    private static readonly UiColor Blue = new(255, 0, 0, 255);
    private static readonly UiColor White = new(255, 255, 255, 255);

    [AvaloniaFact]
    public void FixedSceneDrawsLineRectAndClip()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(100, 100), new Vector(96, 96));
        var draw = new AvaloniaDrawContext();
        using (var dc = bitmap.CreateDrawingContext())
        {
            draw.Use(dc);
            draw.Rect(White, null, 0, 0, 100, 100);
            draw.Line(new UiStroke(Red, 4), 0, 10, 100, 10);
            draw.PushClip(0, 0, 50, 100);
            draw.Rect(Blue, null, 20, 40, 60, 20); // clipped at x = 50
            draw.Pop();
            draw.DrawText(draw.Text("Ab", "Arial", 12, Red, 1), 5, 80);
        }
        var path = Path.Combine(Path.GetTempPath(), $"tf-avalonia-draw-{Guid.NewGuid():N}.png");
        bitmap.Save(path);

        Assert.Equal(Red, Pixel(bitmap, 50, 10));
        Assert.Equal(Blue, Pixel(bitmap, 30, 50));
        Assert.Equal(White, Pixel(bitmap, 70, 50)); // outside the clip
        Assert.Equal(White, Pixel(bitmap, 50, 30));
    }

    internal static UiColor Pixel(Bitmap bitmap, int x, int y)
    {
        using var wb = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        bitmap.CopyPixels(fb, AlphaFormat.Premul);
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, bytes, 0, 4);
        return new UiColor(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
}
