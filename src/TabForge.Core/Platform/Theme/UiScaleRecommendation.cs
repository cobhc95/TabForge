// Owns: the interface size recommended for one monitor (from its working area in DIPs and its display scaling) and the text that explains it.
// Does not own: reading the screens (MonitorUiScale), when the recommendation is applied, or the stored size (AppearanceSettings.UiScale).
// Tests: UiScaleRecommendationTests.
using System;
using System.Globalization;

namespace TabForge.Platform.Theme;

/// <summary>What a recommendation saw on the monitor and the size it picked.</summary>
public readonly record struct UiScaleAdvice(double Scale, double WorkWidth, double WorkHeight, double DisplayScaling)
{
    /// <summary>"0.80×": the size as the Interface size stepper shows it.</summary>
    public string ScaleText => Scale.ToString("0.00", CultureInfo.InvariantCulture) + "×";

    /// <summary>"1536 × 824 working area at 125% display scaling": what the size was picked from.</summary>
    public string Detail => string.Create(CultureInfo.InvariantCulture,
        $"{Math.Round(WorkWidth):0} × {Math.Round(WorkHeight):0} working area at {Math.Round(DisplayScaling * 100):0}% display scaling");
}

/// <summary>
/// The interface size that fits a monitor, using a 1600 × 950 DIP reference working area:
/// a smaller working area shrinks the interface until that design fits (never below <see cref="UiDensity.MinScale"/>); a larger one adds half
/// of the extra room before the multiplier, unless the display scaling is already 150% or more (the OS has made everything bigger, so the extra DIPs are left as room).
/// A uniform 0.95 multiplier leaves a little headroom on every monitor, independent of resolution or display scaling.
/// The result snaps down to the stepper's 0.05 step and never exceeds 1.0; manual settings may still reach <see cref="UiDensity.MaxScale"/>.
/// </summary>
public static class UiScaleRecommendation
{
    /// <summary>The Settings row with the "use the recommended size" button.</summary>
    public const string RowKey = "appearance.uiscale.recommended";

    /// <summary>The working area, in DIPs, the layout is designed for.</summary>
    public const double ReferenceWidth = UiDensity.RecommendationReferenceWidth, ReferenceHeight = UiDensity.RecommendationReferenceHeight;

    /// <summary>The step the recommendation snaps to (the Interface size stepper's step).</summary>
    public const double Step = 0.05;

    /// <summary>The largest automatic recommendation; the manual Interface size range is unchanged.</summary>
    public const double MaxRecommendedScale = 1.0;

    /// <summary>Display scaling from which a larger working area no longer grows the interface.</summary>
    public const double OsEnlargedScaling = 1.5;

    /// <summary>The recommendation for a working area of <paramref name="workWidth"/> × <paramref name="workHeight"/> DIPs at <paramref name="displayScaling"/> (1 = 100%).</summary>
    public static UiScaleAdvice For(double workWidth, double workHeight, double displayScaling)
    {
        var scaling = double.IsFinite(displayScaling) && displayScaling > 0 ? displayScaling : 1.0;
        if (!(double.IsFinite(workWidth) && workWidth > 0 && double.IsFinite(workHeight) && workHeight > 0))
            return new UiScaleAdvice(UiDensity.DefaultScale, workWidth, workHeight, scaling);
        var fit = Math.Min(workWidth / ReferenceWidth, workHeight / ReferenceHeight);
        var scale = fit <= 1 ? fit : scaling >= OsEnlargedScaling ? 1.0 : 1 + (fit - 1) / 2;
        scale *= 0.95;
        // Snap down (with a hair of tolerance, so 0.8 computed as 0.79999 stays 0.8), then clamp only the recommendation to 0.7–1.0.
        scale = Math.Floor(scale / Step + 1e-6) * Step;
        return new UiScaleAdvice(Math.Round(Math.Clamp(scale, UiDensity.MinScale, MaxRecommendedScale), 2), workWidth, workHeight, scaling);
    }
}
