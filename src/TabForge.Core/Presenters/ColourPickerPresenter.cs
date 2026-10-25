// Owns: the colour picker popup's rules without UI types: the ready-made colours, the recent colours (and keeping that list), the colour being
//       chosen as RGBA, whether it can be see-through, the square, hue and opacity positions, the code text with its message, and the text
//       the Use and Default buttons return.
// Does not own: the popup, the square, strip, slider and text box, or where the recent list is stored (the caller passes the list).
// Tests: ColourPickerPopupTests.
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>
/// Colour picker state. <see cref="ChoosePreset"/>, <see cref="TypeCode"/> and <see cref="Apply"/> change the colour; the square, hue and
/// opacity positions and the code text follow from it, and the view copies them to its controls after each change.
/// Thread: UI thread only (it is driven by the picker popup).
/// </summary>
public sealed class ColourPickerPresenter
{
    /// <summary>How many recent colours the picker keeps: the most the settings file allows.</summary>
    public const int RecentLimit = InputLimits.MaxRecentColours;

    /// <summary>How many of the kept recent colours the picker shows.</summary>
    public const int RecentShown = 8;

    /// <summary>How many ready-made colours the picker shows.</summary>
    public const int PresetLimit = 16;

    private readonly IList<string> _recent;

    /// <param name="initialText">The colour the picker opens with; white when it does not read.</param>
    /// <param name="defaultValue">The text the Default button returns, as given.</param>
    /// <param name="recent">The recent colour texts, newest first; <see cref="Remember"/> changes this list.</param>
    public ColourPickerPresenter(string? initialText, string defaultValue, IList<string> recent)
    {
        DefaultValue = defaultValue;
        _recent = recent;
        Presets = NamedColourSwatches.Presets.Take(PresetLimit).Select(preset => (preset.Name, preset.Hex, ColourHex.TryParseStrict(preset.Hex, out var colour) ? colour : default)).ToList();
        SupportsOpacity = HasOpacity(initialText) || HasOpacity(defaultValue);
        Choose(ColourHex.TryParseStrict(initialText, out var initial) ? initial : new Rgba(255, 255, 255));
    }

    /// <summary>The text the Default button returns, as given.</summary>
    public string DefaultValue { get; }

    /// <summary>The ready-made colours, in grid order.</summary>
    public IReadOnlyList<(string Name, string Hex, Rgba Colour)> Presets { get; }

    /// <summary>Whether the colour can be see-through: its start or default text names an opacity (#AARRGGBB).</summary>
    public bool SupportsOpacity { get; }

    /// <summary>The recent colours that read, newest first, each with its text as given (for its tool tip).</summary>
    public IReadOnlyList<(string Text, Rgba Colour)> RecentColours => _recent
        .Select(text => (Text: text, Read: ColourHex.TryParseStrict(text, out var colour), Colour: colour))
        .Where(item => item.Read).Take(RecentShown).Select(item => (item.Text, item.Colour)).ToList();

    /// <summary>The colour being chosen; its opacity is part of it.</summary>
    public Rgba Colour { get; private set; }

    /// <summary>The hue position, 0 to 359.</summary>
    public double Hue { get; private set; }

    /// <summary>The square's saturation position, 0 to 100.</summary>
    public double SaturationPercent { get; private set; }

    /// <summary>The square's brightness position, 0 to 100.</summary>
    public double BrightnessPercent { get; private set; }

    /// <summary>The opacity position, 0 to 100.</summary>
    public double OpacityPercent { get; private set; }

    /// <summary>The code box text: "#RRGGBB", or "#AARRGGBB" when the colour can be see-through.</summary>
    public string CodeText { get; private set; } = "";

    /// <summary>The plain message under the code box after text that is not a colour; null otherwise.</summary>
    public string? CodeError { get; private set; }

    /// <summary>The text the Use colour button returns: "#RRGGBB", or "#AARRGGBB" when the colour is not opaque.</summary>
    public string UseColourText => ColourHex.FormatAuto(Colour);

    /// <summary>Makes a ready-made or recent colour the colour; the square, hue, opacity and code follow it.</summary>
    public void ChoosePreset(string hex)
    {
        if (ColourHex.TryParseStrict(hex, out var colour)) Choose(colour);
    }

    /// <summary>Reads code text typed in the code box. When it reads, the colour is chosen; otherwise nothing changes and <see cref="CodeError"/> says why.</summary>
    public bool TypeCode(string? text)
    {
        if (ColourHex.TryParseStrict(text, out var colour))
        {
            Choose(colour);
            return true;
        }
        CodeError = SupportsOpacity ? "Type a code like #RRGGBB or #AARRGGBB." : "Type a code like #RRGGBB.";
        return false;
    }

    /// <summary>Takes the colour from the square, hue and opacity positions. Opacity counts only when the colour can be see-through.</summary>
    public void Apply(double hue, double saturationPercent, double brightnessPercent, double opacityPercent)
    {
        Hue = hue;
        SaturationPercent = saturationPercent;
        BrightnessPercent = brightnessPercent;
        OpacityPercent = opacityPercent;
        var opaque = ColourChooserPresenter.ColourFromSliders(hue, saturationPercent, brightnessPercent);
        Colour = SupportsOpacity ? new Rgba(opaque.R, opaque.G, opaque.B, (byte)Math.Round(opacityPercent * 255 / 100)) : opaque;
        CodeText = ColourHex.Format(Colour, SupportsOpacity);
        CodeError = null;
    }

    /// <summary>Puts a chosen colour text first in the recent list (once, whatever its case) and keeps the list to <see cref="RecentLimit"/>.</summary>
    public void Remember(string colourText)
    {
        for (var i = _recent.Count - 1; i >= 0; i--)
            if (string.Equals(_recent[i], colourText, StringComparison.OrdinalIgnoreCase)) _recent.RemoveAt(i);
        _recent.Insert(0, colourText);
        while (_recent.Count > RecentLimit) _recent.RemoveAt(_recent.Count - 1);
    }

    private static bool HasOpacity(string? text) => text?.Trim().Length == 9;

    private void Choose(Rgba colour)
    {
        Colour = SupportsOpacity ? colour : new Rgba(colour.R, colour.G, colour.B);
        OpacityPercent = Colour.A * 100.0 / 255;
        (Hue, SaturationPercent, BrightnessPercent) = ColourChooserPresenter.SliderValues(Colour);
        CodeText = ColourHex.Format(Colour, SupportsOpacity);
        CodeError = null;
    }
}
