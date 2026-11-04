using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public class FretboardWoodFinishTests
{
    [AvaloniaFact]
    public void EveryShippedWoodIsFinishedOnceAndOtherMaterialsStayUntouched()
    {
        var assets = new FretboardAssets();
        foreach (var name in FretboardStyles.Woods.Select(FretboardStyles.WoodFile)
                     .Concat(FretboardStyles.Bindings.Select(FretboardStyles.BindingFile).OfType<string>())
                     .Concat(FretboardStyles.InlayMaterials.Select(FretboardStyles.InlayFile)))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://TabForge/Assets/Fretboards/{name}"));
            using var original = new Bitmap(stream);
            var loaded = Assert.IsType<AvaloniaImage>(assets.Get(name));
            var finished = Assert.IsAssignableFrom<Bitmap>(loaded.Source);
            Assert.Equal(original.PixelSize, finished.PixelSize);
            Assert.Equal(original.Dpi, finished.Dpi);
            var expected = Pixels(original);
            if (name.StartsWith("fb_wood_", StringComparison.Ordinal)) FretboardWoodFinish.Calm(expected);
            Assert.Equal(expected, Pixels(finished));
            Assert.Same(loaded, assets.Get(name));
        }
    }

    private static byte[] Pixels(Bitmap source)
    {
        using var readback = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var locked = readback.Lock();
        source.CopyPixels(locked, AlphaFormat.Unpremul);
        var width = source.PixelSize.Width * 4;
        var pixels = new byte[width * source.PixelSize.Height];
        for (var y = 0; y < source.PixelSize.Height; y++)
            Marshal.Copy(locked.Address + y * locked.RowBytes, pixels, y * width, width);
        return pixels;
    }

    [Theory]
    [InlineData(12, 9)]
    [InlineData(45, 28)]
    [InlineData(120, 75)]
    [InlineData(220, 30)]
    public void FinishDarkensTheMeanAndGentlyReducesGrainWithoutMovingEdges(int mean, int grain)
    {
        // Dark composite/ebony through pale maple; a sharp alternating grain stays sharp and alternating.
        var original = Enumerable.Range(0, 8).SelectMany(i => new[] { (byte)(mean + (i % 2 == 0 ? -grain : grain)), (byte)mean, (byte)mean, (byte)255 }).ToArray();
        var pixels = (byte[])original.Clone();
        FretboardWoodFinish.Calm(pixels);
        Assert.Equal(original.Length, pixels.Length);
        Assert.InRange((pixels[0] + pixels[4]) / 2.0, mean * 0.94 - 0.5, mean * 0.94 + 0.5);
        Assert.InRange(pixels[4] - pixels[0], grain * 2 * 0.94 * 0.96 - 1, grain * 2 * 0.94 * 0.96 + 1);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.Equal(pixels[i % 8], pixels[i]);
            Assert.Equal(255, pixels[i + 3]);
            Assert.Equal((byte)Math.Round(mean * 0.94), pixels[i + 1]);
            Assert.Equal(pixels[i + 1], pixels[i + 2]);
        }
    }

    [Fact]
    public void FlatWoodKeepsItsColourBalanceAndOnlyDarkens()
    {
        byte[] pixels = [40, 80, 160, 255, 40, 80, 160, 255];
        FretboardWoodFinish.Calm(pixels);
        Assert.Equal(new byte[] { 38, 75, 150, 255, 38, 75, 150, 255 }, pixels);
        FretboardWoodFinish.Calm([]);
    }
}
