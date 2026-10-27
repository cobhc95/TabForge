// Owns: the one density and UI-scale interpretation every metric entry point shares: the Comfortable, Compact and Spacious
//   factors and the 0.8 to 1.5 UI-scale clamp.
// Does not own: the density setting (AppearanceSettings), the sizes the factors scale, or applying the scale to a window.
// Tests: UiTokensTests, UiStrokesTests, UiMetricsTests.
using System;
using TabForge.Services;

namespace TabForge.Platform.Theme;

/// <summary>The density factors one named density applies to bars, rows, pads and strokes. Comfortable is the reference.</summary>
public readonly record struct DensityFactors(double Bar, double Row, double Pad, double Stroke);

/// <summary>
/// The one density and UI-scale interpretation every metric entry point shares. Compact values are new constants owned here;
//  Comfortable is 1.0 (today's exact output) and Spacious scales bars, rows and pads by 1.2. Wraps AppearanceSettings Density and UiScale; ThemeSettingsBinding delegates its clamp here.
// </summary>
public static class UiDensity
{
    /// <summary>The UI scale when none is set.</summary>
    public const double DefaultScale = 1.0;

    /// <summary>The smallest UI scale the settings allow.</summary>
    public const double MinScale = 0.8;

    /// <summary>The largest UI scale the settings allow.</summary>
    public const double MaxScale = 1.5;

    /// <summary>The clamped UI scale, the one scale mechanism shared by every entry point. A non-finite scale means the default.</summary>
    public static double ClampScale(double uiScale) =>
        Math.Clamp(double.IsFinite(uiScale) ? uiScale : DefaultScale, MinScale, MaxScale);

    /// <summary>The factors for the named density. Anything that is not Compact or Spacious reads as Comfortable.</summary>
    public static DensityFactors Of(AppearanceSettings appearance)
    {
        if (appearance is null) throw new ArgumentNullException(nameof(appearance));
        if (string.Equals(appearance.Density, "ExtraCompact", StringComparison.OrdinalIgnoreCase))
            return new DensityFactors(0.70, 0.70, 0.70, 0.9);
        if (string.Equals(appearance.Density, "Compact", StringComparison.OrdinalIgnoreCase))
            return new DensityFactors(0.85, 0.85, 0.85, 0.9);
        if (string.Equals(appearance.Density, "Spacious", StringComparison.OrdinalIgnoreCase))
            return new DensityFactors(1.2, 1.2, 1.2, 1.0);
        return new DensityFactors(1.0, 1.0, 1.0, 1.0);
    }
}
