namespace TabForge.Playback;

// Owns: keeping the engine's live audible position from moving back under one scheduler anchor: a rise in the output latency (the audio
//     engine's output opening a moment into a start) holds the playhead until the sound catches up instead of moving it back toward the start.
// Does not own: the audible position itself (PlaybackEngine.AudiblePlayhead) or the latency (RoutedMidiOutput.AudibleLatencyMs).
// Tests: TabForge.Avalonia.Tests.AudiblePlayheadLatencyRiseTests.
// Threads: the UI thread, the only caller of AudiblePlayhead. Key: the timeline and the anchor (a start, seek, loop wrap, skip or count-in sets a
//     new anchor, and the position may then move back). Ceiling: one value.
internal sealed class AudiblePositionHold
{
    private (ScoreTimeline? Timeline, double AnchorMs, double Ms) _last = (null, double.NaN, double.NegativeInfinity);

    /// <summary>The position to draw: <paramref name="ms"/>, or the last one drawn under the same timeline and anchor when that was later.</summary>
    public double Hold(ScoreTimeline timeline, double anchorMs, double ms)
    {
        if (ReferenceEquals(_last.Timeline, timeline) && _last.AnchorMs.Equals(anchorMs)) ms = Math.Max(ms, _last.Ms);
        _last = (timeline, anchorMs, ms);
        return ms;
    }
}
