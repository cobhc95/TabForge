using TabForge.Models;

namespace TabForge.Playback;

// Owns: carrying the engine's by-track-index live state (audible mask, practice silence, sounding notes) over to a new track order when a refreshed timeline swaps in.
// Does not own: the swap itself (PlaybackEngine.SchedulerLoop.cs) or the mask's inputs (SetMuteSolo).
// Tests: PlaybackTrackReorderTests.

public sealed partial class PlaybackEngine
{
    /// <summary>
    /// A timeline compiled after a track move numbers its tracks in the new order; the audible mask, the practice track and the sounding-note owners
    /// were numbered in the old one. They are re-keyed by track id here, so a mute, solo or practice silence stays with its track.
    /// </summary>
    internal void RemapTrackState(Guid[] oldIds, Guid[] newIds)
    {
        if (oldIds.AsSpan().SequenceEqual(newIds)) return;
        int NewIndexOf(int old) => old >= 0 && old < oldIds.Length ? Array.IndexOf(newIds, oldIds[old]) : -1;
        if (Volatile.Read(ref _audible) is { } mask)
        {
            var moved = new bool[newIds.Length];
            for (var i = 0; i < moved.Length; i++) { var old = Array.IndexOf(oldIds, newIds[i]); moved[i] = old < 0 || old >= mask.Length || mask[old]; }
            Volatile.Write(ref _audible, moved);
        }
        var practice = Volatile.Read(ref _practiceSilence);
        if (practice >= 0) Volatile.Write(ref _practiceSilence, NewIndexOf(practice));
        lock (_outputGate)
            for (var slot = 0; slot < _soundingTrack.Length; slot++)
                if (_soundingTrack[slot] > 0) _soundingTrack[slot] = NewIndexOf(_soundingTrack[slot] - 1) + 1;   // 0 (silent) when the track is gone
    }

    /// <summary>
    /// The live track an event of the PLAYING timeline belongs to. The playing timeline numbers its tracks in the order it was compiled in; the live
    /// project may already be reordered (the new order only reaches the timeline at the next bar line), so the index is resolved through the
    /// published track ids, not by position. Falls back to the position when no ids are published.
    /// </summary>
    private static TrackModel? LiveTrackOf(SongProject project, Guid[] ids, int timelineIndex)
    {
        if (ids.Length == 0) return timelineIndex >= 0 && timelineIndex < project.Tracks.Count ? project.Tracks[timelineIndex] : null;
        if (timelineIndex < 0 || timelineIndex >= ids.Length) return null;
        foreach (var track in project.Tracks) if (track.Id == ids[timelineIndex]) return track;
        return null;   // the track is gone from the live song
    }

    /// <summary>The audible mask in the playing timeline's track order (what the dispatcher indexes it by), whatever the live list order is.</summary>
    private bool[] SwapAudibleMask(SongProject project, bool liveSolo, out bool[]? previous)
    {
        // Under the engine lock the timeline swap takes: the order the mask is built in and the order it is stored for are the same one, never the one before a swap that lands in between.
        lock (_gate)
        {
            var ids = PublishedTrackIds;
            bool[] mask;
            if (ids.Length == 0) mask = AudibleMask(project, liveSolo);
            else
            {
                mask = new bool[ids.Length];
                for (var i = 0; i < mask.Length; i++)
                    mask[i] = project.Tracks.FirstOrDefault(t => t.Id == ids[i]) is not { } track || MixerGroups.IsAudible(project, track, liveSolo);
            }
            previous = Volatile.Read(ref _audible);
            Volatile.Write(ref _audible, mask);
            return mask;
        }
    }

    /// <summary>A live-list track index as the playing timeline numbers that track (same index when no order is published or the track is new).</summary>
    private int TimelineIndexOf(int liveIndex)
    {
        var ids = PublishedTrackIds;
        if (liveIndex < 0 || ids.Length == 0 || LiveProject is not { } project || liveIndex >= project.Tracks.Count) return liveIndex;
        var at = Array.IndexOf(ids, project.Tracks[liveIndex].Id);
        return at >= 0 ? at : liveIndex;
    }

    internal void SetAudibleForTest(bool[] mask) => Volatile.Write(ref _audible, mask);
    internal bool[]? AudibleForTest() => Volatile.Read(ref _audible);
}
