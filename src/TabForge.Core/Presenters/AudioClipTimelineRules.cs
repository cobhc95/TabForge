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

    /// <summary>The song time in seconds at the score cursor: cell <paramref name="cell"/> of <paramref name="bar"/> (the bar clamped to the song), at the bar's first performance.</summary>
    public static double SecondsAtCursor(SongProject song, int bar, int cell)
    {
        var bars = BarCount(song);
        if (bars == 0) return 0;
        bar = Math.Clamp(bar, 0, bars - 1);
        return SecondsAtBar(song, bar, cell / (double)Math.Max(1, MusicTime.BarSlots(song, bar)));
    }

    /// <summary>
    /// Like <see cref="SecondsAtBar"/>, but at the performance of <paramref name="bar"/> nearest to <paramref name="referenceSec"/> instead of its first one.
    /// The timeline draws every pass of a repeat on the same written bar, so a clip drag reads the pointer through this with the grabbed time as the reference:
    /// the clip then moves in performed time (by the exact delta) and never jumps to the first pass of a repeat. A bar played once gives its one time.
    /// </summary>
    public static double SecondsAtBarNear(SongProject song, int bar, double fraction, double referenceSec)
    {
        var clamped = Math.Clamp(fraction, 0, 1);
        var best = double.NaN;
        foreach (var performed in SongExtent.Timing(song).Performed)
        {
            if (performed.Bar != bar) continue;
            var sec = (performed.StartMs + (performed.EndMs - performed.StartMs) * clamped) / MillisecondsPerSecond;
            if (double.IsNaN(best) || Math.Abs(sec - referenceSec) < Math.Abs(best - referenceSec)) best = sec;
        }
        return double.IsNaN(best) ? SecondsAtBar(song, bar, fraction) : best;
    }

    /// <summary>
    /// The song time at <paramref name="fraction"/> through <paramref name="bar"/>, read along the pass that holds <paramref name="referenceSec"/>: inside the run of
    /// consecutive written bars the reference sits in, the bar's own performance there; outside it, the run's edge continued bar by bar (a bar's length is its first
    /// performance's). The result is continuous and increasing in the written position, so a pointer crossing a repeat sign or a volta bar moves a clip by the
    /// pointer's distance and never lands on another pass (or on 0) because the bar there is not performed next to the reference.
    /// </summary>
    public static double SecondsAtBarAlong(SongProject song, int bar, double fraction, double referenceSec)
    {
        var performed = SongExtent.Timing(song).Performed;
        if (performed.Length == 0) return SecondsAtBar(song, bar, fraction);
        var clamped = Math.Clamp(fraction, 0, 1);
        var index = Math.Max(0, LastStartingAtOrBefore(performed, Math.Max(0, referenceSec) * MillisecondsPerSecond));
        var (low, high) = (index, index);
        while (low > 0 && performed[low - 1].Bar + 1 == performed[low].Bar) low--;
        while (high < performed.Length - 1 && performed[high + 1].Bar == performed[high].Bar + 1) high++;
        var (first, last) = (performed[low].Bar, performed[high].Bar);
        if (bar >= first && bar <= last)
        {
            var inside = performed[low + bar - first];
            return (inside.StartMs + (inside.EndMs - inside.StartMs) * clamped) / MillisecondsPerSecond;
        }
        var lengths = FirstLengths(performed, Math.Max(bar, last) + 1);
        double Length(int b) => b >= 0 && b < lengths.Length && lengths[b] > 0 ? lengths[b] : bar > last ? performed[high].EndMs - performed[high].StartMs : performed[low].EndMs - performed[low].StartMs;
        double ms;
        if (bar > last)
        {
            ms = performed[high].EndMs;
            for (var b = last + 1; b < bar; b++) ms += Length(b);
            ms += Length(bar) * clamped;
        }
        else
        {
            ms = performed[low].StartMs;
            for (var b = first - 1; b > bar; b--) ms -= Length(b);
            ms -= Length(bar) * (1 - clamped);
        }
        return Math.Max(0, ms) / MillisecondsPerSecond;
    }

    /// <summary>The length in ms of each written bar's first performance (0 for a bar never performed), for bars 0 to <paramref name="count"/> - 1.</summary>
    private static double[] FirstLengths(PerformedBar[] performed, int count)
    {
        var lengths = new double[Math.Max(0, count)];
        foreach (var entry in performed)
            if (entry.Bar >= 0 && entry.Bar < lengths.Length && lengths[entry.Bar] <= 0) lengths[entry.Bar] = entry.EndMs - entry.StartMs;
        return lengths;
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
