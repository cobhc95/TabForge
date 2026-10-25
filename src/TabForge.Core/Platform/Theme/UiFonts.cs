// Owns: the shared font entry point: the interface, score and mono typefaces from the appearance and the score style.
// Does not own: the appearance values (AppearanceSettings) or the configured score style (ScoreText).
// Tests: UiFontsTests.
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Platform.Theme;

/// <summary>A typeface: the family, the size in points, the OpenType weight and the italic flag.</summary>
public readonly record struct UiFont(string Family, double Size, int Weight = 400, bool Italic = false);

/// <summary>
/// The shared font entry point. The interface and icon sizes read the appearance live; the score style reads the configured
/// ScoreText; the mono family is the one fixed tab-digits family, owned here so every site names it. Nothing in the app calls it yet.
/// </summary>
public static class UiFonts
{
    /// <summary>The interface typeface from the appearance.</summary>
    public static UiFont Ui(AppearanceSettings appearance) =>
        new UiFont(appearance.FontFamily, appearance.FontSize);

    /// <summary>The configured score typeface: the ScoreText family, size, bold and italic. ScoreText keeps its own clamp and per-area scale.</summary>
    public static UiFont Score() =>
        new UiFont(ScoreText.FontFamily, ScoreText.TextSize, ScoreText.TextBold ? 700 : 400, ScoreText.TextItalic);

    /// <summary>The one fixed-width family for tab digits and log text. It is not a setting today.</summary>
    public static string MonoFamily => "Consolas";

    /// <summary>The icon glyph size from the appearance, in device-independent pixels.</summary>
    public static double IconSize(AppearanceSettings appearance) => appearance.IconSize;

    /// <summary>The toolbar icon size in DIP when the appearance gives none (14).</summary>
    public const double DefaultIconSize = 14;

    /// <summary>The appearance's icon size kept in 10..22 DIP (the default when it is not a number); the app stores it once per Apply.</summary>
    public static double ClampedIconSize(AppearanceSettings appearance) =>
        double.IsFinite(appearance.IconSize) ? Math.Clamp(appearance.IconSize, 10, 22) : DefaultIconSize;
}
