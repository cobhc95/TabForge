using System.Linq;
using TabForge.Models;

namespace TabForge.Documents;

// Owns: moving one audio or MIDI clip, or its whole loop-record take group, to another track or lane of the arrangement. The clip leaves its
//       track, takes the requested lane (the first free lane under it when that lane is busy), the lanes it needs are added, and the source
//       track's empty lanes close up. The clip drag lets every track but a bus host a clip of either kind (CanHost); CanMove (same kind only) is the paste rule.
// Does not own: the drag that chooses the target track and lane (TimelineClipGestureController), the undo step (DocumentEdits.Run), and the
//       lane rules (ClipLanes in TabForge.Models).
// Tests: ClipVerticalDragTests (the timeline clip drag).
public static class ClipRelocation
{
    /// <summary>True when a clip may move from <paramref name="source"/> to <paramref name="target"/>: both tracks are audio or both are not.</summary>
    public static bool CanMove(TrackModel source, TrackModel target) => source.IsAudio == target.IsAudio;

    /// <summary>True when <paramref name="target"/> can hold clips of either kind: every track but a bus (a MIDI clip on an audio track sounds through its instrument plug-in).</summary>
    public static bool CanHost(TrackModel target) => !target.IsBus;

    /// <summary>
    /// Moves <paramref name="clip"/>, which must be on <paramref name="source"/>, onto <paramref name="target"/> at <paramref name="lane"/>.
    /// The caller checks <see cref="CanMove"/> (or <see cref="CanHost"/>) first; the clip's start and length are already final. With
    /// <paramref name="dodge"/> false the clip stays on <paramref name="lane"/> even over another clip (the overlap check then decides which one moves).
    /// </summary>
    public static void Move(TrackModel source, TrackModel target, AudioClip clip, int lane, bool dodge = true)
    {
        if (TakeGroups.IsGrouped(clip))
        {
            MoveGroup(source, target, clip, lane);
            return;
        }
        MoveMany(new[] { (source, clip, target, lane) }, dodge);
    }

    /// <summary>
    /// Moves several clips at once, each to its own track and lane: all leave first, then all land (so a clip never dodges one that is leaving too),
    /// and the emptied lanes close up once at the end. The clips' starts and lengths are already final; none may be in a take group.
    /// </summary>
    public static void MoveMany(IReadOnlyList<(TrackModel Source, AudioClip Clip, TrackModel Target, int Lane)> moves, bool dodge = true)
    {
        foreach (var move in moves) move.Source.AudioClips.Remove(move.Clip);
        foreach (var (_, clip, target, lane) in moves)
        {
            clip.Lane = Math.Max(0, lane);
            if (dodge && target.AudioClips.Any(other => other.Lane == clip.Lane && other.Overlaps(clip.StartSec, clip.EndSec) && !ClipLanes.SameTakeGroup(clip, other)))
                clip.Lane = ClipLanes.FreeLane(target, clip.StartSec, clip.EndSec, clip, clip.TakeGroupId);
            ClipLanes.Ensure(target, clip.Lane + 1);
            target.AudioClips.Add(clip);
        }
        foreach (var track in moves.SelectMany(move => new[] { move.Source, move.Target }).Distinct()) ClipLanes.Compact(track);
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
