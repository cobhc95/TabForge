namespace TabForge.Views;

/// <summary>Chunked horizontal follow policy for the music-sheet viewport.</summary>
internal static class ScoreHorizontalFollow
{
    private const double AdvanceViewportFraction = 0.5;
    private const double LookAheadBars = 0.75;
    private const double MaximumLookAheadViewportFraction = 0.45;
    private const double MinimumLookAheadPixels = 8;

    /// <summary>
    /// Returns the next horizontal offset only when the playhead enters the viewport's end zone.
    /// Each advance is half a viewport; the look-ahead is three quarters of a bar, capped below half
    /// a viewport so an unusually wide bar cannot cause repeated jumps at one playhead position.
    /// </summary>
    internal static double? NextOffset(double currentOffset, double viewportWidth, double playheadX,
        double barWidth, double systemRight, double lookAheadBars = LookAheadBars)
    {
        if (!double.IsFinite(currentOffset) || !double.IsFinite(viewportWidth) ||
            !double.IsFinite(playheadX) || !double.IsFinite(barWidth) ||
            !double.IsFinite(systemRight) || viewportWidth <= 1)
            return null;

        currentOffset = Math.Max(0, currentOffset);
        var maximumOffset = Math.Max(0, systemRight - viewportWidth);

        // A seek or a new row can put the playhead to the left of the current viewport.
        if (playheadX < currentOffset - 0.5)
            return Math.Clamp(playheadX - viewportWidth * 0.25, 0, maximumOffset);

        var viewportRight = currentOffset + viewportWidth;
        if (systemRight <= viewportRight + 0.5) return null;

        var lookAhead = Math.Min(Math.Max(MinimumLookAheadPixels, barWidth * Math.Clamp(lookAheadBars, 0, 2)),
            viewportWidth * MaximumLookAheadViewportFraction);
        if (playheadX < viewportRight - lookAhead) return null;

        var next = Math.Min(maximumOffset, currentOffset + viewportWidth * AdvanceViewportFraction);
        return next > currentOffset + 0.5 ? next : null;
    }
}
