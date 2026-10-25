namespace TabForge.Playback;

// Owns: mapping an absolute musical time to a bar and cell position, using the timeline's own bar map.
// Does not own: the playhead drawing (app Views).
// Tests: no dedicated test class.

/// <summary>
/// Maps an absolute musical time to a bar/cell position using the timeline's own bar map.
/// The playhead, the score caret and the arrangement all use this, so visual position and audible
/// position can never disagree about *where* the music is.
/// </summary>
public static class PlayheadMapper
{
    public static PlaybackPosition Map(ScoreTimeline timeline, double ms, int fallbackBar, int fallbackCell) =>
        MapInto(new PlaybackPosition(), timeline, ms, fallbackBar, fallbackCell);

    /// <summary>Sets <paramref name="pos"/> to the start position (no timeline loaded); returns it. The per-frame path uses it instead of allocating.</summary>
    public static PlaybackPosition Reset(PlaybackPosition pos)
    {
        pos.Bar = 0;
        pos.Cell = 0;
        pos.BarFraction = 0;
        pos.ElapsedMs = 0;
        return pos;
    }

    /// <summary>Fills <paramref name="pos"/> instead of allocating (the per-frame path); returns it.</summary>
    public static PlaybackPosition MapInto(PlaybackPosition pos, ScoreTimeline timeline, double ms, int fallbackBar, int fallbackCell)
    {
        pos.ElapsedMs = ms;
        pos.Bar = fallbackBar;
        pos.Cell = fallbackCell;
        pos.BarFraction = 0;
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
