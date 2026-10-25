using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the S5b overlap mute rule for a landed take: an older clip of the same kind that the new take covers completely is muted.
// Does not own: the landing itself (RecordedTakes.Place), lane audibility (ClipLanes) or the undo step around the landing (the callers).
// Tests: TabForge.Avalonia.Tests.PassAwareEditTests.

/// <summary>
/// A landed take wins where it covers an older clip completely. There is no partial mute in the model, so a partly covered older
/// clip keeps its own mute state (its lane may still be greyed by <see cref="ClipLanes.PlayNewTake"/>, the T1 newest-take rule).
/// </summary>
public static class TakeOverlap
{
    /// <summary>Two spans within this many seconds are treated as the same edge.</summary>
    private const double ToleranceSec = 0.005;

    /// <summary>
    /// Mutes every older clip on <paramref name="track"/> of the same kind (audio or MIDI) whose performed span lies inside
    /// <paramref name="take"/>'s. Returns whether any clip changed. The new take itself and its legacy take-group siblings are left alone.
    /// </summary>
    public static bool MuteCovered(TrackModel track, AudioClip take)
    {
        var changed = false;
        foreach (var older in track.AudioClips)
        {
            if (ReferenceEquals(older, take) || older.IsMidi != take.IsMidi || older.Muted) continue;
            if (take.TakeGroupId is not null && older.TakeGroupId == take.TakeGroupId) continue;
            if (older.StartSec < take.StartSec - ToleranceSec || older.EndSec > take.EndSec + ToleranceSec) continue;
            older.Muted = true;
            changed = true;
        }
        return changed;
    }
}
