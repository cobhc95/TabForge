using TabForge.Models;

namespace TabForge.Services;

// Owns: mapping the tracks of a RangeSnapshot onto the target song's tracks: source k -> target start + k, in order, tab onto tab and audio onto audio; what cannot map is skipped and counted.
// Does not own: placing the content (the paste), the snapshot (RangeSnapshot) or showing the notice (the caller).
// Tests: RangeClipboardTests.

/// <summary>One mapped track: snapshot track <see cref="Source"/> goes onto song track <see cref="Target"/>.</summary>
public readonly record struct TrackPairing(int Source, int Target);

/// <summary>The result of mapping: the pairs, how many source tracks were skipped and the one notice for them (null when none).</summary>
public sealed record PasteMapping(IReadOnlyList<TrackPairing> Pairs, int Skipped, string? Notice);

public static class PasteMapper
{
    /// <summary>Maps <paramref name="snapshot"/> tracks onto <paramref name="targets"/> from <paramref name="startTrack"/>; a source track with no target left, or whose target is the other kind (tab / audio), is skipped.</summary>
    public static PasteMapping Map(RangeSnapshot snapshot, IReadOnlyList<TrackModel> targets, int startTrack)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targets);
        startTrack = Math.Max(0, startTrack);
        var pairs = new List<TrackPairing>();
        for (var source = 0; source < snapshot.Tracks.Count; source++)
        {
            var target = startTrack + source;
            if (target < targets.Count && (snapshot.Tracks[source].Kind == TrackKind.Audio) == targets[target].IsAudio) pairs.Add(new TrackPairing(source, target));
        }
        var skipped = snapshot.Tracks.Count - pairs.Count;
        return new PasteMapping(pairs, skipped, skipped == 0 ? null : $"{skipped} {(skipped == 1 ? "track" : "tracks")} skipped: no matching track");
    }
}
