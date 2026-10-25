using TabForge.Platform;
using TabForge.Shell;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the middle-click tracker (press and release within 8 px fires once).</summary>
public class MiddleClickTrackerTests
{
    [Fact]
    public void ReleaseNearPressFires()
    {
        var t = new MiddleClickTracker();
        t.Press(new UiPoint(100, 20));
        Assert.True(t.Release(new UiPoint(105, 20))); // E3
    }

    [Fact]
    public void ReleaseFarFromPressDoesNothing()
    {
        var t = new MiddleClickTracker();
        t.Press(new UiPoint(100, 20));
        Assert.False(t.Release(new UiPoint(130, 20))); // E3
    }

    [Fact]
    public void ReleaseWithoutPressDoesNothing() => Assert.False(new MiddleClickTracker().Release(new UiPoint(1, 1)));

    [Fact]
    public void PressIsConsumedByItsRelease()
    {
        var t = new MiddleClickTracker();
        t.Press(new UiPoint(10, 10));
        Assert.True(t.Release(new UiPoint(10, 10)));
        Assert.False(t.Release(new UiPoint(10, 10)));
    }

    [Fact]
    public void TrackersAreIndependent()
    {
        var a = new MiddleClickTracker();
        var b = new MiddleClickTracker();
        a.Press(new UiPoint(10, 10));
        Assert.False(b.Release(new UiPoint(10, 10)));
        Assert.True(a.Release(new UiPoint(10, 10)));
    }
}
