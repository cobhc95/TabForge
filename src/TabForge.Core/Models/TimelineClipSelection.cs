using System.Linq;

namespace TabForge.Models;

// Owns: one selected audio clip of the arrangement timeline with its track, and finding it again by id after the song was replaced.
// Does not own: which clips are selected (SelectionModel), the outline drawn for the selection (TimelineAudioClips) or the edits.
// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests, SelectionModelTests.

/// <summary>The audio clip the timeline has selected, and its track.</summary>
public sealed record TimelineClipSelection(TrackModel Track, AudioClip Clip)
{
    /// <summary>The clip with <paramref name="clipId"/> on the track with <paramref name="trackId"/> in <paramref name="song"/>, or null when the song has no such clip.</summary>
    public static TimelineClipSelection? Find(SongProject song, Guid trackId, string clipId)
    {
        var track = song.Tracks.FirstOrDefault(t => t.Id == trackId);
        var clip = track?.AudioClips.FirstOrDefault(c => c.Id == clipId);
        return track is null || clip is null ? null : new TimelineClipSelection(track, clip);
    }

    /// <summary>The same clip (same track id and clip id) in <paramref name="song"/>, or null when an undo or redo removed it.</summary>
    public TimelineClipSelection? ResolveIn(SongProject song) => Find(song, Track.Id, Clip.Id);
}
