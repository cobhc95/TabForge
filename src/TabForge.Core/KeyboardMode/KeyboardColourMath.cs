using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.KeyboardMode;

// Owns: the choice of ink (light or dark) that reads better on a keyboard key's ground.
// Does not own: the colour arithmetic (ThemeColours.Blend, ThemeColours.Contrast), which colours are used (the palette keys) or drawing.
// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests.
public static class KeyboardColourMath
{
    /// <summary>The ink (<paramref name="light"/> or <paramref name="dark"/>) with the higher contrast on <paramref name="ground"/>.</summary>
    public static UiColor TextOn(UiColor ground, UiColor light, UiColor dark) => ThemeColours.Contrast(light, ground) >= ThemeColours.Contrast(dark, ground) ? light : dark;
}
