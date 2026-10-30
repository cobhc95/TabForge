using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Guard: everything the Preferences pages show can be found by the Preferences search. Walks every page's built controls and fails when a labelled
/// control, a shortcut, a keyword, a synonym or a page name is not found; the only controls without a search entry are in
/// <see cref="DecorativeWithoutSearchEntry"/> (dividers, spacing), each saying why. Dialogs opened from a row are not part of a page.
/// </summary>
public class SettingsSearchGuardTests
{
    /// <summary>Controls with no search entry: decorative only, each with the reason. Empty: the pages build no divider or spacing control with a label.</summary>
    private static readonly IReadOnlyDictionary<string, string> DecorativeWithoutSearchEntry = new Dictionary<string, string>
    {
    };
    private static PreferencesWindow Open() => new(new AvaloniaDialogHost(), new AppSettings());

    private static IEnumerable<(Control Control, string Label)> Labelled(PreferencesWindow window) => Interactive(window).Where(item => item.Label.Length > 0);

    /// <summary>Every control of the shown page a user can operate, with its label ("" when it has none).</summary>
    private static IEnumerable<(Control Control, string Label)> Interactive(PreferencesWindow window) =>
        window.GetLogicalDescendants().OfType<StackPanel>().First(panel => panel.Name == PreferencesWindow.PageCardsName).GetLogicalDescendants().OfType<Control>()
            .Where(control => control is CheckBox or ComboBox or NumericUpDown or Slider or TextBox or Button && (control is CheckBox || control is not ToggleButton))
            .Select(control => (Control: control, Label: LabelOf(control)));

    private static List<string> Unlabelled(IEnumerable<(Control Control, string Label)> controls) =>
        controls.Where(item => string.IsNullOrWhiteSpace(item.Label)).Select(item => item.Control.GetType().Name + " " + (item.Control.Name ?? "")).ToList();

    private static string LabelOf(Control control)
    {
        var name = AutomationProperties.GetName(control);
        return !string.IsNullOrWhiteSpace(name) ? name : control is Button { Content: string text } ? text : "";
    }

    private static bool Shows(PreferencesWindow window, Type type, string label) =>
        Labelled(window).Any(item => item.Control.GetType() == type && item.Label == label);

    private static bool HasCardOf(PreferencesWindow window, string page) =>
        window.GetLogicalDescendants().OfType<Border>().Any(card => card.Name?.StartsWith(page + " › ", StringComparison.Ordinal) == true);

    [AvaloniaFact]
    public void Every_labelled_control_of_every_page_is_found_by_searching_its_label()
    {
        var window = Open();
        var missing = new List<string>();
        var checkedCount = 0;
        foreach (var page in SettingsCatalog.Categories)
        {
            window.Search("");
            window.ShowPage(page);
            foreach (var (type, label) in Labelled(window).Select(item => (item.Control.GetType(), item.Label)).Distinct().ToList())
            {
                checkedCount++;
                window.Search(label.ToLowerInvariant());
                if (!Shows(window, type, label)) missing.Add($"{page}: {type.Name} \"{label}\"");
            }
        }
        Assert.True(checkedCount > 200, $"Only {checkedCount} labelled controls were walked");
        Assert.True(missing.Count == 0, "Not found by searching their own label:\n" + string.Join("\n", missing));
    }

    [AvaloniaFact]
    public void Every_interactive_control_of_every_page_has_a_label()
    {
        var window = Open();
        var unlabelled = new List<string>();
        foreach (var page in SettingsCatalog.Categories)
        {
            window.Search("");
            window.ShowPage(page);
            unlabelled.AddRange(Unlabelled(Interactive(window)).Select(item => $"{page}: {item}"));
        }
        Assert.True(unlabelled.Count == 0, "Controls without a label (the search cannot find them):\n" + string.Join("\n", unlabelled));
    }

    [AvaloniaFact]
    public void The_guard_fails_for_an_unlabelled_control_and_a_broken_label()
    {
        var window = Open();
        window.Search("");
        window.ShowPage(SettingsCatalog.Appearance);
        var (control, label) = Labelled(window).First();
        Assert.Empty(Unlabelled(new[] { (control, label) }));
        Assert.Single(Unlabelled(new[] { (control, "  ") }));
        Assert.Single(Unlabelled(new[] { ((Control)new Button(), LabelOf(new Button())) }));
        window.Search((label + " zzz-broken").ToLowerInvariant());
        Assert.False(Shows(window, control.GetType(), label + " zzz-broken"));
    }

