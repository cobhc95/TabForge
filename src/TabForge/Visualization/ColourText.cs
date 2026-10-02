using System.Windows.Media;

namespace TabForge.Visualization;

/// <summary>
/// The one place colour text is parsed (settings, themes, track/section colours, SVG icons).
/// Accepts anything WPF understands ("#RGB", "#RRGGBB", "#AARRGGBB", named colours) and never throws.
/// </summary>
public static class ColourText
{
    public static bool TryParse(string? text, out Color colour)
    {
        colour = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (TabForge.Models.ColourHex.TryParse(text, out var rgba))
        {
            colour = Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
            return true;
        }
        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is not Color parsed) return false;
            colour = parsed;
            return true;
        }
        catch (FormatException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>A brush of the colour text, or <paramref name="fallback"/> (default gray) when it does not parse.</summary>
    public static Brush BrushOr(string? text, Brush? fallback = null) =>
        TryParse(text, out var colour) ? new SolidColorBrush(colour) : fallback ?? Brushes.Gray;

    /// <summary>Strict "#RRGGBB" / "#AARRGGBB" (the leading '#' is optional), as typed in colour fields.</summary>
    public static bool TryParseHex(string? text, out Color colour)
    {
        colour = Colors.Transparent;
        if (!TabForge.Models.ColourHex.TryParseStrict(text, out var rgba)) return false;
        colour = Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
        return true;
    }

    /// <summary>A colour from settings: must also pass <see cref="Services.SettingsColor.IsValid"/>.</summary>
    public static bool TryParseSetting(string? text, out Color colour)
    {
        colour = Colors.Transparent;
        return Services.SettingsColor.IsValid(text) && TryParse(text, out colour);
    }

    /// <summary>"#RRGGBB", or "#AARRGGBB" when <paramref name="includeAlpha"/> is set.</summary>
    public static string Hex(Color colour, bool includeAlpha = false) =>
        TabForge.Models.ColourHex.Format(ToRgba(colour), includeAlpha);

    /// <summary>"#RRGGBB" when opaque, otherwise "#AARRGGBB".</summary>
    public static string HexAuto(Color colour) => TabForge.Models.ColourHex.FormatAuto(ToRgba(colour));

    public static TabForge.Models.Rgba ToRgba(Color colour) => new(colour.R, colour.G, colour.B, colour.A);

    public static Color ParseOr(string? text, Color fallback) => TryParse(text, out var colour) ? colour : fallback;
}
