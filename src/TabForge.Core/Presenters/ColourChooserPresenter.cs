// Owns: the colour chooser's logic without UI types: the colour being chosen as RGBA, the preset rows (with the optional Default row first),
//       which preset row the colour matches, the hex text for the colour, and the slider conversions (hue, saturation, brightness).
// Does not own: the window, the preset dropdown, swatches, sliders and hex box, the live preview, the OK and Cancel result, or toolkit colours.
// Tests: no dedicated test class.
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>
/// Colour chooser state. <see cref="SetColour"/> changes the colour; <see cref="Hex"/> and <see cref="PresetIndex"/> describe it for the
/// text box and the preset dropdown. The slider conversions are static so the view can use them on either side of a drag.
/// Thread: UI thread only (it is driven by the chooser window).
/// </summary>
public sealed class ColourChooserPresenter
{
    private readonly IReadOnlyList<(string Name, string Hex)> _presets;

    /// <param name="initial">The colour the chooser opens with.</param>
    /// <param name="defaultColour">When set, a "Default" row with this colour is listed first.</param>
    /// <param name="presets">The named preset colours, in dropdown order.</param>
    public ColourChooserPresenter(Rgba initial, Rgba? defaultColour, IEnumerable<(string Name, string Hex)> presets)
    {
        var rows = new List<(string Name, string Hex)>();
        if (defaultColour is { } d) rows.Add(("Default", ColourHex.FormatAuto(d)));
        rows.AddRange(presets);
        _presets = rows;
        Colour = initial;
    }

    /// <summary>The preset rows in dropdown order, the Default row first when there is one.</summary>
    public IReadOnlyList<(string Name, string Hex)> Presets => _presets;

    /// <summary>The colour being chosen.</summary>
    public Rgba Colour { get; private set; }

    /// <summary>"#RRGGBB", or "#AARRGGBB" when the colour is not opaque.</summary>
    public string Hex => ColourHex.FormatAuto(Colour);

    /// <summary>The index in <see cref="Presets"/> of the row whose hex equals <see cref="Hex"/> (ignoring case), or -1 when none does.</summary>
    public int PresetIndex
    {
        get
        {
            var hex = Hex;
            for (var i = 0; i < _presets.Count; i++)
            {
                if (string.Equals(_presets[i].Hex, hex, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
    }

    /// <summary>Makes <paramref name="colour"/> the colour being chosen.</summary>
    public void SetColour(Rgba colour) => Colour = colour;

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
