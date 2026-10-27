// Owns: the one window-size entry point: the named kinds, the default and minimum sizes at 0.8, 1.0 and 1.5 scale,
//   the working-area clamp to 1920 by 1080 and 800 by 600, the mostly-on-screen check and the CenterOwner location.
// Does not own: the size maths (WindowUiScale) or the placement check (MainWindowPlacement).
// Tests: WindowMetricsTests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class WindowMetricsTests
{
    private static readonly Size FullArea = new Size(1920, 1080);
    private static readonly Size SmallArea = new Size(800, 600);

    [Fact]
    public void KindsReadTheirOwners()
    {
        Assert.Equal(1040, WindowKind.Settings.Width);
        Assert.Equal(690, WindowKind.Settings.Height);
        Assert.Equal(834, WindowKind.Settings.MinWidth);
        Assert.Equal(597, WindowKind.Settings.MinHeight);
        Assert.Equal(660, WindowKind.SongInfo.Width);
        Assert.Equal(600, WindowKind.SongInfo.Height);
        Assert.Equal(420, WindowKind.SmallDialog.Width);
        Assert.Equal(420, WindowKind.SmallDialog.MinWidth);
    }

    [Fact]
    public void DefaultAtScaleOneIsThePreferredSize()
    {
        var size = WindowMetrics.Default(FullArea, WindowKind.Settings, 1.0);
        Assert.Equal(1040, size.Width, 3);
        Assert.Equal(690, size.Height, 3);
    }

    [Fact]
    public void DefaultScalesAndCapsToTheWorkingArea()
    {
        var full = WindowMetrics.Default(FullArea, WindowKind.Settings, 1.5);
        Assert.Equal(1248, full.Width, 3);
        Assert.Equal(918, full.Height, 3);
        var small = WindowMetrics.Default(SmallArea, WindowKind.Settings, 1.0);
        Assert.Equal(520, small.Width, 3);
        Assert.Equal(510, small.Height, 3);
        var clamped = WindowMetrics.Default(FullArea, WindowKind.Settings, 9.9);
        Assert.Equal(full.Width, clamped.Width, 6);
        Assert.Equal(full.Height, clamped.Height, 6);
    }

    [Fact]
    public void MinWinsOnlyInsideTheWorkingArea()
    {
        var fit = WindowMetrics.Min(FullArea, WindowKind.Settings, 1.0);
        Assert.Equal(1040, fit.Width, 3);
        Assert.Equal(690, fit.Height, 3);
        var small = WindowMetrics.Min(SmallArea, WindowKind.Settings, 1.0);
        Assert.Equal(800, small.Width, 3);
        Assert.Equal(600, small.Height, 3);
    }

    [Fact]
    public void ClampNeverExceedsTheWorkingArea()
    {
        Assert.Equal(FullArea, WindowMetrics.Clamp(new Size(3000, 2000), FullArea));
        Assert.Equal(SmallArea, WindowMetrics.Clamp(new Size(3000, 2000), SmallArea));
        Assert.Equal(new Size(100, 100), WindowMetrics.Clamp(new Size(100, 100), SmallArea));
    }

    [AvaloniaFact]
    public void WindowOverloadsDelegateToWindowUiScale()
    {
        var window = new Window();
        try
        {
            Assert.Equal(
                WindowUiScale.DefaultSize(window, 1.0, 1040, 690),
                WindowMetrics.Default(window, WindowKind.Settings, 1.0));
            Assert.Equal(
                WindowUiScale.ClampRememberedSize(new Size(1040, 690), new Size(834, 597), window),
                WindowMetrics.Min(window, WindowKind.Settings, 1.0));
            Assert.Equal(
                WindowUiScale.ClampToWorkingArea(WindowUiScale.ToOuter(new Size(100, 100), 1.0), window),
                WindowMetrics.Clamp(window, new Size(100, 100), 1.0));
            Assert.Equal(WindowUiScale.WorkingArea(window), WindowMetrics.WorkingArea(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MostlyOnScreenFollowsTheWindowAndOrigin()
    {
        var window = new Window { Width = 400, Height = 300 };
        try
        {
            var area = window.Screens.Primary!.WorkingArea;
            Assert.True(WindowMetrics.IsMostlyOnScreen(window, new PixelPoint(area.X + 10, area.Y + 10)));
            Assert.False(WindowMetrics.IsMostlyOnScreen(window, new PixelPoint(area.X + 100000, area.Y + 100000)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CenterOwnerSetsTheStartupLocation()
    {
        var window = new Window();
        var owner = new Window();
        try
        {
            WindowMetrics.CenterOwner(window);   // the owner itself is set by ShowDialog(owner)
            Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }
}
