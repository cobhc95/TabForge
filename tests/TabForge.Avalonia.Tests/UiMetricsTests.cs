// Owns: the one popup and density-size entry point: bar, row and pad at Comfortable, Compact and Spacious, the popup
//   clamp numbers to 1920 by 1080 and 800 by 600, and the popup wiring keeping offsets at zero when the popup fits.
// Does not own: the density factors (UiDensity) or any view layout.
// Tests: UiMetricsTests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class UiMetricsTests
{
    private static readonly Size FullArea = new Size(1920, 1080);
    private static readonly Size SmallArea = new Size(800, 600);

    [Fact]
    public void ComfortableReturnsTheBase()
    {
        var comfortable = new AppearanceSettings { Density = "Comfortable" };
        Assert.Equal(30, UiMetrics.Bar(30, comfortable));
        Assert.Equal(34, UiMetrics.Row(34, comfortable));
        Assert.Equal(new Thickness(12), UiMetrics.Pad(12, comfortable));
        Assert.Equal(new Thickness(5, 3, 5, 3), UiMetrics.Pad(5, 3, comfortable));
    }

    [Fact]
    public void CompactAndSpaciousScaleTheBase()
    {
        var compact = new AppearanceSettings { Density = "Compact" };
        Assert.Equal(25.5, UiMetrics.Bar(30, compact), 3);
        Assert.Equal(28.9, UiMetrics.Row(34, compact), 3);
        Assert.Equal(10.2, UiMetrics.Pad(12, compact).Left, 6);
        var asymmetric = UiMetrics.Pad(5, 3, compact);
        Assert.Equal(5, asymmetric.Left);
        Assert.Equal(2.55, asymmetric.Top, 6);
        var spacious = new AppearanceSettings { Density = "Spacious" };
        Assert.Equal(36, UiMetrics.Bar(30, spacious), 3);
        Assert.Equal(40.8, UiMetrics.Row(34, spacious), 3);
        Assert.Equal(14.4, UiMetrics.Pad(12, spacious).Left, 6);
    }

    [Fact]
    public void ClampPopupKeepsTheRectInsideTheWorkingArea()
    {
        Assert.Equal(new PixelPoint(100, 100), UiMetrics.ClampPopup(new PixelPoint(100, 100), new Size(200, 100), FullArea));
        Assert.Equal(new PixelPoint(0, 0), UiMetrics.ClampPopup(new PixelPoint(-50, -40), new Size(200, 100), FullArea));
        Assert.Equal(new PixelPoint(1720, 980), UiMetrics.ClampPopup(new PixelPoint(1900, 1050), new Size(200, 100), FullArea));
        Assert.Equal(new PixelPoint(0, 0), UiMetrics.ClampPopup(new PixelPoint(100, 100), new Size(3000, 2000), FullArea));
        Assert.Equal(new PixelPoint(600, 500), UiMetrics.ClampPopup(new PixelPoint(900, 700), new Size(200, 100), SmallArea));
        Assert.Equal(new PixelPoint(0, 0), UiMetrics.ClampPopup(new PixelPoint(0, 0), new Size(800, 600), SmallArea));
    }

}
