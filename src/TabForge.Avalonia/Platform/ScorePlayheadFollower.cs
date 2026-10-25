using System;
using Avalonia;
using Avalonia.Controls;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: scrolling the score viewer so the playhead line stays in view: the Jump and Smooth follow of one viewer, the glide goal and the
//     frame gate that paces a Smooth glide.
// Does not own: the playhead position (ScorePlayState, the playhead overlay), the follow settings (the score view's Follow property), the
//     viewer and its geometry (passed in per call), the decision of when to follow (the score view).
// Tests: TabForge.Avalonia.Tests.ScoreFollowContinuousPlayTests.
// Threads: UI thread only.

/// <summary>Scrolls a score viewer to keep the playhead line in view, by the follow mode (Jump sets the offset at once, Smooth glides toward it).</summary>
internal sealed class ScorePlayheadFollower
{
    /// <summary>Share of the remaining distance a Smooth frame covers.</summary>
    private const double GlideShare = 0.25;

    /// <summary>A Smooth glide stops within this distance of its goal.</summary>
    private const double GlideArrivalPixels = 0.5;

    private readonly FollowFrameGate _frameGate = new();
    private FollowOffset? _glideTarget;
    private double _glideForTop;   // the line top (view pixels) the glide goal was worked out for

    /// <summary>
    /// Follows the playhead line <paramref name="pageLine"/> (page coordinates: x, top and bottom) in <paramref name="viewer"/>. The line and the
    /// bar width are page units, scaled by <paramref name="zoom"/>; <paramref name="leftMargin"/> is the page's left edge inside the viewer.
    /// </summary>
    internal void Follow(FollowSettings settings, ScrollViewer viewer, double leftMargin, double zoom,
        (double X, double Top, double Bottom) pageLine, double pageBarWidth)
    {
        var view = new FollowViewport(viewer.Offset.X, viewer.Offset.Y, viewer.Viewport.Width, viewer.Viewport.Height);
        var line = new FollowLine(leftMargin + pageLine.X * zoom, pageLine.Top * zoom, pageLine.Bottom * zoom);
        var content = new FollowExtent(viewer.Extent.Width, viewer.Extent.Height);
        var barWidth = pageBarWidth * zoom;
        var target = FollowScrollPolicy.Target(settings, view, line, barWidth, content);
        if (target is { } wanted) _glideTarget = wanted;
        if (_glideTarget is not { } goal) return;
        // A line that already left the view cannot wait for a glide: it jumps, so the playing system is never off screen for a frame.
        var outside = line.Top < view.Y || line.Top >= view.Bottom || line.X < view.X || line.X >= view.Right;
        if (settings.Mode == FollowModes.Jump || outside)
        {
            ScorePreviewView.EnsurePageOrigin(viewer, new Vector(goal.X, goal.Y));
            _glideTarget = null;
            return;
        }
        // A goal worked out for another line position (the page reflowed, or the song jumped) is stale: drop it and let the next frame work out a new one.
        var lineTop = pageLine.Top * zoom;
        if (target is null && Math.Abs(lineTop - _glideForTop) > GlideArrivalPixels) { _glideTarget = null; return; }
        if (target is not null) _glideForTop = lineTop;
        if (!_frameGate.Allow(settings.MaxFps)) return;
        var next = new Vector(viewer.Offset.X + (goal.X - viewer.Offset.X) * GlideShare, viewer.Offset.Y + (goal.Y - viewer.Offset.Y) * GlideShare);
        ScorePreviewView.EnsurePageOrigin(viewer, next);
        if (Math.Abs(goal.X - next.X) < GlideArrivalPixels && Math.Abs(goal.Y - next.Y) < GlideArrivalPixels) _glideTarget = null;
    }
}
