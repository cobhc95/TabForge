using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the pure clip edits the timeline commands share: the beat length for a tempo, moving a clip by seconds, moving it one lane up or down
//   and placing a copy on the wanted lane (or the first free one), adding the lane rows they need.
// Does not own: the undo step, the selection, the redraw or the command table (MainWindowClipKeys), the lane rules themselves (ClipLanes in Core)
//   or the clip files and the clipboard (ClipClipboard in Core).
// Tests: TabForge.Avalonia.Tests.ClipEditServiceTests, RepeatTakeLandingTests.
/// <summary>The pure edits behind the clip commands; no document, undo or view state.</summary>
public static class ClipEditService
{
    /// <summary>The lowest tempo (beats per minute) a beat is measured at; the WPF fallback when a bar has no length to read.</summary>
    public const double MinimumTempo = 20;

    /// <summary>One beat in seconds at <paramref name="tempo"/>, never faster than <see cref="MinimumTempo"/>.</summary>
    public static double BeatSeconds(double tempo) => 60.0 / Math.Max(MinimumTempo, tempo);

    /// <summary>Moves the clip's start by <paramref name="deltaSec"/>, never before the song start; false when it does not move.</summary>
    public static bool Move(AudioClip clip, double deltaSec)
    {
        var start = Math.Max(0, clip.StartSec + deltaSec);
        if (start == clip.StartSec) return false;
        clip.StartSec = start;
        return true;
    }

    /// <summary>Moves the clip (always alone: a clip over a repeat is one piece) by <paramref name="deltaSec"/>; false when it does not move.</summary>
    public static bool Move(TrackModel track, AudioClip clip, double deltaSec)
    {
        return Move(clip, deltaSec);
    }

    /// <summary>Moves the clip one lane up; the top lane stays. A repeat-take part stays on its lane (false).</summary>
    public static bool LaneUp(TrackModel track, AudioClip clip)
    {
        if (TakeGroups.IsGrouped(clip) || clip.Lane == 0) return false;
        clip.Lane--;
        return true;
    }

    /// <summary>Moves the clip one lane down, adding the lane when the track does not have it yet. A repeat-take part stays on its lane (false).</summary>
    public static bool LaneDown(TrackModel track, AudioClip clip)
    {
        if (TakeGroups.IsGrouped(clip)) return false;
        clip.Lane++;
        ClipLanes.Ensure(track, clip.Lane + 1);
        return true;
    }

    /// <summary>Sets the copy's start (never before zero) and lane, taking the first free lane when the wanted one overlaps another clip;
    /// adds the copy to the track and ensures the lane exists.</summary>
    public static void PlaceCopy(TrackModel track, AudioClip copy, double startSec, int lane)
    {
        copy.StartSec = Math.Max(0, startSec);
        copy.Lane = track.AudioClips.Any(c => c.Lane == lane && c.Overlaps(copy.StartSec, copy.EndSec) && !ClipLanes.SameTakeGroup(copy, c))
            ? ClipLanes.FreeLane(track, copy.StartSec, copy.EndSec, copy, copy.TakeGroupId) : lane;
        ClipLanes.Ensure(track, copy.Lane + 1);
        track.AudioClips.Add(copy);
    }

    /// <summary>
    /// Places the copies of one repeat take on one lane, their starts relative to the earliest copy: the wanted lane when it is free over
    /// the whole group span, else the first free lane above it. The caller assigns the group's new id before placing.
    /// </summary>
    public static void PlaceGroup(TrackModel track, IReadOnlyList<AudioClip> copies, double startSec, int lane)
    {
        if (copies.Count == 0) return;
        lane = Math.Max(0, lane);
        var anchor = Math.Max(0, startSec);
        var from = anchor + copies.Min(copy => copy.StartSec);
        var to = anchor + copies.Max(copy => copy.EndSec);
        var target = track.AudioClips.Any(c => c.Lane == lane && c.Overlaps(from, to))
            ? ClipLanes.FreeLane(track, from, to, copies, lane) : lane;
        foreach (var copy in copies)
        {
            copy.StartSec += anchor;
            copy.Lane = target;
        }
        ClipLanes.Ensure(track, target + 1);
        track.AudioClips.AddRange(copies);
    }
}
