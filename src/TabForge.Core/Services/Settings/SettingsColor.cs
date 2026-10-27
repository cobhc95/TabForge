using System.Globalization;

namespace TabForge.Services;

// Owns: parsing and formatting the colour values stored in settings.
// Does not own: applying the colours (AvaloniaPalette).
// Tests: no dedicated test class.
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

        return KnownColorNames.Contains(colorText);
    }

    // The known and system colour names (the .NET KnownColor set), matched case-insensitively. Immutable; any thread.
    private static readonly HashSet<string> KnownColorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ActiveBorder", "ActiveCaption", "ActiveCaptionText", "AppWorkspace", "Control", "ControlDark", "ControlDarkDark", "ControlLight",
        "ControlLightLight", "ControlText", "Desktop", "GrayText", "Highlight", "HighlightText", "HotTrack", "InactiveBorder",
        "InactiveCaption", "InactiveCaptionText", "Info", "InfoText", "Menu", "MenuText", "ScrollBar", "Window",
        "WindowFrame", "WindowText", "Transparent", "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure",
        "Beige", "Bisque", "Black", "BlanchedAlmond", "Blue", "BlueViolet", "Brown", "BurlyWood",
        "CadetBlue", "Chartreuse", "Chocolate", "Coral", "CornflowerBlue", "Cornsilk", "Crimson", "Cyan",
        "DarkBlue", "DarkCyan", "DarkGoldenrod", "DarkGray", "DarkGreen", "DarkKhaki", "DarkMagenta", "DarkOliveGreen",
        "DarkOrange", "DarkOrchid", "DarkRed", "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue", "DarkSlateGray", "DarkTurquoise",
        "DarkViolet", "DeepPink", "DeepSkyBlue", "DimGray", "DodgerBlue", "Firebrick", "FloralWhite", "ForestGreen",
        "Fuchsia", "Gainsboro", "GhostWhite", "Gold", "Goldenrod", "Gray", "Green", "GreenYellow",
        "Honeydew", "HotPink", "IndianRed", "Indigo", "Ivory", "Khaki", "Lavender", "LavenderBlush",
        "LawnGreen", "LemonChiffon", "LightBlue", "LightCoral", "LightCyan", "LightGoldenrodYellow", "LightGray", "LightGreen",
        "LightPink", "LightSalmon", "LightSeaGreen", "LightSkyBlue", "LightSlateGray", "LightSteelBlue", "LightYellow", "Lime",
        "LimeGreen", "Linen", "Magenta", "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid", "MediumPurple",
        "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen", "MediumTurquoise", "MediumVioletRed", "MidnightBlue", "MintCream", "MistyRose",
        "Moccasin", "NavajoWhite", "Navy", "OldLace", "Olive", "OliveDrab", "Orange", "OrangeRed",
        "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise", "PaleVioletRed", "PapayaWhip", "PeachPuff", "Peru",
        "Pink", "Plum", "PowderBlue", "Purple", "Red", "RosyBrown", "RoyalBlue", "SaddleBrown",
        "Salmon", "SandyBrown", "SeaGreen", "SeaShell", "Sienna", "Silver", "SkyBlue", "SlateBlue",
        "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan", "Teal", "Thistle", "Tomato",
        "Turquoise", "Violet", "Wheat", "White", "WhiteSmoke", "Yellow", "YellowGreen", "ButtonFace",
        "ButtonHighlight", "ButtonShadow", "GradientActiveCaption", "GradientInactiveCaption", "MenuBar", "MenuHighlight", "RebeccaPurple",
    };
}