    [AvaloniaFact]
    public void Every_labelled_control_outside_the_pages_is_found_by_searching_its_label()
    {
        var window = Open();
        var labels = window.GetLogicalDescendants().OfType<Button>()
            .Where(button => button.IsVisible && !button.GetLogicalAncestors().OfType<StackPanel>().Any(panel => panel.Name == PreferencesWindow.PageCardsName))
            .SelectMany(button => new[] { LabelOf(button) }.Concat(button.Flyout is MenuFlyout flyout ? flyout.Items.OfType<MenuItem>().Select(item => item.Header as string ?? "") : Array.Empty<string>()))
            .Where(label => label.Length > 0).Distinct().ToList();
        var missing = new List<string>();
        foreach (var label in labels)
        {
            window.Search(label.ToLowerInvariant());
            if (!Shows(window, typeof(Button), label)) missing.Add($"\"{label}\"");
        }
        Assert.True(labels.Count >= 8, $"Only {labels.Count} labelled controls outside the pages were walked");
        Assert.True(missing.Count == 0, "Reset page, footer or menu controls not found by searching their label:\n" + string.Join("\n", missing));
    }

    [AvaloniaFact]
    public void Every_shortcut_is_found_by_its_name()
    {
        var window = Open();
        var missing = new List<string>();
        foreach (var action in HotkeyCatalog.All)
        {
            window.Search(action.Name.ToLowerInvariant());
            Dispatcher.UIThread.RunJobs();
            if (!Shows(window, typeof(Button), $"{action.Name} hotkey 1")) missing.Add($"{action.Id} \"{action.Name}\"");
        }
        Assert.True(missing.Count == 0, "Shortcuts not found by name:\n" + string.Join("\n", missing));
    }

    [AvaloniaFact]
    public void Shortcuts_are_found_by_their_current_keys()
    {
        var settings = new AppSettings();
        var entry = SettingsCatalog.Registry(SettingsCatalog.Build(settings), settings.Hotkeys).Single(e => e.Id == "File.Save");
        Assert.True(SettingsCatalog.Matches(entry, "ctrl+s"));
    }

