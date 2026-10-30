using System.Globalization;

namespace TabForge.Presenters;

/// <summary>What the Preferences page rules read from one settings row; the app's catalogue row provides it.</summary>
public interface IPreferenceRow
{
    string Key { get; }
    string Category { get; }
    string Group { get; }
    string Title { get; }
    int Order { get; }
    bool More { get; }
    string? DependsOn { get; }
    string? DependsOnValue { get; }
    bool IsSwitch { get; }
    bool IsColour { get; }
    object? Value { get; }
}

/// <summary>One card of a settings page: its caption, its group, the always-shown rows and the rows behind "More options".</summary>
public sealed record PreferenceCard<T>(string Title, string Group, IReadOnlyList<T> Basic, IReadOnlyList<T> More) where T : IPreferenceRow;

/// <summary>A reset question: its title and message.</summary>
public sealed record ResetPrompt(string Title, string Message);

/// <summary>
/// The Preferences window's page rules without UI types.
/// Owns: the cards of a page (order, grouping, captions, Basic and More rows), the enabled rule of a dependent row,
///     the reset-page and reset-shortcuts texts, and the pending-change list shown before discarding.
/// Does not own: the rows themselves (SettingsCatalog), searching (SettingsCatalog.Matches), saving or migration (SettingsMigration, SettingsStore),
///     or the controls (Views/PreferencesWindow.xaml.cs).
/// Tests: no dedicated test class.
/// </summary>
public static class PreferencesPagePresenter
{
    public const string NoChangedSettings = "No settings on this page differ from the defaults.";
    public const string NoChangedShortcuts = "No shortcuts on this page differ from the defaults.";

    /// <summary>The settings the Appearance page's Theme card resets: the built-in palette, sizing, typography, motion and score look. The theme mode comes first, as its preset rewrites the colours after it.</summary>
    public static readonly IReadOnlyList<string> ThemeResetKeys = new[]
    {
        "appearance.thememode", "appearance.uiscale", "appearance.density", "appearance.reduceanimations",
        "appearance.animationspeed", "appearance.accent", "appearance.selection", "appearance.hover",
        "appearance.background", "appearance.panel", "appearance.titlebar", "appearance.tabactive", "appearance.tabhover",
        "appearance.text", "appearance.muted", "appearance.font", "appearance.fontsize", "appearance.iconsize",
        "appearance.scorefont", "appearance.scorefontsize", "appearance.scorebold",
        "appearance.scoreitalic", "appearance.spacing", "appearance.paper", "appearance.scorepaper.dark",
        "appearance.scorepaper.light", "appearance.scoreink.dark", "appearance.scoreink.light",
        "appearance.scorelines.dark", "appearance.scorelines.light", "appearance.cursor",
        "score.systemspacing", "score.measurespacing", "score.ledger", "score.staffopacity",
        "score.hoverintensity", "score.selectionintensity", "score.barnumbers", "score.barnumberfrequency",
        "score.sectionheadings",
    };

    /// <summary>The cards for the shown rows: a page keeps catalogue order; search results are sorted by page first and captioned "Page › Group".</summary>
    public static IReadOnlyList<PreferenceCard<T>> Cards<T>(IEnumerable<T> shown, bool searching) where T : IPreferenceRow =>
        shown.OrderBy(row => searching ? row.Category : "").ThenBy(row => row.Order).GroupBy(row => (row.Category, row.Group))
            .Select(group => new PreferenceCard<T>(searching ? $"{group.Key.Category} › {group.Key.Group}" : group.Key.Group, group.Key.Group,
                group.Where(row => !row.More).ToList(), group.Where(row => row.More).ToList()))
            .ToList();

    /// <summary>A dependent row is enabled while its parent is on (a switch) or holds one of the '|'-separated values, all the way up; a missing parent or a cycle disables it.</summary>
    public static bool IsEnabled(IPreferenceRow row, Func<string, IPreferenceRow?> find) =>
        IsEnabled(row, find, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static bool IsEnabled(IPreferenceRow row, Func<string, IPreferenceRow?> find, HashSet<string> visited)
    {
        if (row.DependsOn is null) return true;
        if (!visited.Add(row.Key) || find(row.DependsOn) is not { } parent) return false;
        var matches = parent.IsSwitch
            ? parent.Value is true
            : row.DependsOnValue is null || row.DependsOnValue.Split('|').Contains(parent.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        return matches && IsEnabled(parent, find, visited);
    }

    public static ResetPrompt ResetPage(string page, int changed) =>
        new($"Reset {page}", $"Restore {changed} setting{Plural(changed)} on this page to their defaults?");

    public static ResetPrompt ResetShortcuts(int changed) =>
        new("Reset shortcuts", $"Restore the default keys for {changed} changed shortcut{Plural(changed)}?");

    public static ResetPrompt ResetAll() =>
        new("Reset all settings", "Restores every option and keyboard shortcut in Settings to its default. Your presets, layouts, folders and songs are not touched. Cancel still undoes it until you press OK or Apply.");

    /// <summary>A value as the change list shows it: On / Off, a number with up to two decimals, "(none)", or its text.</summary>
    public static string ShowValue(object? value) => value switch
    {
        bool on => on ? "On" : "Off",
        double number => number.ToString("0.##", CultureInfo.CurrentCulture),
        null => "(none)",
        _ => value.ToString() ?? ""
    };

    /// <summary>One setting whose shown value differs from the saved one: its key, its page, its name and the shown old and new values.</summary>
    public sealed record PreferenceChange(string Key, string Page, string Setting, string OldText, string NewText, bool IsColour = false)
    {
        /// <summary>"Page › Setting: old → new", the line the change list and the tooltip show.</summary>
        public string Line => $"{Page} › {Setting}: {OldText} → {NewText}";
    }

    /// <summary>Every row whose shown value differs between <paramref name="before"/> and <paramref name="now"/>, in the order of <paramref name="now"/>.</summary>
    public static List<PreferenceChange> Changes(IEnumerable<IPreferenceRow> before, IEnumerable<IPreferenceRow> now)
    {
        var old = before.ToDictionary(row => row.Key);
        var changes = new List<PreferenceChange>();
        foreach (var row in now)
        {
            if (!old.TryGetValue(row.Key, out var previous)) continue;
            var oldText = ShowValue(previous.Value);
            var newText = ShowValue(row.Value);
            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
                changes.Add(new PreferenceChange(row.Key, row.Category, row.Title, oldText, newText, row.IsColour));
        }
        return changes;
    }

    /// <summary>"Page › Setting: old → new" for every row whose shown value differs, then the shortcuts line; never empty.</summary>
    public static List<string> PendingChanges(IEnumerable<IPreferenceRow> before, IEnumerable<IPreferenceRow> now, bool shortcutsChanged)
    {
        var changes = Changes(before, now).Select(change => change.Line).ToList();
        if (shortcutsChanged) changes.Add("Hotkeys › keyboard shortcuts were changed");
        if (changes.Count == 0) changes.Add("Other settings not shown on these pages were changed");
        return changes;
    }

    private static string Plural(int count) => count == 1 ? "" : "s";
}
