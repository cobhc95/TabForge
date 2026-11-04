// Owns: colour conversion between RGBA and the picker sliders (hue, saturation, brightness).
// Does not own: the picker's state (ColourPickerPresenter) or its drawing (ColourSurfaces).
// Tests: no dedicated test class.
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>RGBA to and from the colour picker's slider positions.</summary>
public static class ColourHsv
{
    /// <summary>The slider positions for a colour: hue 0 to 360, saturation and brightness 0 to 100.</summary>
    public static (double Hue, double SaturationPercent, double BrightnessPercent) SliderValues(Rgba colour)
    {
        var (hue, saturation, value) = ToHsv(colour);
        return (hue, saturation * 100, value * 100);
    }

    /// <summary>The opaque colour at slider positions: hue 0 to 359, saturation and brightness 0 to 100.</summary>
    public static Rgba ColourFromSliders(double hue, double saturationPercent, double brightnessPercent) =>
        FromHsv(hue, saturationPercent / 100, brightnessPercent / 100);

    private static (double Hue, double Saturation, double Value) ToHsv(Rgba colour)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var hue = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : d / max, max);
    }

    private static Rgba FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;
        var (r, g, b) = hue < 60 ? (c, x, 0.0) : hue < 120 ? (x, c, 0.0) : hue < 180 ? (0.0, c, x)
            : hue < 240 ? (0.0, x, c) : hue < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return new Rgba((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
