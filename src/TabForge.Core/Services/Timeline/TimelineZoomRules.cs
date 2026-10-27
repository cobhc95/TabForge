namespace TabForge.Services;

// Owns: the arrangement timeline's horizontal zoom: the allowed factors, the default, the clamp and the next step in or out.
// Does not own: drawing or scrolling the timeline (TimelineView, ArrangementView), the saved value (TimelineSettings.ZoomFactor).
// Tests: TabForge.Avalonia.Tests.TimelineZoomTests.
public static class TimelineZoomRules
{
    /// <summary>The zoom factors the timeline steps through, ascending (1 = the bar width the timeline had before it could zoom).</summary>
    public static readonly IReadOnlyList<double> Steps = new[] { 0.25, 0.4, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0 };

    /// <summary>The factor a timeline starts at.</summary>
    public const double DefaultFactor = 1.0;

    public static double MinFactor => Steps[0];
    public static double MaxFactor => Steps[^1];

    /// <summary>The factor kept inside the allowed range; a non-finite value gives the default.</summary>
    public static double Clamp(double factor) => double.IsFinite(factor) ? Math.Clamp(factor, MinFactor, MaxFactor) : DefaultFactor;

    /// <summary>The next step in (direction above 0) or out (direction below 0) from <paramref name="current"/>; at an end it stays there.</summary>
    public static double Step(double current, int direction)
    {
        var factor = Clamp(current);
        if (direction > 0) return Steps.FirstOrDefault(s => s > factor + 1e-9, MaxFactor);
        if (direction < 0) return Steps.LastOrDefault(s => s < factor - 1e-9, MinFactor);
        return factor;
    }
}
