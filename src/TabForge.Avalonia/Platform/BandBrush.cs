using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

// Owns: turning a theme palette key into a solid brush for the Band view's panels and pills, in the element's current theme variant.
// Does not own: which key a part uses (its owner names it) or the palette itself (AvaloniaPalette).
// Tests: TabForge.Avalonia.Tests.BandViewTests.
internal static class BandBrush
{
    /// <summary>A solid brush of the palette colour <paramref name="paletteKey"/> in the element's theme variant.</summary>
    public static SolidColorBrush Of(StyledElement element, string paletteKey)
    {
        var light = element.ActualThemeVariant == ThemeVariant.Light;
        return new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(paletteKey, light)));
    }
}
