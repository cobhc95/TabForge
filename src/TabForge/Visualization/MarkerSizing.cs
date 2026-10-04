namespace TabForge.Visualization;

/// <summary>Owns: the effective size factor of fretboard note markers for the "Note marker size" setting.
/// Does not own: drawing or the setting store. Tests: TestFretMarkerSize.</summary>
public static class MarkerSizing
{
    public const int MinPercent = 60;
    public const int MaxPercent = 160;
    public const int DefaultPercent = 100;

    /// <summary>Percent snapped to 10% steps inside the allowed range.</summary>
    public static int Normalise(int percent) => Math.Clamp((int)(Math.Round(percent / 10.0) * 10), MinPercent, MaxPercent);

    public static double Scale(int percent) => Normalise(percent) / 100.0;

    /// <summary>Factor applied to both the bubble radius and its number. Shrinking is never limited; growing stops once the
    /// bubble would be wider than the gap between adjacent strings (never below the unscaled size).</summary>
    public static double Factor(double scale, double baseRadius, double stringGap)
    {
        if (scale <= 1 || baseRadius <= 0) return scale;
        var limit = Math.Max(baseRadius, stringGap / 2 - 0.5);
        return Math.Min(scale, limit / baseRadius);
    }
}
