using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: showing the Preferences search: the registry entries that match the query (SettingsCatalog.Matches), as cards captioned
//       "Page › Section" in page order, and the card of a catalogue page's rows.
// Does not own: what matches (SettingsCatalog), the controls of a special entry (PreferencesSpecialPages) or the shortcut rows (ShortcutsPage).
// Tests: TabForge.Avalonia.Tests.SettingsSearchGuardTests.
public sealed partial class PreferencesWindow
{
    private SpecialActions SpecialActions() => new(ShowPage,
        () => Platform.UiTasks.Forget(ResetThemeAsync(), "Reset theme", text => _status.Text = text),
        () => Platform.UiTasks.Forget(ResetAllAsync(), "Reset all settings", text => _status.Text = text),
        () => _ = OpenResetAppAsync(),
        RunFooterAction);

    /// <summary>Search results never run anything: picking a result opens the entry's page, where the real button lives.</summary>
    private SpecialActions SearchActions() => new(ShowPage,
        () => ShowPage(SettingsCatalog.Appearance),
        () => ShowPage(SettingsCatalog.Advanced),
        () => ShowPage(SettingsCatalog.Advanced),
        id => ShowPage(SettingsCatalog.Special(id).Page));

    /// <summary>The card of a page's group: its rows, and the Theme card's reset right after the theme row on the Appearance page.</summary>
    private Control RowCard(PreferenceCard<SettingDescriptor> card, bool searching)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (var row in card.Basic)
        {
            stack.Children.Add(RowPanel(row));
            // The Theme combo's card is the Interface group in the catalogue, so the reset follows the combo row itself.
            if (!searching && _page == SettingsCatalog.Appearance && row.Key == "appearance.thememode")
                stack.Children.Add(PreferencesSpecialPages.Part(SettingsCatalog.Special(SettingsCatalog.ActionResetTheme), SpecialActions())!);
        }
        if (card.More.Count > 0) stack.Children.Add(MoreOptions(card.Category(), card.Group, card.More, searching));
        return PreferencesSpecialPages.Card(card.Title, stack);
    }

    /// <summary>Shows every registry entry matching <paramref name="query"/> and the chosen category: rows, buttons, links, info and shortcuts, grouped by page.</summary>
    private void AddSearchResults(string query)
    {
        var found = Registry.Where(entry => SettingsCatalog.Matches(entry, query, _searchBar.SelectedCategory)).ToList();
        var cards = new List<(int Page, Control Card)>();
        foreach (var card in PreferencesPagePresenter.Cards(found.Where(entry => entry.Row is not null).Select(entry => entry.Row!).ToList(), searching: true))
            cards.Add((PageIndex(card.Category()), RowCard(card, searching: true)));
        var actions = SearchActions();
        foreach (var group in found.Where(entry => entry.Row is null && entry.Kind != SettingsEntryKind.Shortcut).GroupBy(entry => (entry.Page, entry.Section)))
        {
            var parts = group.Select(entry => PreferencesSpecialPages.Part(entry, actions)).OfType<Control>().ToList();
            if (parts.Count == 0) continue;
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.AddRange(parts);
            cards.Add((PageIndex(group.Key.Page), PreferencesSpecialPages.Card($"{group.Key.Page} › {group.Key.Section}", stack)));
        }
        var shortcuts = found.Where(entry => entry.Kind == SettingsEntryKind.Shortcut).Select(entry => entry.Id).ToHashSet();
        var commands = found.Any(entry => entry.Id is SettingsCatalog.ActionPreset or SettingsCatalog.ActionResetCategory);
        if (shortcuts.Count > 0 || commands)
            cards.AddRange(PreferencesSpecialPages.Shortcuts(Working.Hotkeys, ShortcutsChanged, shortcuts, commands).Select(card => (PageIndex(SettingsCatalog.Hotkeys), card)));
        foreach (var (_, card) in cards.OrderBy(pair => pair.Page)) _cards.Children.Add(card);
        if (cards.Count == 0) _cards.Children.Add(ToolWindowParts.Small("No settings match the search."));
    }

    private static int PageIndex(string page) => SettingsCatalog.Categories.ToList().IndexOf(page);
}
