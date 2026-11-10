namespace TabForge.Playback;

// Owns: keeping the engine's live audible position from moving back under one scheduler anchor: a rise in the output latency (the audio
//     engine's output opening a moment into a start) holds the playhead until the sound catches up instead of moving it back toward the start.
// Does not own: the audible position itself (PlaybackEngine.AudiblePlayhead) or the latency (RoutedMidiOutput.AudibleLatencyMs).
// Tests: TabForge.Avalonia.Tests.AudiblePlayheadLatencyRiseTests.
// Threads: the UI thread, the only caller of AudiblePlayhead. Key: the timeline, the anchor, the seek request id and the loops done (a start, seek, loop wrap,
//     skip or count-in sets a new anchor, and the position may then move back; a seek back to the same anchor on the same timeline changes only
//     the request id; a loop wrap back to the same loop start changes only the loops done). Ceiling: one value.
internal sealed class AudiblePositionHold
{
    private (ScoreTimeline? Timeline, double AnchorMs, long SeekId, int Loops, double Ms) _last = (null, double.NaN, 0, 0, double.NegativeInfinity);

    /// <summary>The position to draw: <paramref name="ms"/>, or the last one drawn under the same timeline, anchor and seek when that was later.</summary>
    public double Hold(ScoreTimeline timeline, double anchorMs, long seekId, int loopsDone, double ms)
    {
        if (ReferenceEquals(_last.Timeline, timeline) && _last.AnchorMs.Equals(anchorMs) && _last.SeekId == seekId && _last.Loops == loopsDone) ms = Math.Max(ms, _last.Ms);
        _last = (timeline, anchorMs, seekId, loopsDone, ms);
        return ms;
    }
}
