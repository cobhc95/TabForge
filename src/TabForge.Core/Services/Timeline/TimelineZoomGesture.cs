namespace TabForge.Services;

// Owns: the maths of a continuous timeline zoom (mouse wheel or pinch): the next factor, clamped to the allowed range, and the scroll offset that keeps the point under the pointer in place.
// Does not own: the discrete steps of the buttons and hotkeys (TimelineZoomRules), laying the bars out or scrolling (TimelineView, ArrangementView) or when a gesture has settled (ArrangementView's timer).
// Tests: TabForge.Avalonia.Tests.TimelineZoomPerfTests.
public sealed class TimelineZoomGesture
{
    /// <summary>The factor one wheel notch multiplies the zoom by (a half notch from a touchpad takes the square root).</summary>
    public double NotchRatio { get; init; } = 1.15;

    /// <summary>The factor after <paramref name="notches"/> wheel notches (positive zooms in) from <paramref name="current"/>, kept inside the allowed range.</summary>
    public double Wheel(double current, double notches) =>
        TimelineZoomRules.Clamp(TimelineZoomRules.Clamp(current) * Math.Pow(NotchRatio, double.IsFinite(notches) ? notches : 0));

    /// <summary>The factor after a pinch whose finger spread changed by <paramref name="scale"/> (1 = unchanged), kept inside the allowed range.</summary>
    public double Pinch(double current, double scale) =>
        TimelineZoomRules.Clamp(TimelineZoomRules.Clamp(current) * (double.IsFinite(scale) && scale > 0 ? scale : 1));

    /// <summary>The scroll offset after the content width went from <paramref name="oldWidth"/> to <paramref name="newWidth"/> that keeps the content point at <paramref name="anchor"/> (the pointer's x in the viewport) under it; never negative.</summary>
    public double AnchoredOffset(double offset, double anchor, double oldWidth, double newWidth)
    {
        if (oldWidth <= 0) return Math.Max(0, offset);
        return Math.Max(0, (offset + anchor) / oldWidth * newWidth - anchor);
    }
}
