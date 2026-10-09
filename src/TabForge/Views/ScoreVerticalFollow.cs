namespace TabForge.Views;

/// <summary>Threshold-based vertical follow policy for the music-sheet viewport.</summary>
internal static class ScoreVerticalFollow
{
    /// <summary>One time-based glide step toward <paramref name="target"/>: ~95% in 120 ms, landed within ~250 ms at any frame rate.</summary>
    internal static double GlideStep(double current, double target, double dtSeconds)
    {
        var delta = target - current;
        if (Math.Abs(delta) < 4) return target;
        return current + delta * (1 - Math.Exp(-Math.Max(0, dtSeconds) / 0.04));
    }

    /// <summary>
    /// Returns a new vertical offset only when the active system approaches a viewport edge.
    /// The system is placed inside a comfortable band, and offsets are clamped to the actual
    /// scrollable extent. A small viewport prioritizes keeping the system's top visible.
    /// </summary>
    internal static double? NextOffset(double currentOffset, double viewportHeight, double systemTop,
        double systemHeight, double extentHeight, double marginPercent = 20, double triggerPercent = 80)
    {
        if (!double.IsFinite(currentOffset) || !double.IsFinite(viewportHeight) ||
            !double.IsFinite(systemTop) || !double.IsFinite(systemHeight) ||
            !double.IsFinite(extentHeight) || viewportHeight <= 1 || systemHeight <= 0)
            return null;

        var maximumOffset = Math.Max(0, extentHeight - viewportHeight);
        currentOffset = Math.Clamp(currentOffset, 0, maximumOffset);
        systemTop = Math.Max(0, systemTop);

        var preferredMargin = viewportHeight * Math.Clamp(marginPercent, 0, 45) / 100.0;
        var minimumMargin = Math.Min(viewportHeight * 0.08, Math.Max(8, systemHeight * 0.12));
        var topMargin = Math.Min(viewportHeight * 0.45,
            Math.Max(preferredMargin, Math.Max(minimumMargin, systemHeight * 0.18)));
        var bottomMargin = Math.Min(viewportHeight * 0.45,
            Math.Max(preferredMargin, Math.Max(minimumMargin, systemHeight * 0.22)));
        var triggerY = currentOffset + viewportHeight * Math.Clamp(triggerPercent, 40, 95) / 100.0;
        double target;

        // A backward seek or playback reversal has moved the active system above the viewport.
        if (systemTop < currentOffset + topMargin - 0.5)
        {
            target = systemTop - topMargin;
        }
        else
        {
            var systemBottom = systemTop + systemHeight;
            if (systemBottom <= triggerY + 0.5) return null;

            // If the viewport cannot contain a full system, keep its beginning in view; otherwise
            // advance just enough to expose the complete row below the comfortable lower margin.
            target = systemHeight + topMargin + bottomMargin >= viewportHeight
                ? systemTop - topMargin
                : systemBottom - viewportHeight * Math.Clamp(triggerPercent, 40, 95) / 100.0;
        }

        target = Math.Clamp(target, 0, maximumOffset);
        return Math.Abs(target - currentOffset) > 0.5 ? target : null;
    }
}
