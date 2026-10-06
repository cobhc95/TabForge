using System.Runtime.CompilerServices;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>
/// The colour a section is shown in, shared by the arrangement timeline and the sidebar section list.
/// Optionally, sections with the same base name ("Verse 1", "Verse 2") share the first one's colour, and a
/// different section type whose colour looks too similar gets the next distinct colour instead. An explicit family
/// choice takes precedence over automatic collision separation and is precomputed when section hits are rebuilt.
/// </summary>
public static class SectionColours
{
    private static readonly ConditionalWeakTable<MarkerModel, object> Resolved = new();

    /// <summary>Display colour per marker (markers in bar order). Also remembered for <see cref="DisplayFor"/>.</summary>
    public static Dictionary<MarkerModel, Color> Resolve(IReadOnlyList<MarkerModel> sortedMarkers, bool matchSimilar, Color fallback)
    {
        var result = new Dictionary<MarkerModel, Color>(ReferenceEqualityComparer.Instance);
        var familyColours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        var explicitFamilyColours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        var usedColours = new HashSet<Color>();
        if (matchSimilar)
            foreach (var marker in sortedMarkers)
                if (marker.ColorIsExplicit && Family(marker.Title) is { Length: > 0 } family &&
                    !explicitFamilyColours.ContainsKey(family) && ColourText.TryParse(marker.ColorHex, out var chosen))
                    explicitFamilyColours.Add(family, chosen);

        foreach (var marker in sortedMarkers)
        {
            var colour = ColourText.TryParse(marker.ColorHex, out var parsed)
                ? marker.ColorIsExplicit ? parsed : Draw.Tame(parsed)
                : fallback;
            var family = matchSimilar ? Family(marker.Title) : "";
            if (family.Length > 0)
            {
                if (familyColours.TryGetValue(family, out var shared)) colour = shared;
                else
                {
                    if (explicitFamilyColours.TryGetValue(family, out var chosen)) colour = chosen;
                    else if (usedColours.Any(used => LooksAlike(used, colour)))
                        colour = DistinctSectionColours.FirstOrDefault(c => !usedColours.Any(used => LooksAlike(used, c)), colour);
                    familyColours[family] = colour;
                    usedColours.Add(colour);
                }
            }
            result[marker] = colour;
            Resolved.AddOrUpdate(marker, colour);
        }
        return result;
    }

    /// <summary>The colour last resolved for this marker (null until the timeline or list resolved it).</summary>
    public static Color? DisplayFor(MarkerModel marker) => Resolved.TryGetValue(marker, out var colour) ? (Color)colour : null;

    /// <summary>The editor's current visible RGB value, including a resolved family or collision colour.</summary>
    public static string EditorColourHex(MarkerModel marker)
    {
        if (DisplayFor(marker) is { } shown) return ColourText.Hex(shown);
        if (!ColourText.TryParse(marker.ColorHex, out var own)) return "#2E74B5";
        return ColourText.Hex(marker.ColorIsExplicit ? own : Draw.Tame(own));
    }

    /// <summary>Checks whether saving the editor would change the name or the currently visible colour.</summary>
    public static bool IsUnchangedEdit(MarkerModel marker, string title, string colourHex) =>
        string.Equals(title, marker.Title, StringComparison.Ordinal) &&
        ColourText.TryParse(colourHex, out var chosen) &&
        ColourText.TryParse(EditorColourHex(marker), out var shown) && chosen == shown;

    /// <summary>Applies an explicit RGB choice to the selected section family when matching is enabled.</summary>
    public static void ApplyExplicitChoice(IReadOnlyList<MarkerModel> markers, MarkerModel selected, string title, string colourHex, bool matchSimilar)
    {
        selected.Title = title;
        var family = matchSimilar ? Family(title) : "";
        foreach (var marker in markers)
        {
            if (!ReferenceEquals(marker, selected) &&
                (family.Length == 0 || !string.Equals(Family(marker.Title), family, StringComparison.OrdinalIgnoreCase))) continue;
            marker.ColorHex = colourHex;
            marker.ColorIsExplicit = true;
        }
    }

    /// <summary>Applies an editor result; an unchanged visible value is a no-op, and a rename alone keeps family resolution.</summary>
    public static bool ApplyEdit(IReadOnlyList<MarkerModel> markers, MarkerModel selected, string title, string colourHex, bool matchSimilar)
    {
        if (IsUnchangedEdit(selected, title, colourHex)) return false;
        var colourChanged = !ColourText.TryParse(colourHex, out var chosen) ||
                            !ColourText.TryParse(EditorColourHex(selected), out var shown) || chosen != shown;
        if (colourChanged) ApplyExplicitChoice(markers, selected, title, colourHex, matchSimilar);
        else selected.Title = title;
        return true;
    }

    /// <summary>"Verse 2" / "Chorus (x2)" / "Intro II" -> the section type ("Verse", "Chorus", "Intro").</summary>
    public static string Family(string? title)
    {
        var name = (title ?? "").Trim();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*[\(\[]?\s*x\s*\d+\s*[\)\]]?\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[\s#\-_.]*(\d+|[ivx]+)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return name.Trim();
    }

    private static bool LooksAlike(Color a, Color b)
    {
        static (double h, double s, double l) Hsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, bl = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, bl)), min = Math.Min(r, Math.Min(g, bl)), l = (max + min) / 2, d = max - min;
            if (d < 1e-6) return (0, 0, l);
            var s = d / (1 - Math.Abs(2 * l - 1));
            double h = max == r ? (g - bl) / d % 6 : max == g ? (bl - r) / d + 2 : (r - g) / d + 4;
            return ((h * 60 + 360) % 360, s, l);
        }
        var (h1, s1, l1) = Hsl(a);
        var (h2, s2, l2) = Hsl(b);
        if (s1 < 0.18 || s2 < 0.18) return s1 < 0.18 && s2 < 0.18 && Math.Abs(l1 - l2) < 0.15;
        var hueGap = Math.Abs(h1 - h2);
        hueGap = Math.Min(hueGap, 360 - hueGap);
        return hueGap < 28 && Math.Abs(l1 - l2) < 0.22;
    }

    /// <summary>Well-separated hues (dark-theme friendly) handed out when section types would collide.</summary>
    private static readonly Color[] DistinctSectionColours =
    {
        Color.FromRgb(0x2E, 0x74, 0xB5), Color.FromRgb(0x3F, 0x9B, 0x4F), Color.FromRgb(0xB8, 0x86, 0x2B),
        Color.FromRgb(0x7B, 0x4F, 0xC9), Color.FromRgb(0x14, 0x8F, 0x8A), Color.FromRgb(0xC2, 0x4B, 0x5A),
        Color.FromRgb(0xC9, 0x6A, 0x2C), Color.FromRgb(0x4B, 0x5B, 0xD1), Color.FromRgb(0xB5, 0x45, 0x9E),
        Color.FromRgb(0x6F, 0x8F, 0x2A), Color.FromRgb(0x2A, 0x9F, 0xC4), Color.FromRgb(0x8C, 0x5A, 0x3C),
        Color.FromRgb(0x5E, 0x6B, 0x7D), Color.FromRgb(0xA3, 0x3C, 0x3C)
    };

    /// <summary>
    /// A section's family name: the title without trailing numbers, roman numerals or repeat marks,
    /// so "Verse 2", "Verse II" and "Chorus (x2)" group with "Verse" and "Chorus".
    /// </summary>
}
