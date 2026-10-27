using System;
using Avalonia;
using TabForge.Models;

namespace TabForge.Avalonia.Platform;

// Owns: the performed time a press on a clip grabs. The timeline draws every pass of a repeat on the same written bars, so an x alone does not say which
//   pass is meant; the clip drag reads the pointer in the pass it grabbed, and the clip then moves by the exact performed-time delta.
// Does not own: the drag (TimelineClipGestureController), the fold (RepeatFold) or the slice boxes (TimelineAudioClips.Fold.cs).
// Tests: TabForge.Avalonia.Tests.ClipDragRepeatTests.
internal static partial class TimelineAudioClips
{
    /// <summary>
    /// The song time (performed clock) of <paramref name="clip"/> under <paramref name="point"/>: the point read in the pass of the slice (or box) it is in,
    /// not the first pass of the bar. A point on no slice takes the pass of the clip start.
    /// </summary>
    internal static double GrabSeconds(TimelineView view, TrackModel track, AudioClip clip, Point point)
    {
        var laneTop = LaneTopOf(view, view.Song.Tracks.IndexOf(track)) + Math.Max(0, clip.Lane) * ClipLanes.AudioLaneHeight;
        var (from, to, rect) = (clip.StartSec, clip.EndSec, BoundsOf(view, track, clip, laneTop, ClipLanes.AudioLaneHeight));
        if (FoldOf(view, clip) is { } slices)
        {
            var regions = TimelineRepeatPicker.RegionsOfFold(view, slices);
            var found = false;
            foreach (var slice in slices)
            {
                var box = SliceRect(view, slice, laneTop, IsManyPass(slice));
                if (!box.Contains(point)) continue;
                // A many-pass repeat draws only its shown pass: a press grabs that one.
                if (IsManyPass(slice) && TimelineRepeatPicker.RegionOfSlice(view, regions, slice) is { } region && slice.PartIndex != view.RepeatShown.ShownPass(region) - 1) continue;
                (from, to, rect, found) = (slice.StartSec, slice.EndSec, box, true);
                break;
            }
            if (!found) return view.SecondsOfXNear(point.X, clip.StartSec);
        }
        var fraction = rect.Width > 0 ? Math.Clamp((point.X - rect.X) / rect.Width, 0, 1) : 0;
        return view.SecondsOfXNear(point.X, from + fraction * (to - from));
    }
}
