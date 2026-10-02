namespace TabForge.Models;

/// <summary>One colour as 8-bit channels; framework-neutral so models, services and file formats can hold colours.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// Hex colour text, without any UI framework: parses "#RGB", "#ARGB", "#RRGGBB" and "#AARRGGBB" (the same digit
/// forms the UI colour parser accepts) and formats "#RRGGBB" or "#AARRGGBB".
/// </summary>
public static class ColourHex
{
    /// <summary>Parses a hex form (surrounding whitespace ignored); false for anything else, including names.</summary>
    public static bool TryParse(string? text, out Rgba colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.AsSpan().Trim();
        if (value[0] != '#') return false;
        var digits = value[1..];
        foreach (var digit in digits)
            if (!Uri.IsHexDigit(digit)) return false;
        switch (digits.Length)
        {
            case 3: colour = new Rgba(Nibble(digits[0]), Nibble(digits[1]), Nibble(digits[2])); return true;
            case 4: colour = new Rgba(Nibble(digits[1]), Nibble(digits[2]), Nibble(digits[3]), Nibble(digits[0])); return true;
            case 6: colour = new Rgba(Pair(digits[0], digits[1]), Pair(digits[2], digits[3]), Pair(digits[4], digits[5])); return true;
            case 8: colour = new Rgba(Pair(digits[2], digits[3]), Pair(digits[4], digits[5]), Pair(digits[6], digits[7]), Pair(digits[0], digits[1])); return true;
            default: return false;
        }
    }

    /// <summary>Strict "#RRGGBB" / "#AARRGGBB" as typed in colour fields; the leading '#' is optional.</summary>
    public static bool TryParseStrict(string? text, out Rgba colour)
    {
        colour = default;
        var value = (text ?? "").Trim();
        if (value.Length > 0 && value[0] != '#') value = "#" + value;
        return value.Length is 7 or 9 && TryParse(value, out colour);
    }

    /// <summary>"#RRGGBB" (alpha dropped).</summary>
    public static string Format(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>"#RRGGBB", or "#AARRGGBB" when <paramref name="includeAlpha"/> is set.</summary>
    public static string Format(Rgba colour, bool includeAlpha = false) =>
        includeAlpha ? $"#{colour.A:X2}{colour.R:X2}{colour.G:X2}{colour.B:X2}" : Format(colour.R, colour.G, colour.B);

    /// <summary>"#RRGGBB" when opaque, otherwise "#AARRGGBB".</summary>
    public static string FormatAuto(Rgba colour) => Format(colour, colour.A != 255);

    private static byte Nibble(char digit) => (byte)(Hex(digit) * 17);

    private static byte Pair(char high, char low) => (byte)((Hex(high) << 4) | Hex(low));

    private static int Hex(char digit) => digit <= '9' ? digit - '0' : (digit | 0x20) - 'a' + 10;
}
