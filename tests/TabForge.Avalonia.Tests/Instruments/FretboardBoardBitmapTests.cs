using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the fretboard's still bitmap shows the same neck on a scaled display as at 100% (wood in place, wires and inlays there).
/// Tests: InstrumentPanelView.BoardCache.
/// </summary>
public class FretboardBoardBitmapTests
{
    private static byte[] Pixels(RenderTargetBitmap bitmap, string name)
    {
        var size = bitmap.PixelSize;
        using var readback = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = readback.Lock();
        bitmap.CopyPixels(locked, AlphaFormat.Premul);
        var bytes = new byte[size.Width * size.Height * 4];
        for (var y = 0; y < size.Height; y++) Marshal.Copy(locked.Address + y * locked.RowBytes, bytes, y * size.Width * 4, size.Width * 4);
        if (Environment.GetEnvironmentVariable("TF_VIDEO_DUMP") is { } dir) bitmap.Save(Path.Combine(dir, name));
        return bytes;
    }

    [AvaloniaFact]
    public void AScaledDisplayShowsTheSameBoardAsOneAtFullSize()
    {
        var view = new InstrumentPanelView();
        view.Show(DemoSongFactory.Create().Tracks[0], Array.Empty<InstrumentLitNotes.Lit>());
        var window = new Window { Width = 1400, Height = 220, Content = view };
        window.Show();
        window.UpdateLayout();
        var ink = AvaloniaPalette.InkOf(false);
        var one = view.BoardBitmapAt(1, ink);
        var width = one.PixelSize.Width; var height = one.PixelSize.Height;
        var plain = Pixels(one, "board1.png");
        var two = Pixels(view.BoardBitmapAt(2, ink), "board2.png");
        long difference = 0, samples = 0;
        for (var y = 0; y < height; y += 2)
            for (var x = 0; x < width; x += 2)
            {
                difference += Math.Abs(plain[(y * width + x) * 4 + 1] - two[(y * 2 * width * 2 + x * 2) * 4 + 1]);
                samples++;
            }
        window.Close();
        Assert.InRange(difference / (double)samples, 0, 12);
    }
}
