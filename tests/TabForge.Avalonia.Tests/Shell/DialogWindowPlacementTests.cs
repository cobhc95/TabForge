using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class DialogWindowPlacementTests
{
    private sealed class RememberedDialog : Window { }
    private sealed class FreshDialog : Window { }
    private sealed class OtherDialog : Window { }

    [AvaloniaFact]
    public void FirstOpenCentresOnLiveOwnerBoundsAndReopenRemembersPosition()
    {
        using var owner = new TestWindow();
        owner.Window.Width = 1000;
        owner.Window.Height = 800;
        owner.Window.Show();
        owner.Window.Position = new PixelPoint(40, 50);
        var first = new RememberedDialog { Width = 200, Height = 100 };
        first.Show(owner.Window);
        var screen = first.Screens.ScreenFromWindow(owner.Window)!;
        var size = PixelSize.FromSize(first.FrameSize ?? first.ClientSize, screen.Scaling);
        var expected = DialogWindowPlacement.Clamp(DialogWindowPlacement.Centre(
            new PixelRect(owner.Window.Position, PixelSize.FromSize(owner.Window.ClientSize, owner.Window.DesktopScaling)), size), size, screen.WorkingArea);
        Assert.Equal(expected, first.Position);
        var moved = new PixelPoint(120, 140);
        first.Position = moved;
        first.Close();
        var reopened = new RememberedDialog { Width = 200, Height = 100 };
        try
        {
            reopened.Show(owner.Window);
            Assert.Equal(WindowStartupLocation.Manual, reopened.WindowStartupLocation);
            Assert.Equal(DialogWindowPlacement.Clamp(moved, size, screen.WorkingArea), reopened.Position);
        }
        finally { reopened.Close(); }
    }

    [AvaloniaFact]
    public void NewSessionHasNoRememberedPositionAndTypesAreIndependent()
    {
        var session = new DialogWindowPlacement();
        var first = new FreshDialog { Width = 200, Height = 100 };
        session.Prepare(first);
        first.Show();
        first.Position = new PixelPoint(120, 140);
        first.Close();
        var sameSession = new FreshDialog();
        var nextSession = new FreshDialog();
        var differentType = new OtherDialog();
        try
        {
            session.Prepare(sameSession);
            Assert.Equal(WindowStartupLocation.Manual, sameSession.WindowStartupLocation);
            Assert.Equal(new PixelPoint(120, 140), sameSession.Position);
            new DialogWindowPlacement().Prepare(nextSession);
            Assert.Equal(WindowStartupLocation.CenterOwner, nextSession.WindowStartupLocation);
            session.Prepare(differentType);
            Assert.Equal(WindowStartupLocation.CenterOwner, differentType.WindowStartupLocation);
        }
        finally { sameSession.Close(); nextSession.Close(); differentType.Close(); }
    }

    [AvaloniaFact]
    public void FretboardSettingsDefaultsToCentreOwner()
    {
        var window = new FretboardSettingsWindow(new AppSettings(), () => { }, () => { });
        try
        {
            window.Show();
            Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(-3000, -1000, -1920, 0)]
    [InlineData(500, 2000, -400, 700)]
    public void RememberedPositionIsClampedOnNegativeCoordinateMonitor(int x, int y, int expectedX, int expectedY)
    {
        Assert.Equal(new PixelPoint(expectedX, expectedY), DialogWindowPlacement.Clamp(
            new PixelPoint(x, y), new PixelSize(400, 300), new PixelRect(-1920, 0, 1920, 1000)));
    }

    [Fact]
    public void OversizedDialogKeepsItsTitleBarReachable() => Assert.Equal(new PixelPoint(10, 20),
        DialogWindowPlacement.Clamp(new PixelPoint(500, 600), new PixelSize(2000, 1500), new PixelRect(10, 20, 800, 600)));

    private sealed class TestWindow : System.IDisposable
    {
        internal Window Window { get; } = new();
        public void Dispose() => Window.Close();
    }
}
