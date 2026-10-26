using System.Collections.Generic;
using System.Linq;
using TabForge.Features.Recording.Rules;
using TabForge.Models;

namespace TabForge.Avalonia.Platform;

// Owns: the lane edit cursor a click in a clip lane leaves (the split position when it lies inside the selected clip).
// Does not own: the selected clip (TimelineClipSelection in Core), the outline drawn for the selection (TimelineAudioClips), the cursor's use (MainWindowClipProperties.Split) or the edits.
// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests, RecordingLaneLifecycleTests, ClipEditingBatchFourTests.

/// <summary>The lane edit cursor: the last click in a clip lane. <see cref="Position"/> is the
/// pass-aware position when the click resolved one (null: the old first-occurrence <see cref="Sec"/>).</summary>
internal readonly record struct TimelineLaneCursor(TrackModel Track, int Lane, double Sec, RepeatPosition? Position)
{
    /// <summary>The cursor on the track of the same id in <paramref name="song"/>, or null when that track is gone.</summary>
    public TimelineLaneCursor? ResolveIn(SongProject song)
    {
        var id = Track.Id;
        var track = song.Tracks.FirstOrDefault(t => t.Id == id);
        return track is null ? null : this with { Track = track };
    }

    /// <summary>True when the marker lies inside a clip of its own lane (within <paramref name="among"/> when given): a marker outside every clip is stale and never drawn.</summary>
    public bool InsideClip(IEnumerable<AudioClip>? among = null)
    {
        var (track, lane, sec) = (Track, Lane, Sec);
        return (among ?? track.AudioClips).Any(clip => clip.Lane == lane && sec >= clip.StartSec - 1e-6 && sec <= clip.EndSec + 1e-6 && track.AudioClips.Any(own => ReferenceEquals(own, clip)));
    }
}
