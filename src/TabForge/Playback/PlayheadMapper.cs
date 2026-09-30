namespace TabForge.Playback;

/// <summary>
/// Maps an absolute musical time to a bar/cell position using the timeline's own bar map.
/// The playhead, the score caret and the arrangement all use this, so visual position and audible
/// position can never disagree about *where* the music is.
/// </summary>
public static class PlayheadMapper
{
    public static PlaybackPosition Map(ScoreTimeline timeline, double ms, int fallbackBar, int fallbackCell)
    {
        var pos = new PlaybackPosition { ElapsedMs = ms, Bar = fallbackBar, Cell = fallbackCell };
        if (timeline.Bars.Count == 0) return pos;

        var bar = timeline.BarAt(ms);
        pos.Bar = bar.Bar;
        var fraction = bar.SlotFraction(ms);   // a fermata hold keeps the playhead on its beat
        pos.BarFraction = fraction;
        var slots = Math.Max(1, bar.Slots);
        pos.Cell = Math.Clamp((int)Math.Floor(fraction * slots), 0, slots - 1);
        return pos;
    }
}