    [Fact]
    public void Every_keyword_and_title_of_every_entry_finds_it()
    {
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings);
        var registry = SettingsCatalog.Registry(rows, settings.Hotkeys);
        var missing = new List<string>();
        foreach (var entry in registry)
        {
            var terms = new List<string> { entry.Label.ToLowerInvariant() };
            if (entry.Row is { } row) terms.AddRange(row.Keywords.Select(keyword => keyword.ToLowerInvariant()));
            missing.AddRange(terms.Where(term => !SettingsCatalog.Matches(entry, term)).Select(term => $"{entry.Id}: {term}"));
        }
        Assert.Empty(missing);
        Assert.Equal(rows.Count + SettingsCatalog.SpecialEntries().Count + HotkeyCatalog.All.Count, registry.Count);
    }

    [AvaloniaFact]
    public void Every_page_control_has_a_registry_entry()
    {
        var window = Open();
        var settings = new AppSettings();
        var registry = SettingsCatalog.Registry(SettingsCatalog.Build(settings), settings.Hotkeys);
        var missing = new List<string>();
        var seenAllowed = new HashSet<string>();
        foreach (var page in SettingsCatalog.Categories)
        {
            window.Search("");
            window.ShowPage(page);
            foreach (var label in Labelled(window).Select(item => item.Label).Distinct().ToList())
            {
                if (DecorativeWithoutSearchEntry.TryGetValue(label, out var reason))
                {
                    Assert.False(string.IsNullOrWhiteSpace(reason), $"Allow-listed \"{label}\" must say why it needs no search entry.");
                    seenAllowed.Add(label);
                    continue;
                }
                if (!registry.Any(entry => SettingsCatalog.Matches(entry, label.ToLowerInvariant())))
                    missing.Add($"{page}: \"{label}\"");
            }
        }
        Assert.True(missing.Count == 0, "Page controls with no search entry (add a catalogue entry with keywords):\n" + string.Join("\n", missing));
        Assert.All(DecorativeWithoutSearchEntry.Keys, key => Assert.Contains(key, seenAllowed));
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("wipe")]
    [InlineData("factory")]
    [InlineData("default")]
    [InlineData("clear")]
    [InlineData("erase")]
    public void Reset_synonyms_list_both_reset_actions(string query)
    {
        var settings = new AppSettings();
        var registry = SettingsCatalog.Registry(SettingsCatalog.Build(settings), settings.Hotkeys);
        foreach (var id in new[] { SettingsCatalog.ActionResetAll, SettingsCatalog.ActionResetApp })
            Assert.True(SettingsCatalog.Matches(registry.Single(entry => entry.Id == id), query), $"\"{query}\" does not find {id}");
    }

    [Theory]
    [InlineData("compatibility", SettingsCatalog.InfoCompatibility)]
    [InlineData("migrate", SettingsCatalog.InfoCompatibility)]
    [InlineData("arrangement", SettingsCatalog.InfoArrangement)]
    [InlineData("sound bank", SettingsCatalog.BankDownload)]
    [InlineData("delete", SettingsCatalog.BankDelete)]
    [InlineData("download", SettingsCatalog.BankDownload)]
    [InlineData("shortcuts reset", SettingsCatalog.ActionResetCategory)]
    [InlineData("theme reset", SettingsCatalog.ActionResetTheme)]
    public void Info_bank_and_reset_queries_find_their_entries(string query, string id)
    {
        var settings = new AppSettings();
        var entry = SettingsCatalog.Registry(SettingsCatalog.Build(settings), settings.Hotkeys).Single(e => e.Id == id);
        Assert.True(SettingsCatalog.Matches(entry, query), $"\"{query}\" does not find {id}");
    }

    [AvaloniaFact]
    public void Search_results_select_the_right_page()
    {
        var window = Open();
        window.Search("reset entire app");
        Assert.True(HasCardOf(window, SettingsCatalog.Advanced), "No Advanced card for \"reset entire app\".");
        window.Search("download");
        Assert.True(HasCardOf(window, SettingsCatalog.AudioVst), "No Audio & Plug-ins card for \"download\".");
        window.Search("compatibility");
        Assert.True(HasCardOf(window, SettingsCatalog.Advanced), "No Advanced card for \"compatibility\".");
    }

    [AvaloniaFact]
    public void Search_results_never_run_an_action()
    {
        var window = Open();
        foreach (var id in new[] { SettingsCatalog.ActionResetApp, SettingsCatalog.ActionResetAll })
        {
            var label = SettingsCatalog.Special(id).Label;
            window.Search(label.ToLowerInvariant());
            window.GetLogicalDescendants().OfType<Button>().First(button => button.IsVisible && LabelOf(button) == label)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SettingsCatalog.Advanced, window.Page);
            Assert.Equal("All changes saved", window.StatusText);
        }
    }

    [Theory]
    [InlineData("color", "appearance.accent")]
    [InlineData("colour", "appearance.accent")]
    [InlineData("microphone", "vst.input")]
    [InlineData("mic", "vst.input")]
    [InlineData("plugin", "vst.folders")]
    [InlineData("latency", "vst.buffer")]
    [InlineData("hotkey", "File.Save")]
    [InlineData("keybinding", "File.Save")]
    [InlineData("shortcut", "File.Save")]
    [InlineData("measure", "score.barnumbers")]
    public void Everyday_words_find_their_entries(string query, string id)
    {
        var settings = new AppSettings();
        var entry = SettingsCatalog.Registry(SettingsCatalog.Build(settings), settings.Hotkeys).Single(e => e.Id == id);
        Assert.True(SettingsCatalog.Matches(entry, query), $"\"{query}\" does not find {id}");
    }

    [AvaloniaFact]
    public void Searching_a_page_name_shows_that_pages_entries()
    {
        var window = Open();
        var rows = PreferencesCatalog.Build(new AppSettings());
        foreach (var page in SettingsCatalog.Categories)
        {
            window.Search(page.ToLowerInvariant());
            Assert.True(HasCardOf(window, page), $"No result card for the page name \"{page}\"");
            var shown = window.ShownRows.Select(row => row.Key).ToHashSet();
            Assert.All(rows.Where(row => row.Category == page), row => Assert.Contains(row.Key, shown));
        }
    }

    [AvaloniaFact]
    public void The_category_filter_limits_matches_to_that_page()
    {
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings);
        var theme = SettingsCatalog.Registry(rows, settings.Hotkeys).Single(e => e.Id == "appearance.thememode");
        Assert.True(SettingsCatalog.Matches(theme, "theme", SettingsCatalog.Appearance));
        Assert.False(SettingsCatalog.Matches(theme, "theme", SettingsCatalog.Score));
    }
}
