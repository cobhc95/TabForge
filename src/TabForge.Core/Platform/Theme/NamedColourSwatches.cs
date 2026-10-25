using System.Collections.Generic;
using System.Linq;

namespace TabForge.Platform.Theme;

// Owns: the named track and section colour lists, and the default track and section colours, as "#RRGGBB" text (the form the song model stores).
// Does not own: the theme palette (Palette: #AARRGGBB keys with dark and light values), the pickers that show these lists, or the colour a mixer group gets (TrackColouring).
// Tests: NamedColourSwatchesTests (TabForge.Avalonia.Tests), which pins every value and its order.

/// <summary>
/// The shared colour lists the track and section colours are picked from. The lists are typed read-only; the values are the
/// exact hex text, so a list entry and the song model's stored colour compare equal.
/// </summary>
public static class NamedColourSwatches
{
    /// <summary>Named, slightly muted track colours (readable on dark and light themes), in picker order.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> All = new (string Name, string Hex)[]
    {
        ("Red", "#B8403A"), ("Orange", "#C46A2B"), ("Amber", "#B8902A"), ("Yellow", "#A89A2E"), ("Olive", "#7A8A34"),
        ("Green", "#3F8F4E"), ("Teal", "#2E8A7E"), ("Turquoise", "#2A96A6"), ("Blue", "#3A6FB8"), ("Indigo", "#5357B0"),
        ("Purple", "#7A4FB0"), ("Magenta", "#A4468E"), ("Pink", "#B8577A"), ("Brown", "#8A5E3C"), ("Grey", "#6E7580"), ("Slate", "#4A5563"),
    };

    /// <summary>Named section marker colours, in the order imported sections take them.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Sections = new (string Name, string Hex)[]
    {
        ("Blue", "#2E74B5"), ("Green", "#3FB950"), ("Gold", "#D8A032"), ("Violet", "#8B5CF6"),
        ("Teal", "#00A6A6"), ("Rose", "#E06C75"), ("Slate", "#64748B"), ("Pink", "#C45A9A")
    };

    /// <summary>The named preset colours the colour choosers offer (a setting's colour, a window's colour), in dropdown order.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = new (string Name, string Hex)[]
    {
        ("Glide blue", "#4C9AFF"), ("Cyan", "#22D3EE"), ("Teal", "#14B8A6"), ("Emerald", "#10B981"),
        ("Green", "#3FB950"), ("Lime", "#84CC16"), ("Violet", "#8B5CF6"), ("Indigo", "#6366F1"),
        ("Magenta", "#D946EF"), ("Rose", "#F43F5E"), ("Crimson", "#DC2626"), ("Orange", "#F97316"),
        ("Amber", "#F59E0B"), ("White", "#F2F4F6"), ("Silver", "#98A1AE"), ("Slate", "#4B5563"),
        ("Charcoal", "#23272D"), ("Black", "#111111"),
    };

    /// <summary>The colour of a track that has none of its own (a new track, a startup track, an imported track without a colour).</summary>
    public const string TrackDefaultColour = "#F61A16";

    /// <summary>The colours new tracks take in turn, starting with <see cref="TrackDefaultColour"/>.</summary>
    public static readonly IReadOnlyList<string> TrackRotationColours =
        new[] { TrackDefaultColour, "#ED2224", "#F4E014", "#2248E8", "#35B954", "#D850C6" };

    /// <summary>The ten quick track colours the track properties window offers beside its colour box, in picker order (two rows of five).</summary>
    public static readonly IReadOnlyList<string> TrackQuickPickColours = new[]
    {
        "#E5484D", "#F76B15", "#FFC53D", "#46A758", "#12A594", "#3E63DD", "#8E4EC6", "#D6409F", "#8B8D98", "#F0F0F0",
    };

    /// <summary>The colour of a section marker that has none of its own: the first section colour.</summary>
    public static readonly string SectionDefaultColour = Sections[0].Hex;

    /// <summary>The hex text of the entry named <paramref name="name"/> in <see cref="All"/>; throws when there is no such entry.</summary>
    public static string HexOf(string name) => All.Single(swatch => swatch.Name == name).Hex;
}
