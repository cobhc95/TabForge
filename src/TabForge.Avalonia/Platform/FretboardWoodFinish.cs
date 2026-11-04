using System;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the subtle, theme-independent finish on the wood textures, applied once when decoded.
// Does not own: lighting, other fretboard materials, or the original image files (which stay untouched).
// Tests: FretboardWoodFinishTests.
internal static class FretboardWoodFinish
{
    internal static Bitmap Apply(Bitmap source)
    {
        var result = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        try
        {
            using var locked = result.Lock();
            source.CopyPixels(locked, AlphaFormat.Unpremul);
            var width = source.PixelSize.Width * 4;
            var pixels = new byte[width * source.PixelSize.Height];
            for (var y = 0; y < source.PixelSize.Height; y++)
                Marshal.Copy(locked.Address + y * locked.RowBytes, pixels, y * width, width);
            Calm(pixels);
            for (var y = 0; y < source.PixelSize.Height; y++)
                Marshal.Copy(pixels, y * width, locked.Address + y * locked.RowBytes, width);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Opaque wood BGRA pixels: 4% less grain contrast about each texture's own mean, then 6% darker.</summary>
    internal static void Calm(byte[] pixels)
    {
        if (pixels.Length == 0) return;
        // Separate channel means retain the individual wood's colour. No neighbouring samples or blur:
        // grain edges, knots and pores remain in exactly the same places at the original resolution.
        var mean = new double[3];
        for (var i = 0; i < pixels.Length; i += 4)
            for (var c = 0; c < 3; c++) mean[c] += pixels[i + c];
        for (var c = 0; c < 3; c++) mean[c] /= pixels.Length / 4;
        for (var i = 0; i < pixels.Length; i += 4)
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)Math.Clamp(Math.Round(0.94 * (mean[c] + 0.96 * (pixels[i + c] - mean[c]))), 0, 255);
    }
}
