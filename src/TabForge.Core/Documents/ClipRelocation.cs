using System.Linq;
using TabForge.Models;

namespace TabForge.Documents;

// Owns: moving one audio or MIDI clip, or its whole loop-record take group, to another track or lane of the arrangement. The clip leaves its
//       track, takes the requested lane (the first free lane under it when that lane is busy), the lanes it needs are added, and the source
//       track's empty lanes close up. Only tracks of the same kind take each other's clips (audio with audio, MIDI with the non-audio tracks).
// Does not own: the drag that chooses the target track and lane (TimelineClipGestureController), the undo step (DocumentEdits.Run), and the
//       lane rules (ClipLanes in TabForge.Models).
// Tests: none of its own yet; the move is exercised by the timeline clip drag (TimelineClipGestureController).
public static class ClipRelocation
{
    /// <summary>True when a clip may move from <paramref name="source"/> to <paramref name="target"/>: both tracks are audio or both are not.</summary>
    public static bool CanMove(TrackModel source, TrackModel target) => source.IsAudio == target.IsAudio;

    /// <summary>
    /// Moves <paramref name="clip"/>, which must be on <paramref name="source"/>, onto <paramref name="target"/> at <paramref name="lane"/>.
    /// The caller checks <see cref="CanMove"/> first; the clip's start and length are already final.
    /// </summary>
    public static void Move(TrackModel source, TrackModel target, AudioClip clip, int lane)
    {
        if (TakeGroups.IsGrouped(clip))
        {
            MoveGroup(source, target, clip, lane);
            return;
        }
        source.AudioClips.Remove(clip);
        clip.Lane = lane;
        if (target.AudioClips.Any(other => other.Lane == lane && other.Overlaps(clip.StartSec, clip.EndSec) && !ClipLanes.SameTakeGroup(clip, other)))
            clip.Lane = ClipLanes.FreeLane(target, clip.StartSec, clip.EndSec, clip, clip.TakeGroupId);
        ClipLanes.Ensure(target, clip.Lane + 1);
        target.AudioClips.Add(clip);
        ClipLanes.Compact(source);
    }

    /// <summary>
    /// Moves every member of the clip's take group to one lane on <paramref name="target"/>, choosing the wanted lane when the whole group
    /// span is free there and the first free lane above it otherwise. Members keep their starts; foreign clips are never overlapped.
    /// </summary>
    private static void MoveGroup(TrackModel source, TrackModel target, AudioClip clip, int lane)
    {
        lane = Math.Max(0, lane);
        var members = TakeGroups.Members(source, clip);
        var from = members.Min(member => member.StartSec);
        var to = members.Max(member => member.EndSec);
        foreach (var member in members) source.AudioClips.Remove(member);
        var targetLane = target.AudioClips.Any(other => other.Lane == lane && other.Overlaps(from, to))
            ? ClipLanes.FreeLane(target, from, to, members, lane) : lane;
        foreach (var member in members) member.Lane = targetLane;
        ClipLanes.Ensure(target, targetLane + 1);
        target.AudioClips.AddRange(members);
        ClipLanes.Compact(source);
    }
}
