using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Capture;

// Owns: one rendered picture as plain pixels: rendering a control at a fixed scale, cropping, the PNG file and the RGBA bytes a GIF takes.
// Does not own: which control is shot or where the file goes (the shot steps).
// Tests: TabForge.Avalonia.Tests.CaptureImageTests.
internal sealed class CaptureImage
{
    private readonly byte[] _bgra;

    private CaptureImage(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        _bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Renders <paramref name="visual"/> (its own bounds, no operating-system frame) at <paramref name="scale"/> times 96 DPI: 2 gives crisp images from layout done at 1x.</summary>
    public static CaptureImage Render(Visual visual, double scale)
    {
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(visual.Bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(visual.Bounds.Height * scale)));
        var dpi = new Vector(96 * scale, 96 * scale);
        using var target = new RenderTargetBitmap(size, dpi);
        AvaloniaDrawContext.WithClipsSkipped(scale != 1, () => target.Render(visual));   // Avalonia drops what is drawn after a clip when a bitmap renders at another scale (see the flag)
        using var readback = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var locked = readback.Lock();
        target.CopyPixels(locked, AlphaFormat.Premul);
        var bytes = new byte[size.Width * size.Height * 4];
        for (var y = 0; y < size.Height; y++)
            Marshal.Copy(locked.Address + y * locked.RowBytes, bytes, y * size.Width * 4, size.Width * 4);
        return new CaptureImage(size.Width, size.Height, bytes);
    }

    /// <summary>The part of the picture at <paramref name="x"/>, <paramref name="y"/> of the given size (pixels), clipped to the picture.</summary>
    public CaptureImage Crop(int x, int y, int width, int height)
    {
        if (x >= Width || y >= Height || x + width <= 0 || y + height <= 0) throw new ArgumentException($"The crop {x},{y} {width}x{height} lies outside the {Width}x{Height} picture.");
        var left = Math.Clamp(x, 0, Width - 1);
        var top = Math.Clamp(y, 0, Height - 1);
        var w = Math.Clamp(width, 1, Width - left);
        var h = Math.Clamp(height, 1, Height - top);
        var bytes = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(_bgra, ((top + row) * Width + left) * 4, bytes, row * w * 4, w * 4);
        return new CaptureImage(w, h, bytes);
    }

    /// <summary>The picture on a larger canvas, top-left aligned, the rest filled with the picture's top-left colour (frames of one GIF must share a size).</summary>
    public CaptureImage PadTo(int width, int height)
    {
        if (width == Width && height == Height) return this;
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < bytes.Length; i += 4) Buffer.BlockCopy(_bgra, 0, bytes, i, 4);
        for (var y = 0; y < Height; y++) Buffer.BlockCopy(_bgra, y * Width * 4, bytes, y * width * 4, Width * 4);
        return new CaptureImage(width, height, bytes);
    }

    /// <summary>Writes the picture as a PNG, creating the folder.</summary>
    public void SavePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var straight = Unpremultiplied();
        using (var locked = bitmap.Lock())
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(straight, y * Width * 4, locked.Address + y * locked.RowBytes, Width * 4);
        }
        bitmap.Save(path);
    }

    /// <summary>The pixels with the colour divided by the alpha again (the render holds premultiplied colour, a PNG straight colour); opaque pixels are copied as they are.</summary>
    private byte[] Unpremultiplied()
    {
        var bytes = (byte[])_bgra.Clone();
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var alpha = bytes[i + 3];
            if (alpha is 0 or 255) continue;
            for (var c = 0; c < 3; c++) bytes[i + c] = (byte)Math.Min(255, (bytes[i + c] * 255 + alpha / 2) / alpha);
        }
        return bytes;
    }

    /// <summary>The pixels as R, G, B, A bytes, the order a GIF frame takes.</summary>
    public byte[] ToRgba()
    {
        var straight = Unpremultiplied();
        var rgba = new byte[straight.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = straight[i + 2];
            rgba[i + 1] = straight[i + 1];
            rgba[i + 2] = straight[i];
            rgba[i + 3] = straight[i + 3];
        }
        return rgba;
    }

    /// <summary>The share of pixels that are not the picture's top-left colour; 0 for a blank picture (a shot that rendered nothing).</summary>
    public double InkShare()
    {
        var first = BitConverter.ToInt32(_bgra, 0);
        var different = 0;
        for (var i = 0; i < _bgra.Length; i += 4)
            if (BitConverter.ToInt32(_bgra, i) != first) different++;
        return different / (double)(_bgra.Length / 4);
    }
}
