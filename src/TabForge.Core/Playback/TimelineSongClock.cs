using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: mapping a playing timeline's milliseconds to the song's performed seconds (repeats and jumps applied), so a take
//     recorded mid-song lands where the song was when heard. A timeline compiled mid-song drops every bar before its start,
//     so its own milliseconds run behind the song clock by the dropped span (and by 1/Speed, with a count-in in front). The audio engine's
//     song position and the takes both use the song second; only the MIDI clips are rebased to the timeline.
// Does not own: the timeline compile (ScoreToMidiCompiler), the performed order (SongExtent.Timing), the take capture or landing.
// Tests: TabForge.Avalonia.Tests.TimelineSongClockTests.

/// <summary>Converts the playing timeline's milliseconds to song seconds on the performed clock.</summary>
public static class TimelineSongClock
{
    /// <summary>
    /// The song second at <paramref name="timelineMs"/> on <paramref name="timeline"/>, whose bars are a suffix of
    /// <paramref name="performed"/> in playing order. <paramref name="preferSongMs"/> (a loop start, in song seconds) picks the
    /// matching pass nearest to it; otherwise the first matching pass is taken. A null or empty timeline passes the value through.
    /// </summary>
    public static double SongSecAt(PerformedBar[] performed, ScoreTimeline? timeline, double timelineMs, double? preferSongMs = null) =>
        new Mapping(performed, timeline, preferSongMs).SongSecAt(timelineMs);

    /// <summary>
    /// One timeline's mapping to song seconds, its start in the performed order found once: a per-frame caller builds it when the
    /// timeline (or pass hint) changes and then asks <see cref="SongSecAt"/> allocation-free. The count-in holds at the start point.
    /// </summary>
    public sealed class Mapping
    {
        private readonly PerformedBar[] _performed;
        private readonly ScoreTimeline? _timeline;
        private readonly int _start;

        public Mapping(PerformedBar[] performed, ScoreTimeline? timeline, double? preferSongMs = null)
        {
            _performed = performed;
            _timeline = timeline is { Bars.Count: > 0 } && performed.Length > 0 ? timeline : null;
            _start = _timeline is null ? 0 : FindTimelineStart(performed, _timeline, preferSongMs);
        }

        /// <summary>True while <paramref name="timelineMs"/> is inside the count-in, before the start point.</summary>
        public bool InCountIn(double timelineMs) => _timeline is { } timeline && timelineMs < timeline.CountInMs;

        /// <summary>The song second at <paramref name="timelineMs"/>; NaN stays NaN, and a mapping without bars passes the value through.</summary>
        public double SongSecAt(double timelineMs)
        {
            if (double.IsNaN(timelineMs)) return double.NaN;
            if (_timeline is not { } timeline) return Math.Max(0, timelineMs / 1000.0);
            if (InCountIn(timelineMs)) timelineMs = timeline.PlayFromMs;   // the count-in plays before the start point: the song waits there
            var i = LastStartingAtOrBefore(timeline.Bars, timelineMs);
            if (i < 0) i = 0;
            if (i >= timeline.Bars.Count) i = timeline.Bars.Count - 1;
            var bar = timeline.Bars[i];
            var length = bar.EndMs - bar.StartMs;
            var frac = length > 1e-9 ? Math.Clamp((timelineMs - bar.StartMs) / length, 0, 1) : 0;
            var p = _performed[Math.Min(_start + i, _performed.Length - 1)];
            return (p.StartMs + frac * (p.EndMs - p.StartMs)) / 1000.0;
        }
    }

    /// <summary>The song second at <paramref name="timelineMs"/> on <paramref name="timeline"/> of <paramref name="song"/>.</summary>
    public static double SongSecAt(SongProject song, ScoreTimeline? timeline, double timelineMs, double? preferSongMs = null) =>
        SongSecAt(SongExtent.Timing(song).Performed, timeline, timelineMs, preferSongMs);

    /// <summary>The index in <paramref name="performed"/> where <paramref name="timeline"/>'s bars begin in playing order.</summary>
    private static int FindTimelineStart(PerformedBar[] performed, ScoreTimeline timeline, double? preferSongMs)
    {
        var depth = Math.Min(8, Math.Min(timeline.Bars.Count, performed.Length));
        var best = -1;
        for (var k = 0; k + depth <= performed.Length; k++)
        {
            var j = 0;
            while (j < depth && performed[k + j].Bar == timeline.Bars[j].Bar) j++;
            if (j < depth) continue;
            if (preferSongMs is null) return k;
            if (best < 0 || Math.Abs(performed[k].StartMs / 1000.0 - preferSongMs.Value) < Math.Abs(performed[best].StartMs / 1000.0 - preferSongMs.Value)) best = k;
        }
        if (best >= 0) return best;
        // No multi-bar run matched (a one-bar timeline, or loop repeats inside it): the first performance of its first bar.
        for (var k = 0; k < performed.Length; k++)
            if (performed[k].Bar == timeline.Bars[0].Bar) return k;
        return 0;
    }

    /// <summary>The index of the last timeline bar starting at or before <paramref name="ms"/>; -1 when none does.</summary>
    private static int LastStartingAtOrBefore(System.Collections.Generic.List<ScoreBar> bars, double ms)
    {
        var low = 0;
        var high = bars.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (bars[mid].StartMs <= ms) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found;
    }
}
