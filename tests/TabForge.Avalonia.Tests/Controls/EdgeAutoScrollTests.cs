using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the drag-select edge scroll's rules: no scroll inside the view (its edges included), a step that grows with the distance the pointer is
///   past the top or bottom and stops growing at the cap, the clamp that moves nothing at the content end, and the hold on the playhead
///   follow: on from the first move past an edge, off when the pointer is back inside or the drag ends.
/// Does not own: the timer or the views that call it (ScorePreviewInput, TimelineInputController.Box, PianoRollSurface.Select).
/// Tests: this class.
/// </summary>
public class EdgeAutoScrollTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    [InlineData(400)]
    public void APointerInsideTheViewDoesNotScroll(double pointer) => Assert.Equal(0, EdgeAutoScroll.Step(pointer, 0, 400));

    [Fact]
    public void TheStepGrowsWithTheDistancePastTheEdgeUpToTheCap()
    {
        var near = EdgeAutoScroll.Step(410, 0, 400);
        var far = EdgeAutoScroll.Step(480, 0, 400);
        Assert.True(near > 0 && far > near, $"{near} then {far}");
        Assert.Equal(EdgeAutoScroll.MinStep + 10 * EdgeAutoScroll.StepPerDip, near, 6);
        Assert.Equal(EdgeAutoScroll.MaxStep, EdgeAutoScroll.Step(100_000, 0, 400));
    }

    [Fact]
    public void AboveTheTopScrollsUpAsFastAsBelowTheBottomScrollsDown()
    {
        Assert.Equal(-EdgeAutoScroll.Step(430, 0, 400), EdgeAutoScroll.Step(-30, 0, 400));
        Assert.True(EdgeAutoScroll.Step(-30, 0, 400) < 0);
    }

    [Fact]
    public void AnEmptyViewOrAnUnknownPointerDoesNotScroll()
    {
        Assert.Equal(0, EdgeAutoScroll.Step(500, 0, 0));
        Assert.Equal(0, EdgeAutoScroll.Step(double.NaN, 0, 400));
    }

    [AvaloniaFact]
    public void ThePlayheadFollowWaitsOnlyWhileTheDragIsPastAnEdge()
    {
        var scroll = new EdgeAutoScroll(_ => 0, _ => { });
        Assert.False(scroll.HoldsFollow);
        scroll.Update(200, 0, 400);   // inside: the follow goes on as before
        Assert.False(scroll.HoldsFollow);
        scroll.Update(450, 0, 400);   // past the bottom
        Assert.True(scroll.HoldsFollow);
        scroll.Update(390, 0, 400);   // back inside
        Assert.False(scroll.HoldsFollow);
        scroll.Update(-20, 0, 400);   // past the top
        Assert.True(scroll.HoldsFollow);
        scroll.Stop();                // release, Escape or a lost capture
        Assert.False(scroll.HoldsFollow);
        Assert.False(scroll.IsScrolling);
    }

    [Theory]
    [InlineData(100, 10, 500, 10)]     // room left: the whole step
    [InlineData(495, 10, 500, 5)]      // near the end: what is left
    [InlineData(500, 10, 500, 0)]      // at the content end: nothing
    [InlineData(4, -10, 500, -4)]      // near the top: what is left
    [InlineData(0, -10, 500, 0)]       // at the top: nothing
    [InlineData(0, 10, -20, 0)]        // content shorter than the view: nothing
    public void TheClampStopsAtTheContentEnds(double offset, double step, double max, double expected) =>
        Assert.Equal(expected, EdgeAutoScroll.Clamped(offset, step, max), 6);
}
