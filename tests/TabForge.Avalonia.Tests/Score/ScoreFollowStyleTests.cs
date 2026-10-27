using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the follow style of the score's playhead follow (SC-10): a page turn waits for the trigger, while continuous scroll keeps the played line
//   at its margin position as it moves.
// Does not own: the scroll itself (ScorePlayheadFollower), the settings (FollowSettings), the page-turn triggers (FollowScrollPolicy).
// Tests: ScoreFollowStyleTests (this file).
public class ScoreFollowStyleTests
{
    [Fact]
    public void ContinuousScrollMovesWithThePlayedLineWhileAPageTurnWaitsForItsTrigger()
    {
        var follow = new FollowSettings { Mode = FollowModes.Smooth, ContinuousScroll = false };
        var view = new FollowViewport(0, 0, 900, 600);
        var line = new FollowLine(X: 300, Top: 250, Bottom: 330);   // well above the trigger (80% of the viewport): a page turn holds
        var extent = new FollowExtent(5000, 5000);

        Assert.Null(FollowScrollPolicy.Target(follow, view, line, 100, extent));

        follow.ContinuousScroll = true;
        var moved = FollowScrollPolicy.Target(follow, view, line, 100, extent);
        Assert.Equal(new FollowOffset(X: 300 - 900 * FollowScrollPolicy.HorizontalParkShare, Y: 250 - 600 * follow.MarginPercent / 100.0), moved);
    }
}
