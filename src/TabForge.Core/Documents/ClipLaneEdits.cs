using System.Linq;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Documents;

// Owns: the take-lane menu's model edits: a new empty one-bar MIDI clip, renaming a lane, moving a lane up or down, and deleting a lane with its clips.
// Does not own: the undo step, the menu, the prompts (the window's take-lane commands) or the lane rules themselves (ClipLanes).
// Tests: TabForge.Avalonia.Tests.TakeLaneMenuTests.
/// <summary>The pure edits behind the take-lane menu; each returns whether it changed the track, and runs inside the caller's one undo step.</summary>
public static class ClipLaneEdits
{
    /// <summary>The name of a clip made by "New empty MIDI clip here".</summary>
    public const string NewMidiClipName = "MIDI clip";

    /// <summary>How many clips sit on <paramref name="lane"/>.</summary>
    public static int ClipsOn(TrackModel track, int lane) => track.AudioClips.Count(clip => clip.Lane == lane);

    /// <summary>The length of the bar that holds <paramref name="startSec"/>, in seconds (a 4/4 bar at the song tempo past the last bar).</summary>
    public static double BarSeconds(SongProject song, double startSec)
    {
        var (bar, _) = AudioClipTimelineRules.BarAtSeconds(song, startSec);
        var length = AudioClipTimelineRules.SecondsAtBar(song, bar, 1) - AudioClipTimelineRules.SecondsAtBar(song, bar, 0);
        return length > 0 ? length : 240.0 / Math.Max(20, song.Tempo);
    }

    /// <summary>
    /// Adds an empty MIDI clip one bar long at <paramref name="startSec"/> on <paramref name="lane"/> (the first free lane when a clip is in the way);
    /// the clip is returned, with the lane it landed on in <see cref="AudioClip.Lane"/>. Without <paramref name="dodge"/> it stays on <paramref name="lane"/> even over
    /// another clip (the overlap check then decides which clip moves).
    /// </summary>
    public static AudioClip NewMidiClip(SongProject song, TrackModel track, int lane, double startSec, bool dodge = true)
    {
        var start = double.IsNaN(startSec) || startSec < 0 ? 0 : startSec;
        var length = BarSeconds(song, start);
        var target = Math.Max(0, lane);
        if (dodge && track.AudioClips.Any(c => c.Lane == target && c.Overlaps(start, start + length))) target = ClipLanes.FreeLane(track, start, start + length);
        ClipLanes.Ensure(track, target + 1);
        var clip = new AudioClip { Name = NewMidiClipName, Notes = new List<ClipNote>(), StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = target };
        track.AudioClips.Add(clip);
        return clip;
    }

    /// <summary>Gives the lane a name (blank clears it); false when it already has that name.</summary>
    public static bool Rename(TrackModel track, int lane, string? name)
    {
        var next = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (lane < 0 || (next is null && (lane >= track.Lanes.Count || track.Lanes[lane].Name is null))) return false;
        ClipLanes.Ensure(track, lane + 1);
        if (track.Lanes[lane].Name == next) return false;
        track.Lanes[lane].Name = next;
        return true;
    }

    /// <summary>Swaps the lane with its neighbour (<paramref name="delta"/> -1 up, +1 down): the clips, the play state and the name move together. False at either end.</summary>
    public static bool Move(TrackModel track, int lane, int delta)
    {
        var other = lane + delta;
        if (Math.Abs(delta) != 1 || lane < 0 || other < 0 || other >= ClipLanes.Count(track)) return false;
        ClipLanes.Ensure(track, Math.Max(lane, other) + 1);
        (track.Lanes[lane], track.Lanes[other]) = (track.Lanes[other], track.Lanes[lane]);
        foreach (var clip in track.AudioClips)
        {
            if (clip.Lane == lane) clip.Lane = other;
            else if (clip.Lane == other) clip.Lane = lane;
        }
        return true;
    }

    /// <summary>Removes the lane and every clip on it; the lanes below move up. Where a removed playing lane leaves a kind of take (audio or MIDI) with none playing, the last lane holding that kind plays.</summary>
    public static bool Delete(TrackModel track, int lane)
    {
        if (lane < 0 || lane >= ClipLanes.Count(track)) return false;
        ClipLanes.Ensure(track, lane + 1);
        var removedPlaying = track.Lanes[lane].Plays;
        track.AudioClips.RemoveAll(clip => clip.Lane == lane);
        foreach (var clip in track.AudioClips) if (clip.Lane > lane) clip.Lane--;
        track.Lanes.RemoveAt(lane);
        if (removedPlaying)
            foreach (var midi in new[] { false, true })
            {
                var holding = Enumerable.Range(0, track.Lanes.Count).Where(l => track.AudioClips.Any(c => c.Lane == l && c.IsMidi == midi)).ToList();
                if (holding.Count > 0 && !holding.Any(l => track.Lanes[l].Plays)) track.Lanes[holding[^1]].Plays = true;
            }
        return true;
    }
}
