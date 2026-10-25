using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TabForge.Avalonia.Capture;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the capture picture checks: render scale, crop clipping, the PNG file, the RGBA bytes and the blank-picture measure.
/// Does not own: which control a shot renders (CaptureSessionTests).
/// Tests: this class.
/// </summary>
public class CaptureImageTests
{
    private static Control TwoColours()
    {
        var panel = new Grid { Width = 40, Height = 20, ColumnDefinitions = new ColumnDefinitions("*,*") };
        panel.Children.Add(new Border { Background = Brushes.Red });
        var right = new Border { Background = Brushes.Blue };
        Grid.SetColumn(right, 1);
        panel.Children.Add(right);
        var host = new Window { Content = panel, Width = 40, Height = 20 };
        host.Show();
        return panel;
    }

    [AvaloniaFact]
    public void RendersAtTheGivenScaleAndCropsToTheRegion()
    {
        var image = CaptureImage.Render(TwoColours(), 2);
        Assert.Equal(80, image.Width);
        Assert.Equal(40, image.Height);
        var crop = image.Crop(60, 0, 100, 100);
        Assert.Equal(20, crop.Width);
        Assert.Equal(40, crop.Height);
        var rgba = crop.ToRgba();
        Assert.True(rgba[2] > 200 && rgba[0] < 50, "the right half is blue");
        Assert.Equal(255, rgba[3]);
    }

    [AvaloniaFact]
    public void WritesAPngAndMeasuresInk()
    {
        var image = CaptureImage.Render(TwoColours(), 1);
        var path = Path.Combine(DialogTestKit.RenderDirectory(nameof(CaptureImageTests)), "two.png");
        image.SavePng(path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(path)[..4]);
        Assert.InRange(image.InkShare(), 0.4, 0.6);
        Assert.Equal(0, image.Crop(0, 0, 10, 10).InkShare());
    }
}
