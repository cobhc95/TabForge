using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: finding the loop-recording takes that belong together (the clips one recording cut into laps, named "... take N") and making one of them the
//   active take: it is unmuted and its lane plays, the other takes are muted and their lanes grey. Nothing is deleted.
// Does not own: cutting the recording into takes (RecordedTakes), the undo step (the caller), the menu rows (TimelineMenus) or the lane rules (ClipLanes).
// Tests: TabForge.Avalonia.Tests.LoopTakesTests.
public static class LoopTakes
{
    private static readonly Regex TakeName = new(@"^(?<base>.*) take (?<n>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The takes <paramref name="clip"/> belongs to, in take order (empty when it is not one of several takes).</summary>
    public static List<AudioClip> Of(TrackModel track, AudioClip clip)
    {
        var match = TakeName.Match(clip.Name ?? "");
        if (!match.Success) return new List<AudioClip>();
        var group = track.AudioClips
            .Select(c => (Clip: c, Match: TakeName.Match(c.Name ?? "")))
            .Where(t => t.Match.Success && t.Clip.IsMidi == clip.IsMidi && SameRecording(clip, t.Clip) && t.Match.Groups["base"].Value == match.Groups["base"].Value)
            .OrderBy(t => int.TryParse(t.Match.Groups["n"].Value, out var n) ? n : int.MaxValue)
            .Select(t => t.Clip).ToList();
        return group.Count > 1 ? group : new List<AudioClip>();
    }

    private static bool SameRecording(AudioClip a, AudioClip b) =>
        a.LoopRecordingId is not null || b.LoopRecordingId is not null ? a.LoopRecordingId == b.LoopRecordingId : a.File == b.File;

    /// <summary>True when <paramref name="take"/> is the audible one of its takes.</summary>
    public static bool IsActive(TrackModel track, AudioClip take) => !take.Muted && (take.Lane >= track.Lanes.Count || track.Lanes[take.Lane].Plays);

    /// <summary>Makes <paramref name="take"/> the active one of <paramref name="takes"/>; false when nothing changed.</summary>
    public static bool Activate(TrackModel track, IReadOnlyList<AudioClip> takes, AudioClip take)
    {
        if (!takes.Contains(take) || IsActive(track, take) && takes.All(t => t == take || t.Muted)) return false;
        foreach (var other in takes) other.Muted = other != take;
        ClipLanes.Ensure(track, take.Lane + 1);
        track.Lanes[take.Lane].Plays = true;
        // Only lanes holding other takes of this group go grey (and only when no clip outside the group shares them); other lanes are untouched.
        foreach (var other in takes)
            if (other != take && other.Lane != take.Lane && other.Lane < track.Lanes.Count && !track.AudioClips.Any(c => c.Lane == other.Lane && !takes.Contains(c)))
                track.Lanes[other.Lane].Plays = false;
        return true;
    }
}
