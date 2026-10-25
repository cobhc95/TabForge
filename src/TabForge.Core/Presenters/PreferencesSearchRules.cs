using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>
/// The Preferences search bar's sizes and category rule without UI types.
/// Owns: the category list, the "all categories" text, the bar's sizes and the category filter rule.
/// Does not own: the controls (each UI toolkit's search bar) or the text match (SettingsCatalog.Matches).
/// Tests: TabForge.Avalonia.Tests.PreferencesSearchBarTests.
/// </summary>
public static class PreferencesSearchRules
{
    public const string AllCategoriesText = "All categories";
    public const double CategoryColumnWidth = 128;
    public const double SearchMaxWidth = 896;
    public const double HeaderLeft = 22, HeaderTop = 18, HeaderRight = 28, HeaderBottom = 12;

    /// <summary>"All categories" first, then the Preferences pages only; the shortcut categories are the Shortcuts page's own filter.</summary>
    public static IReadOnlyList<string> Categories() => new[] { AllCategoriesText }.Concat(SettingsCatalog.Categories).ToArray();

    /// <summary>True when a row of <paramref name="rowCategory"/> passes the chosen category (none or "All categories" passes every row).</summary>
    public static bool PassesCategory(string? chosen, string rowCategory) =>
        string.IsNullOrEmpty(chosen) || chosen == AllCategoriesText || chosen.Equals(rowCategory, StringComparison.OrdinalIgnoreCase);
}
