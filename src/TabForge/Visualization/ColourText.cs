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
        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is not Color parsed) return false;
            colour = parsed;
            return true;
        }
        catch (FormatException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>Strict "#RRGGBB" / "#AARRGGBB" (the leading '#' is optional), as typed in colour fields.</summary>
    public static bool TryParseHex(string? text, out Color colour)
    {
        colour = Colors.Transparent;
        var value = (text ?? "").Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        return value.Length is 7 or 9 && TryParse(value, out colour);
    }

    /// <summary>A colour from settings: must also pass <see cref="Services.SettingsColor.IsValid"/>.</summary>
    public static bool TryParseSetting(string? text, out Color colour)
    {
        colour = Colors.Transparent;
        return Services.SettingsColor.IsValid(text) && TryParse(text, out colour);
    }

    public static Color ParseOr(string? text, Color fallback) => TryParse(text, out var colour) ? colour : fallback;
}
