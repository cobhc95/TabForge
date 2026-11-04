using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the searchable, grouped tuning preset list of the track properties window: which presets show for a string count, the search, the label of the current tuning.
// Does not own: the preset data (TuningPresets), the user presets (TuningUserRules) or the window.
// Tests: TabForge.Avalonia.Tests.TrackTuningDraftTests, TuningPresetCatalogTests.

/// <summary>One preset in the menu.</summary>
public sealed record TuningMenuEntry(string Name, string Group, int Strings, int[] HighToLow)
{
    /// <summary>The menu text: the name, with the string count when the list spans several counts.</summary>
    public string Text(bool showCount) => showCount ? $"{Name} ({Strings} strings)" : Name;
}

/// <summary>A heading and its presets.</summary>
public sealed record TuningMenuGroup(string Name, IReadOnlyList<TuningMenuEntry> Entries);

public static class TuningPresetMenu
{
    public const string Custom = "Custom", GuitarGroup = "Guitar", BassGroup = "Bass";
    public const string UkuleleGroup = TuningPresets.UkuleleGroup;
    public const string MandolinGroup = TuningPresets.MandolinGroup;
    public const string BanjoGroup = TuningPresets.BanjoGroup;
    public const string ViolinGroup = TuningPresets.ViolinGroup;
    public const string ViolaGroup = TuningPresets.ViolaGroup;
    public const string CelloGroup = TuningPresets.CelloGroup;
    public const string DoubleBassGroup = TuningPresets.DoubleBassGroup;
    public const string LapSteelGroup = TuningPresets.LapSteelGroup;
    public const string DulcimerGroup = TuningPresets.DulcimerGroup;
    public const string MyTuningsGroup = "My tunings";

    /// <summary>The groups for the search text: only <paramref name="stringCount"/> strings unless <paramref name="allCounts"/>; the track's own instrument group first; empty groups left out.</summary>
    public static IReadOnlyList<TuningMenuGroup> Groups(string query, int stringCount, bool allCounts, bool bass, IReadOnlyList<TuningMenuEntry>? userPresets = null)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = All(userPresets).Where(e => (allCounts || e.Strings == stringCount) && words.All(w => Matches(e, w))).ToList();
        // Local order (no static state): the track's family first, then the rest in catalog order.
        string[] rest = { UkuleleGroup, BanjoGroup, MandolinGroup, ViolinGroup, ViolaGroup, CelloGroup, DoubleBassGroup, LapSteelGroup, DulcimerGroup };
        var order = new List<string> { bass ? BassGroup : GuitarGroup, bass ? GuitarGroup : BassGroup };
        order.AddRange(rest);
        if (userPresets?.Count > 0) order.Add(MyTuningsGroup);
        return order.Select(g => new TuningMenuGroup(g, entries.Where(e => e.Group == g).ToList())).Where(g => g.Entries.Count > 0).ToList();
    }

    /// <summary>The preset the strings match for this instrument, or null (shown as Custom).</summary>
    public static TuningMenuEntry? Match(IReadOnlyList<int> tunings, bool bass, IReadOnlyList<TuningMenuEntry>? userPresets = null)
    {
        var hits = All(userPresets).Where(e => e.Strings == tunings.Count && e.HighToLow.SequenceEqual(tunings)).ToList();
        if (hits.Count == 0) return null;
        var preferred = bass ? BassGroup : GuitarGroup;
        var other = bass ? GuitarGroup : BassGroup;
        return hits.FirstOrDefault(e => e.Group == preferred)
            ?? hits.FirstOrDefault(e => e.Group == other)
            ?? hits[0];
    }

    public static string Label(IReadOnlyList<int> tunings, bool bass, IReadOnlyList<TuningMenuEntry>? userPresets = null) => Match(tunings, bass, userPresets)?.Name ?? Custom;

    /// <summary>The user's presets as menu entries under <see cref="MyTuningsGroup"/>.</summary>
    public static IReadOnlyList<TuningMenuEntry> UserEntries(IEnumerable<TuningUserPreset> users) =>
        users.Select(u => new TuningMenuEntry(u.Name, MyTuningsGroup, u.HighToLow.Length, u.HighToLow.ToArray())).ToList();

    private static bool Matches(TuningMenuEntry e, string word) =>
        e.Name.Contains(word, StringComparison.OrdinalIgnoreCase) || e.Group.Contains(word, StringComparison.OrdinalIgnoreCase)
        || e.Strings.ToString() == word || word.Equals(e.Strings + " strings", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<TuningMenuEntry> All(IReadOnlyList<TuningMenuEntry>? userPresets)
    {
        foreach (var (group, preset) in TuningPresets.Catalog)
            yield return new TuningMenuEntry(preset.Name, group, preset.HighToLow.Length, preset.HighToLow);
        if (userPresets is null) yield break;
        foreach (var u in userPresets) yield return u;
    }
}
