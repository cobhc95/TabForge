using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the rule for where the score scrolls while playback runs: which axes follow (FollowSettings), the vertical trigger and parking
//   position, the look-ahead bars and the stop at the end. Pure maths on page coordinates.
// Does not own: the scroll viewer, the clock and the Smooth glide (Avalonia ScorePreviewView), the playhead geometry (ScorePlayState),
//   the settings store.
// Tests: TabForge.Avalonia.Tests.FollowSettingsTests.

/// <summary>The visible part of the score, in the same units as the playhead line.</summary>
public readonly record struct FollowViewport(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>The played line: its x and its top and bottom edges.</summary>
public readonly record struct FollowLine(double X, double Top, double Bottom);

/// <summary>The full size of the scrollable score.</summary>
public readonly record struct FollowExtent(double Width, double Height);

/// <summary>A scroll position the view should move to.</summary>
public readonly record struct FollowOffset(double X, double Y);

public static class FollowScrollPolicy
{
    /// <summary>Horizontal parking: a jump puts the played line this far from the viewport's left edge (share of its width).</summary>
    public const double HorizontalParkShare = 0.2;

    /// <summary>
    /// The offset the score should move to so the played line is in view, or null when it stays. The switch off never moves. A horizontal jump happens
    /// when the line or the look-ahead bars leave the viewport; a vertical one when the system passes the trigger position, and it parks the
    /// system at the margin, and a system whose bottom is below the viewport always moves. Stop at end keeps still once the end of that axis is visible.
    /// Continuous scroll (the smooth page turn) does not wait for the triggers: see <see cref="ContinuousTarget"/>.
    /// </summary>
    public static FollowOffset? Target(FollowSettings follow, FollowViewport view, FollowLine line, double barWidth, FollowExtent content)
    {
        if (!follow.FollowPlayback) return null;
        if (follow.ContinuousScroll) return ContinuousTarget(follow, view, line, content);
        var x = view.X;
        var y = view.Y;
        var horizontalEndVisible = follow.StopAtEnd && view.Right >= content.Width;
        // Stop at end only holds the score back from scrolling forward: a line that is behind the view (a seek, a loop, a reflow that moved it) is always brought back.
        if (follow.HorizontalFollow)
        {
            var lookAheadEdge = line.X + follow.AnticipationBars * barWidth;
            if (line.X < view.X || (!horizontalEndVisible && lookAheadEdge > view.Right)) x = Math.Max(0, line.X - view.Width * HorizontalParkShare);
        }
        var verticalEndVisible = follow.StopAtEnd && view.Bottom >= content.Height;
        if (follow.VerticalFollow)
        {
            var trigger = view.Y + view.Height * follow.VerticalTriggerPercent / 100.0;
            // A system whose bottom is below the viewport always scrolls, even at the end (its bottom must not stay cut off); the trigger governs the rest.
            if (line.Top < view.Y || line.Bottom > view.Bottom || (!verticalEndVisible && line.Bottom > trigger)) y = Math.Max(0, line.Top - view.Height * follow.MarginPercent / 100.0);
        }
        return x == view.X && y == view.Y ? null : new FollowOffset(x, y);
    }

    /// <summary>
    /// Continuous scroll: the played line is kept at its margin position without waiting for a trigger. Horizontally the view follows the line
    /// as it moves along the system; vertically it moves to the system at the margin as soon as the system changes. Stop at end holds the view
    /// once the end of an axis is visible, and a line that is off the view is always brought back.
    /// </summary>
    private static FollowOffset? ContinuousTarget(FollowSettings follow, FollowViewport view, FollowLine line, FollowExtent content)
    {
        var x = view.X;
        var y = view.Y;
        var horizontalEndVisible = follow.StopAtEnd && view.Right >= content.Width;
        if (follow.HorizontalFollow && (line.X < view.X || line.X > view.Right || !horizontalEndVisible))
            x = Math.Max(0, line.X - view.Width * HorizontalParkShare);
        var verticalEndVisible = follow.StopAtEnd && view.Bottom >= content.Height;
        if (follow.VerticalFollow && (line.Top < view.Y || line.Bottom > view.Bottom || !verticalEndVisible))
            y = Math.Max(0, line.Top - view.Height * follow.MarginPercent / 100.0);
        return x == view.X && y == view.Y ? null : new FollowOffset(x, y);
    }
}
