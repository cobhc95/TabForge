// Owns: the section colour rules shared by the timeline and the section lists, on plain RGBA: a section's display colour (tamed unless
//       chosen exactly), one colour for a section family ("Verse 1", "Verse 2"), a distinct colour for a family that looks like one
//       already used, the tame maths and the look-alike test, and the family name of a title.
// Does not own: the marker storage, the resolved-colour cache (WPF SectionColours keeps it), palette lookup (callers pass the distinct
//       colours, named by DistinctPaletteNames), drawing. Colour text is hex only (ColourHex), as the markers store it.
// Tests: SectionColourPresenterTests.
using System.Text.RegularExpressions;
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>Section colour rules. Pure and allocation-light: the per-frame callers stay cheap. Thread: any; holds no state.</summary>
public static class SectionColourPresenter
{
    /// <summary>Palette keys of the well-separated colours handed out when section families would collide, in hand-out order.</summary>
    public static IReadOnlyList<string> DistinctPaletteNames { get; } =
        Enumerable.Range(1, 14).Select(number => "SectionColour" + number).ToArray();

    /// <summary>Display colour per marker, index-aligned with <paramref name="sortedMarkers"/> (markers in bar order).</summary>
    public static Rgba[] Resolve(IReadOnlyList<MarkerModel> sortedMarkers, bool matchSimilar, Rgba fallback, IReadOnlyList<Rgba> distinct)
    {
        var result = new Rgba[sortedMarkers.Count];
        var familyColours = new Dictionary<string, Rgba>(StringComparer.OrdinalIgnoreCase);
        var explicitFamilyColours = new Dictionary<string, Rgba>(StringComparer.OrdinalIgnoreCase);
        var usedColours = new List<Rgba>();
        if (matchSimilar)
            foreach (var marker in sortedMarkers)
                if (marker.ColorIsExplicit && Family(marker.Title) is { Length: > 0 } family &&
                    !explicitFamilyColours.ContainsKey(family) && ColourHex.TryParse(marker.ColorHex, out var chosen))
                    explicitFamilyColours.Add(family, chosen);

        for (var i = 0; i < sortedMarkers.Count; i++)
        {
            var marker = sortedMarkers[i];
            var colour = OwnColour(marker, fallback);
            var family = matchSimilar ? Family(marker.Title) : "";
            if (family.Length > 0)
            {
                if (familyColours.TryGetValue(family, out var shared)) colour = shared;
                else
                {
                    if (explicitFamilyColours.TryGetValue(family, out var chosen)) colour = chosen;
                    else if (usedColours.Any(used => LooksAlike(used, colour)))
                        colour = distinct.FirstOrDefault(candidate => !usedColours.Any(used => LooksAlike(used, candidate)), colour);
                    familyColours[family] = colour;
                    usedColours.Add(colour);
                }
            }
            result[i] = colour;
        }
        return result;
    }

    /// <summary>A section's own colour before family sharing: its stored colour, tamed unless it was chosen exactly; the fallback if it has none.</summary>
    public static Rgba OwnColour(MarkerModel marker, Rgba fallback) =>
        ColourHex.TryParse(marker.ColorHex, out var parsed) ? marker.ColorIsExplicit ? parsed : Tame(parsed) : fallback;

    /// <summary>
    /// Display version of a user or imported colour: very bright, saturated values are toned down and pure black is lifted slightly, so
    /// blocks stay readable on both themes. The stored colour is unchanged. Lightness is clamped to 0.16 to 0.50, saturation to 0.68.
    /// </summary>
    public static Rgba Tame(Rgba colour)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2; var d = max - min;
        var s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        var nl = Math.Clamp(l, 0.16, 0.50);
        var ns = Math.Min(s, 0.68);
        if (Math.Abs(nl - l) < 0.001 && Math.Abs(ns - s) < 0.001) return colour;
        var cc = (1 - Math.Abs(2 * nl - 1)) * ns; var x = cc * (1 - Math.Abs(h / 60 % 2 - 1)); var m = nl - cc / 2;
        var (rr, gg, bb) = h < 60 ? (cc, x, 0.0) : h < 120 ? (x, cc, 0.0) : h < 180 ? (0.0, cc, x) : h < 240 ? (0.0, x, cc) : h < 300 ? (x, 0.0, cc) : (cc, 0.0, x);
        return new Rgba((byte)Math.Round((rr + m) * 255), (byte)Math.Round((gg + m) * 255), (byte)Math.Round((bb + m) * 255), colour.A);
    }

    /// <summary>True when two colours look alike: low-saturation pairs by lightness, others by hue (under 28 degrees) and lightness (under 0.22).</summary>
    public static bool LooksAlike(Rgba a, Rgba b)
    {
        var (h1, s1, l1) = Hsl(a);
        var (h2, s2, l2) = Hsl(b);
        if (s1 < 0.18 || s2 < 0.18) return s1 < 0.18 && s2 < 0.18 && Math.Abs(l1 - l2) < 0.15;
        var hueGap = Math.Abs(h1 - h2);
        hueGap = Math.Min(hueGap, 360 - hueGap);
        return hueGap < 28 && Math.Abs(l1 - l2) < 0.22;
    }

    /// <summary>A section's family name: the title without trailing numbers, roman numerals or repeat marks, so "Verse 2", "Verse II" and "Chorus (x2)" group with "Verse" and "Chorus".</summary>
    public static string Family(string? title)
    {
        var name = (title ?? "").Trim();
        name = Regex.Replace(name, @"\s*[\(\[]?\s*x\s*\d+\s*[\)\]]?\s*$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"[\s#\-_.]*(\d+|[ivx]+)\s*$", "", RegexOptions.IgnoreCase);
        return name.Trim();
    }

    private static (double Hue, double Saturation, double Lightness) Hsl(Rgba colour)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, bl = colour.B / 255.0;
        double max = Math.Max(r, Math.Max(g, bl)), min = Math.Min(r, Math.Min(g, bl)), l = (max + min) / 2, d = max - min;
        if (d < 1e-6) return (0, 0, l);
        var s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? (g - bl) / d % 6 : max == g ? (bl - r) / d + 2 : (r - g) / d + 4;
        return ((h * 60 + 360) % 360, s, l);
    }
}
