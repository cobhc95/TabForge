namespace TabForge.Views.Score;

// Owns: the scroll offsets that keep the cursor in view after a cursor move (a key, an edit, a menu or a Go To): vertically, a system with less
//   than a quarter of its height showing is brought to the usual margin (a system taller than the pane keeps its lower part, the TAB, in view);
//   sideways, the one-line view scrolls when the cursor's bar leaves the viewport or starts in its last OneLineEdgeMargin.
// Does not own: the cursor (ScorePreviewEditor), the viewer and its offset (Avalonia ScorePreviewView), the playhead follow (FollowScrollPolicy).
// Tests: TabForge.Avalonia.Tests.ScoreCursorSightTests.

public static class ScoreCursorSight
{
    /// <summary>The share of the cursor's system that must show; below it the view scrolls.</summary>
    public const double MinimumVisibleShare = 0.25;

    /// <summary>In the one-line view a cursor bar starting this far (DIPs) from the viewport's right edge counts as out of view.</summary>
    public const double OneLineEdgeMargin = 120;

    /// <summary>
    /// The vertical offset that keeps the system (page top and height) in the viewport (its top and height), or null when it is shown enough.
    /// <paramref name="marginPercent"/> is the share of the viewport above the system the usual target leaves.
    /// </summary>
    public static double? VerticalOffset(double top, double height, double viewportTop, double viewportHeight, int marginPercent)
    {
        if (viewportHeight <= 1) return null;
        if (height > viewportHeight) return Math.Abs(viewportTop - (top + height - viewportHeight)) > 2 ? top + height - viewportHeight : null;
        var shown = Math.Min(top + height, viewportTop + viewportHeight) - Math.Max(top, viewportTop);
        if (shown >= height * MinimumVisibleShare) return null;
        return Math.Max(0, top - viewportHeight * marginPercent / 100.0);
    }

    /// <summary>The horizontal offset that brings the cursor bar starting at page x into the one-line view, or null when it is in view.</summary>
    public static double? HorizontalOffset(double x, double viewportLeft, double viewportWidth) =>
        x < viewportLeft || x > viewportLeft + viewportWidth - OneLineEdgeMargin ? Math.Max(0, x) : null;
}
