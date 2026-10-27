using TabForge.Models;

namespace TabForge.Documents;

/// <summary>One overlap an edit made: the clip being edited and the existing clip on the same lane of <see cref="Track"/> that it now covers.</summary>
public sealed record ClipOverlap(TrackModel Track, AudioClip Edited, AudioClip Existing);

// Owns: finding the clips (audio and MIDI alike) that an edit made overlap another clip on the same lane of a track, and moving each existing clip to the first
//   lower lane where it fits (a new lane when none is free). The clip being edited never moves.
// Does not own: asking the user (ClipOverlapPolicy, the prompt), the undo step (DocumentEdits) or the edits that cause the overlap.
// Tests: TabForge.Avalonia.Tests.ClipLaneOverlapTests.
public static class ClipLaneOverlap
{
    /// <summary>Overlaps shorter than this many seconds are rounding noise (clips that only touch).</summary>
    private const double Tolerance = 1e-6;

    /// <summary>The existing clips that each of <paramref name="edited"/> now overlaps on its lane. Two edited clips never count against each other, nor do clips of one take group.</summary>
    public static List<ClipOverlap> Find(SongProject project, IEnumerable<AudioClip> edited)
    {
        var set = edited.ToList();
        var found = new List<ClipOverlap>();
        foreach (var clip in set)
        {
            var track = project.Tracks.FirstOrDefault(t => t.AudioClips.Any(c => ReferenceEquals(c, clip)));
            if (track is null) continue;
            foreach (var other in track.AudioClips)
            {
                if (ReferenceEquals(other, clip) || other.Lane != clip.Lane || set.Any(c => ReferenceEquals(c, other)) || ClipLanes.SameTakeGroup(clip, other)) continue;
                if (other.StartSec < clip.EndSec - Tolerance && other.EndSec > clip.StartSec + Tolerance && !found.Any(f => ReferenceEquals(f.Existing, other)))
                    found.Add(new ClipOverlap(track, clip, other));
            }
        }
        return found;
    }

    /// <summary>Moves every existing clip of <paramref name="overlaps"/> to the first lane below its own where it overlaps nothing, adding the lane when none is free;
    /// a lane it creates plays as the lane it left. Returns how many clips moved.</summary>
    public static int Move(IReadOnlyList<ClipOverlap> overlaps)
    {
        var moved = 0;
        foreach (var overlap in overlaps)
        {
            var (track, clip) = (overlap.Track, overlap.Existing);
            var from = clip.Lane;
            var lane = ClipLanes.FreeLane(track, clip.StartSec, clip.EndSec, clip, clip.TakeGroupId, from + 1);
            var created = lane >= track.Lanes.Count;
            ClipLanes.Ensure(track, lane + 1);
            if (created) track.Lanes[lane].Plays = ClipLanes.Plays(track, from);
            clip.Lane = lane;
            moved++;
        }
        return moved;
    }
}
