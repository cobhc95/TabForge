using System;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the snap targets of an audio clip drag on the arrangement, in pixels: the grid line of the bar under the pointer (the grid setting
//       sets the step), the start and end of the other clips (not the dragged pass's own take group, whose passes share its times), and the
//       playhead. The nearest target within the snap distance wins (the grid at any distance when that setting is on). With snapping off, or
//       Alt held, nothing snaps.
// Does not own: the drag (TimelineClipGestureController), the snap settings (TransportBarView holds them; the timeline reads them), the snap
//       window and the drawing.
// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests.
internal static class TimelineClipSnap
{
    /// <summary>
    /// The x a clip edge at <paramref name="x"/> snaps to. <paramref name="distancePx"/> is how far it moves, or infinity when no target is in reach
    /// (then the result is <paramref name="x"/>). <paramref name="except"/> is the dragged clip, which never snaps to itself.
    /// </summary>
    public static double SnapX(TimelineView view, double x, AudioClip? except, bool altHeld, out double distancePx)
    {
        distancePx = double.PositiveInfinity;
        var snap = view.Snap;
        if (snap is not { Enabled: true } || altHeld || view.BarCount == 0) return x;
        var best = x;
        var bestDistance = double.PositiveInfinity;
        void Consider(double candidate, double limitPx) =>
            ClipGesturePresenter.ConsiderSnap(ref best, ref bestDistance, candidate, Math.Abs(candidate - x), limitPx);
        if (snap.ToItems)
            foreach (var track in view.Song.Tracks)
                foreach (var other in track.AudioClips)
                {
                    // The other passes of the dragged pass's own take group share its start and end: snapping to them
                    // would pin a pass that should move freely.
                    if (ReferenceEquals(other, except) ||
                        (except?.TakeGroupId is { Length: > 0 } group && other.TakeGroupId == group)) continue;
                    Consider(view.XOfSeconds(other.StartSec), snap.DistancePx);
                    Consider(view.XOfSeconds(other.EndSec), snap.DistancePx);
                }
        if (snap.ToPlayhead && view.PlayheadBar >= 0 && view.PlayheadBar < view.BarCount)
            Consider(BarFractionX(view, view.PlayheadBar, view.PlayheadFraction), snap.DistancePx);
        if (snap.ToGrid) Consider(GridX(view, x, snap.Grid), snap.GridAtAnyDistance ? double.PositiveInfinity : snap.DistancePx);
        distancePx = bestDistance;
        return best;
    }

    /// <summary>The x of a position given as a bar and its fraction of that bar.</summary>
    private static double BarFractionX(TimelineView view, int bar, double fraction)
    {
        var left = view.BarX(bar);
        return left + (view.BarX(bar + 1) - left) * fraction;
    }

    /// <summary>The grid line of the bar under <paramref name="x"/> that is nearest to it; the grid setting gives the step in beat slots.</summary>
    private static double GridX(TimelineView view, double x, string grid)
    {
        var bar = Math.Clamp(view.BarAtX(x), 0, view.BarCount - 1);
        var slots = Math.Max(1, MusicTime.BarSlots(view.Song, bar));
        var left = view.BarX(bar);
        var width = view.BarX(bar + 1) - left;
        if (width <= 0) return x;
        // Past the last bar (a clip dragged out over the ghost bars) the grid goes on in bars as wide as the last one, so it never pulls the clip back to the end.
        // When the song cannot extend (it ends in a jump such as D.S. or Fine) there is no such grid: the end is the only line.
        var end = view.BarX(view.BarCount);
        if (x > end)
        {
            if (!view.ContinuesPastEnd) return end;
            left = end + Math.Floor((x - end) / width) * width;
        }
        var step = GridStepSlots(grid, slots);
        var snapped = Math.Round((x - left) / width * slots / step) * step;
        return left + snapped / slots * width;
    }

    /// <summary>The grid step in slots of a bar: "Bar" is the whole bar, the others are note values (a quarter note is 4 slots).</summary>
    private static double GridStepSlots(string grid, int slots) => grid switch
    {
        "Bar" => slots,
        "1/2" => 8.0,
        "1/4" => 4.0,
        "1/8" => 2.0,
        "1/16" => 1.0,
        "1/32" => 0.5,
        _ => 4.0,
    };
}
