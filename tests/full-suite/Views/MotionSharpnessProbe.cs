using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TabForge;

/// <summary>
/// Owns: measuring whether moving content stays crisp. A root element is drawn off-screen at a display scale (root DPI and bitmap DPI
/// agree), moved by a fractional amount, and compared with a still picture of the same content moved by whole device pixels:
/// identical pixels mean the motion is pixel-aligned, a difference means the edges were resampled (motion blur).
/// Does not own: the views, their scrolling code or the pass/fail rules (the tests in SelfTestMotionSharpness).
/// </summary>
internal static class MotionSharpnessProbe
{
    /// <summary>A picture of <paramref name="root"/> (a parentless element, already sized) at <paramref name="scale"/> device pixels per DIP.</summary>
    internal static BitmapSource Shoot(FrameworkElement root, double scale)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    /// <summary>Gives a parentless element the display scale and a size, and lays it out.</summary>
    internal static void Stage(FrameworkElement root, double scale, double width, double height)
    {
        VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static byte[] Pixels(BitmapSource b)
    {
        var px = new byte[b.PixelWidth * b.PixelHeight * 4];
        b.CopyPixels(px, b.PixelWidth * 4, 0);
        return px;
    }

    /// <summary>
    /// Compares <paramref name="moved"/> with <paramref name="still"/> moved by (<paramref name="dx"/>, <paramref name="dy"/>) device pixels over
    /// <paramref name="region"/> (device pixels of <paramref name="moved"/>). Diff: mean absolute channel difference (0 = pixel-identical).
    /// Sharp: squared-gradient energy of <paramref name="moved"/> divided by that of the still picture (1 = as crisp, below 1 = blurred).
    /// </summary>
    internal static (double Diff, double Sharp) Compare(BitmapSource moved, BitmapSource still, int dx, int dy, Int32Rect region)
    {
        var a = Pixels(moved);
        var b = Pixels(still);
        int w = moved.PixelWidth, h = moved.PixelHeight;
        var x0 = Math.Max(region.X, Math.Max(1, 1 - dx));
        var y0 = Math.Max(region.Y, Math.Max(1, 1 - dy));
        var x1 = Math.Min(region.X + region.Width, Math.Min(w, still.PixelWidth - dx) - 1);
        var y1 = Math.Min(region.Y + region.Height, Math.Min(h, still.PixelHeight - dy) - 1);
        double diff = 0, energyMoved = 0, energyStill = 0;
        long n = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * w + x) * 4;
                var j = ((y + dy) * still.PixelWidth + x + dx) * 4;
                for (var c = 0; c < 3; c++)
                {
                    diff += Math.Abs(a[i + c] - b[j + c]);
                    { double gx = a[i + c] - a[i - 4 + c], gy = a[i + c] - a[i - w * 4 + c]; energyMoved += gx * gx + gy * gy; }
                    { double gx = b[j + c] - b[j - 4 + c], gy = b[j + c] - b[j - still.PixelWidth * 4 + c]; energyStill += gx * gx + gy * gy; }
                }
                n += 3;
            }
        return n == 0 ? (0, 1) : (diff / n, energyStill <= 0 ? 1 : energyMoved / energyStill);
    }

    /// <summary>
    /// Columns a thin vertical line covers only partly: <paramref name="with"/> minus <paramref name="without"/> (the same picture with the line hidden)
    /// along row <paramref name="row"/>. A line on whole device pixels has none; a line between pixels has two soft edge columns.
    /// </summary>
    internal static int PartialColumns(BitmapSource with, BitmapSource without, int row)
    {
        var a = Pixels(with);
        var b = Pixels(without);
        var w = with.PixelWidth;
        var diff = new int[w];
        for (var x = 0; x < w; x++)
        {
            var i = (row * w + x) * 4;
            diff[x] = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
        }
        var max = diff.Max();
        if (max == 0) return 0;
        return diff.Count(d => d > 0.1 * max && d < 0.9 * max);
    }

    /// <summary>
    /// <see cref="Compare"/> over the shifts within one device pixel of the expected one: a view may round a half pixel the other way, but a
    /// resampled (blurred) picture matches no whole-pixel shift.
    /// </summary>
    internal static (double Diff, double Sharp) Best(BitmapSource moved, BitmapSource still, int dx, int dy, bool vertical, Int32Rect region)
    {
        var best = (Diff: double.MaxValue, Sharp: 1.0);
        for (var d = -1; d <= 1; d++)
        {
            var r = Compare(moved, still, vertical ? dx : dx + d, vertical ? dy + d : dy, region);
            if (r.Diff < best.Diff) best = r;
        }
        return best;
    }


    /// <summary>Writes the same <paramref name="region"/> of several pictures side by side, each enlarged by <paramref name="zoom"/>, as one PNG (for looking at single pixels).</summary>
    internal static void SaveStrip(IReadOnlyList<BitmapSource> frames, Int32Rect region, int zoom, string path)
    {
        var cell = region.Width * zoom;
        var width = frames.Count * (cell + 4);
        var height = region.Height * zoom;
        var big = new byte[width * height * 4];
        Array.Fill(big, (byte)255);
        for (var f = 0; f < frames.Count; f++)
        {
            var px = Pixels(new CroppedBitmap(frames[f], region));
            for (var y = 0; y < height; y++)
                for (var x = 0; x < cell; x++)
                    Array.Copy(px, ((y / zoom) * region.Width + x / zoom) * 4, big, (y * width + f * (cell + 4) + x) * 4, 4);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, big, width * 4)));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
}
