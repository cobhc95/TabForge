using System.Windows.Media;

namespace TabForge.KeyboardMode;

// Owns: the colours of the pane in the dark and the light theme (panel, strip, text), the keyboard view's colour set per theme and the contrast helpers.
// Does not own: drawing, or which theme is on (the pane asks the theme and picks).
// Tests: TestKeyboardModeKeyView (contrast of every text colour on its ground).
public sealed class KeyboardModePalette
{
    private KeyboardModePalette(bool dark, Color panel, Color strip, Color hitLine, Color label)
    {
        Dark = dark;
        Panel = Freeze(panel); Strip = Freeze(strip); HitLine = Freeze(hitLine); Label = Freeze(label);
        PanelColour = panel; LabelColour = label; StripColour = strip;
    }

    public bool Dark { get; }
    /// <summary>The colours of the keyboard falling-notes view for this theme.</summary>
    public KeyboardColours Keyboard => KeyboardColours.For(Dark);
    public SolidColorBrush Panel { get; }
    public SolidColorBrush Strip { get; }
    public SolidColorBrush HitLine { get; }
    /// <summary>Text on the panel (the hint, the score panel).</summary>
    public SolidColorBrush Label { get; }
    public Color PanelColour { get; }
    public Color StripColour { get; }
    public Color LabelColour { get; }

    public static KeyboardModePalette For(bool dark) => dark ? DarkSet : LightSet;

    private static readonly KeyboardModePalette DarkSet = new(true, panel: Rgb(0x0E, 0x15, 0x26), strip: Rgb(0x07, 0x0B, 0x16), hitLine: Rgb(0xFF, 0x8A, 0x1F), label: Rgb(0xDC, 0xE4, 0xF4));
    private static readonly KeyboardModePalette LightSet = new(false, panel: Rgb(0xEE, 0xF2, 0xF9), strip: Rgb(0xD3, 0xDC, 0xEC), hitLine: Rgb(0xD9, 0x68, 0x0A), label: Rgb(0x2B, 0x36, 0x50));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static SolidColorBrush Freeze(Color c) { var brush = new SolidColorBrush(c); brush.Freeze(); return brush; }

    /// <summary>Black or white, whichever reads better on <paramref name="ground"/>.</summary>
    public static Color TextOn(Color ground) => Contrast(Colors.White, ground) >= Contrast(Rgb(0x0B, 0x12, 0x20), ground) ? Colors.White : Rgb(0x0B, 0x12, 0x20);

    /// <summary>WCAG contrast ratio of two colours (1 to 21).</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
