using System.Drawing;
using System.Globalization;

namespace TabForge.Services;

/// <summary>Framework-neutral validation for color values persisted in application settings.</summary>
public static class SettingsColor
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var colorText = value.Trim();
        if (colorText[0] == '#')
        {
            var digits = colorText.AsSpan(1);
            if (digits.Length is not (3 or 4 or 6 or 8)) return false;
            foreach (var digit in digits)
                if (!Uri.IsHexDigit(digit)) return false;
            return true;
        }

        if (colorText.StartsWith("sc#", StringComparison.OrdinalIgnoreCase))
        {
            var channels = colorText[3..].Split(',', StringSplitOptions.TrimEntries);
            return channels.Length == 4 && channels.All(channel =>
                float.TryParse(channel, NumberStyles.Float, CultureInfo.InvariantCulture, out var component) &&
                float.IsFinite(component) && component is >= 0 and <= 1);
        }

        var named = Color.FromName(colorText);
        return named.IsKnownColor || named.IsSystemColor;
    }
}
