using System.Collections.Generic;
using System.Linq;

namespace TabForge.Models;

// Owns: the read-only take-group rules: whether a clip is grouped, its member set, pass order and audibility.
// Does not own: changing a group (TakeGroupEdits), the undo step (DocumentEdits), the drawing or the gestures.
// Tests: TabForge.Avalonia.Tests.RepeatViewTests, LoopPassGroupTests.
public static class TakeGroups
{
    /// <summary>True when the clip carries a repeat-take group id.</summary>
    public static bool IsGrouped(AudioClip clip) => !string.IsNullOrEmpty(clip.TakeGroupId);

    /// <summary>
    /// Is this clip heard? A clip follows <see cref="AudioClip.Muted"/> (a clip over a repeat plays straight through, the repeat view only folds its drawing).
    /// </summary>
    public static bool Audible(TrackModel track, AudioClip clip) => !clip.Muted;

    /// <summary>The clip plus every other clip of <paramref name="track"/> with the same non-null group id; just the clip when it has none.</summary>
    public static List<AudioClip> Members(TrackModel track, AudioClip clip)
    {
        if (!IsGrouped(clip)) return new List<AudioClip> { clip };
        var members = new List<AudioClip> { clip };
        foreach (var other in GroupOf(track, clip.TakeGroupId!))
            if (!ReferenceEquals(other, clip)) members.Add(other);
        return members;
    }

    /// <summary>Every clip of <paramref name="track"/> with <paramref name="groupId"/>, in track order.</summary>
    public static List<AudioClip> GroupOf(TrackModel track, string groupId)
    {
        var members = new List<AudioClip>();
        foreach (var other in track.AudioClips)
            if (other is not null && other.TakeGroupId == groupId) members.Add(other);
        return members;
    }

    /// <summary>Every clip of <paramref name="clips"/> with <paramref name="groupId"/>, in list order.</summary>
    public static List<AudioClip> GroupOf(IReadOnlyList<AudioClip> clips, string groupId)
    {
        var members = new List<AudioClip>();
        foreach (var other in clips)
            if (other is not null && other.TakeGroupId == groupId) members.Add(other);
        return members;
    }

    /// <summary>
    /// The members in pass order: by <see cref="AudioClip.TakeGroupPass"/> when every pass number is positive and unique,
    /// else by start time (then pass number and end) so a partial or malformed group still has a stable, never-throwing order.
    /// </summary>
    public static IReadOnlyList<AudioClip> Ordered(IReadOnlyList<AudioClip> members)
    {
        // Pass numbers only decide the order; rows are renumbered 1..N locally so a partial or malformed group has no gaps.
        var passNumbers = members.Select(member => member.TakeGroupPass).ToList();
        var passOrderIsUsable = passNumbers.All(pass => pass > 0) && passNumbers.Distinct().Count() == members.Count;
        return passOrderIsUsable
            ? members.OrderBy(member => member.TakeGroupPass).ThenBy(member => member.StartSec).ToList()
            : members.OrderBy(member => member.StartSec).ThenBy(member => member.TakeGroupPass)
                .ThenBy(member => member.EndSec).ToList();
    }

}
