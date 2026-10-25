using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the place of a song time in seconds on the bar grid the timeline draws (the bar that holds it, and how far through that bar),
//       so an audio clip's start and end can be placed with the bar geometry. Song time is the performed clock: repeats and jumps are applied,
//       as the playback, the playhead and the recorded takes run on it.
// Does not own: drawing and hit tests (Avalonia TimelineView and TimelineAudioClips), the performed bar order (SongExtent.Timing).
// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests.

/// <summary>Maps song time in seconds onto the bars of the arrangement timeline, on the performed clock.</summary>
public static class AudioClipTimelineRules
{
    private const double MillisecondsPerSecond = 1000;

    /// <summary>The bar that holds <paramref name="seconds"/> on the performed clock and how far through it (0 to 1). A time past the song's end gives (bar count, 0).</summary>
    public static (int Bar, double Fraction) BarAtSeconds(SongProject song, double seconds)
    {
        var bars = BarCount(song);
        var performed = SongExtent.Timing(song).Performed;
        var ms = Math.Max(0, seconds) * MillisecondsPerSecond;
        var index = LastStartingAtOrBefore(performed, ms);
        if (index < 0 || ms >= performed[^1].EndMs) return (bars, 0);
        var bar = performed[index];
        var length = bar.EndMs - bar.StartMs;
        return (bar.Bar, length > 0 ? (ms - bar.StartMs) / length : 0);
    }

    /// <summary>
    /// The song time in seconds at <paramref name="fraction"/> (0 to 1) through <paramref name="bar"/>, at its first performance: the inverse of
    /// <see cref="BarAtSeconds"/>. A bar that no playback order performs (a jump skips it) takes the time of the next bar that is performed.
    /// </summary>
    public static double SecondsAtBar(SongProject song, int bar, double fraction)
    {
        var timing = SongExtent.Timing(song);
        var clamped = Math.Clamp(fraction, 0, 1);
        foreach (var performed in timing.Performed)
            if (performed.Bar == bar) return (performed.StartMs + (performed.EndMs - performed.StartMs) * clamped) / MillisecondsPerSecond;
        foreach (var performed in timing.Performed)
            if (performed.Bar > bar) return performed.StartMs / MillisecondsPerSecond;
        return timing.EndSec;
    }

    /// <summary>The number of bars the timeline draws: the longest track's.</summary>
    public static int BarCount(SongProject song) => song.Tracks.Count == 0 ? 0 : song.Tracks.Max(t => t.Measures.Count);

    /// <summary>The index of the last performed bar that starts at or before <paramref name="ms"/>; -1 when none does.</summary>
    private static int LastStartingAtOrBefore(PerformedBar[] performed, double ms)
    {
        var low = 0;
        var high = performed.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (performed[mid].StartMs <= ms) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found;
    }
}
