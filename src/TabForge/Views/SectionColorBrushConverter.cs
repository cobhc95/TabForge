using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>
/// Sidebar section-list colours: the timeline hue (tamed like the timeline), as a translucent tint.
/// ConverterParameter is the alpha (0-255); "dim:ALPHA" also desaturates and darkens it for inactive rows.
/// </summary>
public sealed class SectionColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = parameter?.ToString() ?? "";
        var dim = text.StartsWith("dim:", StringComparison.Ordinal);
        if (dim) text = text[4..];
        var alpha = byte.TryParse(text, out var parsedAlpha) ? parsedAlpha : (byte)34;
        var colour = SectionColorValueConverter.SectionColour(value);
        if (dim)
        {
            var grey = colour.R * 0.3 + colour.G * 0.59 + colour.B * 0.11;
            byte Mix(byte v) => (byte)Math.Round((v * 0.55 + grey * 0.45) * 0.8);
            colour = Color.FromRgb(Mix(colour.R), Mix(colour.G), Mix(colour.B));
        }
        var brush = new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>The section colour itself (tamed like the timeline), e.g. for glows.</summary>
public sealed class SectionColorValueConverter : IValueConverter
{
    private static readonly Color DefaultSectionColour = Color.FromRgb(0x2E, 0x74, 0xB5);

    // Bound to the section itself: its colour is the one the timeline resolved (shared per section type).
    internal static Color SectionColour(object value) => value switch
    {
        MarkerModel marker => SectionColours.DisplayFor(marker)
                              ?? (ColourText.TryParse(marker.ColorHex, out var own) ? Draw.Tame(own) : DefaultSectionColour),
        string hex when ColourText.TryParse(hex, out var colour) => Draw.Tame(colour),
        _ => DefaultSectionColour,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => SectionColour(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
