using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Restart in place: a recompile that keeps the run's start (mixer, routing, plug-in steps) resumes at the exact
/// position in the new timeline and skips every event already sent, so no note is played twice or rewound.
/// Owns: the captured resume point and its mapping onto the new timeline. Does not own: compiling or dispatch.
/// Tests: SelfTestInteractionsPlugins (I-5 "no note is sent twice"), TestEngineSync*.
/// </summary>
public sealed partial class PlaybackEngine
{
    private readonly record struct ResumePoint(ScoreTimeline Timeline, int BarIndex, double Ms, double SentThroughMs);

    private ResumePoint? _resume;
    private bool _resumeMapped;
    /// <summary>Events at or before this stream time were already sent by the previous run (NaN: none).</summary>
    private double _resumeSkipThroughMs = double.NaN;
    /// <summary>Latest event time the scheduler has dispatched (the dispatch lead runs ahead of the reported position).</summary>
    private double _sentThroughMs;

    /// <summary>Recompiles from the run's own start and resumes where it is; false when the position cannot be mapped.</summary>
    private bool TryRestartInPlace(SongProject project, PlaybackOptions opts)
    {
        var old = _timeline;
        if (old is null) return false;
        var ms = Volatile.Read(ref _currentStreamMs);
        var bar = BarIndexAt(old, ms);
        if (bar < 0) return false;
        _resume = new ResumePoint(old, bar, ms, Math.Max(ms, Volatile.Read(ref _sentThroughMs)));
        _resumeMapped = false;
        RestartKeepingState(project, opts, _paused);
        _resume = null;
        return _resumeMapped;
    }

    /// <summary>Called by Start once the new timeline is compiled: maps the captured point onto it.</summary>
    private void ApplyResume(ScoreTimeline timeline)
    {
        _resumeSkipThroughMs = double.NaN;
        if (_resume is not { } r) return;
        var i = r.BarIndex;
        if (timeline.Bars.Count != r.Timeline.Bars.Count || timeline.Bars[i].Bar != r.Timeline.Bars[i].Bar) return;
        var o = r.Timeline.Bars[i];
        var n = timeline.Bars[i];
        var oldLength = Math.Max(1e-6, o.EndMs - o.StartMs);
        var scale = (n.EndMs - n.StartMs) / oldLength;
        _startMs = n.StartMs + (r.Ms - o.StartMs) * scale;
        // The previous run is stopped by now; anything it sent after the capture counts too.
        var sent = Math.Max(r.SentThroughMs, Volatile.Read(ref _sentThroughMs));
        _resumeSkipThroughMs = _startMs + (sent - r.Ms) * scale;
        _resumeMapped = true;
    }

    private static int BarIndexAt(ScoreTimeline timeline, double ms)
    {
        var bars = timeline.Bars;
        for (var i = 0; i < bars.Count; i++)
            if (ms >= bars[i].StartMs && ms < bars[i].EndMs) return i;
        return -1;
    }

    /// <summary>First event index for a run starting at <paramref name="startMs"/>, past anything the previous run sent.</summary>
    private int FirstIndexToPlay(List<ScoreEvent> events, double startMs)
    {
        var index = FirstIndexAtOrAfter(events, startMs);
        var skip = _resumeSkipThroughMs;
        if (double.IsNaN(skip)) return index;
        while (index < events.Count && events[index].TimeMs <= skip + SeekToleranceMs) index++;
        return index;
    }
}
