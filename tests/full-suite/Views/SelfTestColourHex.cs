using System.Windows.Media;
using TabForge.Models;
using TabForge.Visualization;

namespace TabForge;

/// <summary>Colour text: the framework-neutral parser and the UI adapter agree with the UI framework's own converter.</summary>
public static partial class SelfTest
{
    private static (bool Ok, byte A, byte R, byte G, byte B) ReferenceColourParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, 0, 0, 0, 0);
        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is not Color c) return (false, 0, 0, 0, 0);
            return (true, c.A, c.R, c.G, c.B);
        }
        catch (FormatException) { return (false, 0, 0, 0, 0); }
        catch (NotSupportedException) { return (false, 0, 0, 0, 0); }
        catch (InvalidOperationException) { return (false, 0, 0, 0, 0); }
    }

    private static void TestColourHexEquivalence()
    {
        var inputs = new List<string?>
        {
            null, "", " ", "\t", "#", "##", "#1", "#12", "#123", "#1234", "#12345", "#123456", "#1234567", "#12345678", "#123456789",
            "#abc", "#ABC", "#aBc", "#abcd", "#ABCD", "#abcdef", "#ABCDEF", "#AbCdEf", "#80FF0000", "#80ff0000", "#00000000", "#FFFFFFFF",
            "#000", "#FFF", "#000000", "#FFFFFF", "#F61A16", "#2E74B5", "#3fb950", "  #3FB950  ", "\t#3FB950\r\n", "# 3FB950", "#3F B950",
            "#GGGGGG", "#12G", "#12345G", "#-12345", "#+12345", "3FB950", "FFF", "0x3FB950", "0xFFFFFF", "#0x3FB950",
            "red", "Red", "RED", " red ", "CornflowerBlue", "cornflowerblue", "SteelBlue", "Transparent", "transparent", "Gray", "Grey", "DarkGoldenrod",
            "ActiveBorder", "NotAColour", "red blue", "sc#1,0.5,0.25,0", "sc#0.5,1,0.5,0.25", "sc#1,2,3,4", "sc#1,0.5", "ScRGB", "none", "null", "#ff", "#fffff", "#fffffff",
            "rgb(1,2,3)", "1,2,3", "é", "#ééé", "#٣٣٣",
        };
        for (var i = 0; i < 256; i++) inputs.Add($"#{i:X2}{(255 - i):X2}{(i * 7 % 256):X2}");

        int mismatches = 0;
        string? firstMismatch = null;
        void Mismatch(string what, string? input, string expected, string actual)
        {
            mismatches++;
            firstMismatch ??= $"{what} '{input}': reference {expected}, new {actual}";
        }

        foreach (var input in inputs)
        {
            var reference = ReferenceColourParse(input);
            var adapted = ColourText.TryParse(input, out var c);
            var adaptedText = adapted ? $"{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : "fail";
            var referenceText = reference.Ok ? $"{reference.A:X2}{reference.R:X2}{reference.G:X2}{reference.B:X2}" : "fail";
            if (adaptedText != referenceText) Mismatch("ColourText.TryParse", input, referenceText, adaptedText);

            // The framework-neutral parser: whenever it accepts, the framework agrees on every channel; hex digit forms are all accepted.
            var neutral = ColourHex.TryParse(input, out var rgba);
            if (neutral && (!reference.Ok || (rgba.A, rgba.R, rgba.G, rgba.B) != (reference.A, reference.R, reference.G, reference.B)))
                Mismatch("ColourHex.TryParse", input, referenceText, $"{rgba.A:X2}{rgba.R:X2}{rgba.G:X2}{rgba.B:X2}");
            var trimmed = (input ?? "").Trim();
            var isHexForm = trimmed.Length is 4 or 5 or 7 or 9 && trimmed[0] == '#' && trimmed.Skip(1).All(Uri.IsHexDigit);
            if (isHexForm != neutral) Mismatch("ColourHex hex-form coverage", input, isHexForm.ToString(), neutral.ToString());

            // Strict typed-field parser: "#" optional, 7 or 9 characters, then whatever the framework says.
            var oldStrict = StrictReference(input);
            var strict = ColourText.TryParseHex(input, out var sc);
            var strictText = strict ? $"{sc.A:X2}{sc.R:X2}{sc.G:X2}{sc.B:X2}" : "fail";
            if (strictText != oldStrict) Mismatch("ColourText.TryParseHex", input, oldStrict, strictText);
        }

        // Formatting: "#RRGGBB", "#AARRGGBB", automatic alpha.
        foreach (var (a, r, g, b) in new[] { ((byte)255, (byte)0, (byte)0, (byte)0), ((byte)255, (byte)255, (byte)255, (byte)255), ((byte)255, (byte)0x3F, (byte)0xB9, (byte)0x50), ((byte)0x80, (byte)1, (byte)2, (byte)3), ((byte)0, (byte)0xFF, (byte)0, (byte)0xAB) })
        {
            var colour = Color.FromArgb(a, r, g, b);
            var opaque = $"#{r:X2}{g:X2}{b:X2}";
            var withAlpha = $"#{a:X2}{r:X2}{g:X2}{b:X2}";
            if (ColourText.Hex(colour) != opaque) Mismatch("ColourText.Hex", opaque, opaque, ColourText.Hex(colour));
            if (ColourText.Hex(colour, true) != withAlpha) Mismatch("ColourText.Hex(alpha)", withAlpha, withAlpha, ColourText.Hex(colour, true));
            if (ColourText.HexAuto(colour) != (a == 255 ? opaque : withAlpha)) Mismatch("ColourText.HexAuto", withAlpha, a == 255 ? opaque : withAlpha, ColourText.HexAuto(colour));
            var rgba = new Rgba(r, g, b, a);
            if (ColourHex.Format(rgba) != opaque || ColourHex.Format(rgba, true) != withAlpha || ColourHex.FormatAuto(rgba) != (a == 255 ? opaque : withAlpha))
                Mismatch("ColourHex.Format", withAlpha, withAlpha, ColourHex.Format(rgba, true));
            // Round trip through the parser.
            if (!ColourHex.TryParse(withAlpha, out var back) || back != rgba) Mismatch("ColourHex round trip", withAlpha, withAlpha, "differs");
        }

        Check("colour text: neutral and UI parsers/formatters agree with the framework's converter over every accepted form",
            mismatches == 0, $"{inputs.Count} inputs, {mismatches} mismatches; first: {firstMismatch}");
    }

    private static string StrictReference(string? text)
    {
        var value = (text ?? "").Trim();
        if (!value.StartsWith('#')) value = "#" + value;
        if (value.Length is not (7 or 9)) return "fail";
        var r = ReferenceColourParse(value);
        return r.Ok ? $"{r.A:X2}{r.R:X2}{r.G:X2}{r.B:X2}" : "fail";
    }
}
