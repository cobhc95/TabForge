using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Settings pages beyond General and Appearance: every catalogue page shows its rows, a control round-trips a setting, and Common
/// settings, Shortcuts and Advanced render in both themes.
/// Tests: this class.
/// </summary>
public class PreferencesPagesTests
{
    private static PreferencesWindow Open() => new(new AvaloniaDialogHost(), new AppSettings());

    [AvaloniaFact]
    public void EveryCataloguePageShowsItsRows()
    {
        var window = Open();
        var all = PreferencesCatalog.Build(new AppSettings());
        foreach (var page in SettingsCatalog.Bands.SelectMany(b => b.Pages).Where(p => p is not (SettingsCatalog.Home or SettingsCatalog.Hotkeys or SettingsCatalog.Advanced)))
        {
            window.ShowPage(page);
            var expected = all.Where(r => r.Category == page).Select(r => r.Key).OrderBy(k => k).ToList();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, window.ShownRows.Select(r => r.Key).OrderBy(k => k).ToList());
        }
    }

    [AvaloniaFact]
    public void ScorePageControlRoundTripsASetting()
    {
        var window = Open();
        window.ShowPage(SettingsCatalog.Score);
        var row = window.ShownRows.First(r => r.Kind == SettingKind.Bool);
        var toggle = (CheckBox)window.EditorOf(row.Key);
        var before = toggle.IsChecked == true;
        toggle.IsChecked = !before;
        Assert.Equal(!before, (bool)PreferencesCatalog.Build(window.Working).Single(r => r.Key == row.Key).Get()!);
    }

    [Fact]
    public void EveryThemeResetKeyIsACatalogueRow()
    {
        var known = PreferencesCatalog.Build(new AppSettings()).Select(r => r.Key).ToHashSet();
        Assert.All(TabForge.Presenters.PreferencesPagePresenter.ThemeResetKeys, key => Assert.Contains(key, known));
    }

    [AvaloniaFact]
    public void CommonSettingsRowsWriteThroughTheSameSettersAndLinksNavigate()
    {
        var window = Open();
        window.ShowPage(SettingsCatalog.Home);
        var known = PreferencesCatalog.Build(new AppSettings()).Select(r => r.Key).ToHashSet();
        Assert.Equal(PreferencesSpecialPages.HomeCards.SelectMany(c => c.Keys).Count(known.Contains), window.ShownRows.Count);
        ((ComboBox)window.EditorOf("appearance.thememode")).SelectedItem = "Light";
        Assert.Equal("Light", window.Working.Appearance.ThemeMode);
        var link = window.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "All score settings");
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SettingsCatalog.Score, window.Page);
    }

    [AvaloniaFact]
    public void ShortcutsPageListsEveryCommandAndSwitchesPreset()
    {
        var window = Open();
        window.ShowPage(SettingsCatalog.Hotkeys);
        var builtSynchronously = window.GetLogicalDescendants().OfType<Grid>().Count(g => g.Name == "Shortcut");
        Assert.InRange(builtSynchronously, 1, 40);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(HotkeyCatalog.All.Count, window.GetLogicalDescendants().OfType<Grid>().Count(g => g.Name == "Shortcut"));
        var preset = window.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Name != "CategoryFilter");
        preset.SelectedItem = HotkeyPresets.GuitarPro5;
        Assert.Equal(HotkeyPresets.GuitarPro5, window.Working.Hotkeys.Preset);
    }

    [AvaloniaFact]
    public void ShortcutsPageCapturesConflictsReassignsAndResets()
    {
        var window = Open();
        window.ShowPage(SettingsCatalog.Hotkeys);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var pair = HotkeyCatalog.All.Where(a => HotkeyCatalog.ContextOf(a.Id) == 0).Take(2).ToList();
        var (a, b) = (pair[0], pair[1]);
        var free = Enumerable.Range(9, 16).Select(n => "F" + n).First(k => !HotkeyCatalog.All.Any(x => HotkeyCatalog.DefaultFor(x, 1) == k || HotkeyCatalog.DefaultFor(x, 2) == k));
        var hotkeys = window.Working.Hotkeys;
        Button Find(string name) => window.GetLogicalDescendants().OfType<Button>().First(x => global::Avalonia.Automation.AutomationProperties.GetName(x) == name);
        void Press(Button button, global::Avalonia.Input.Key key) => button.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
            { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = key, KeyModifiers = global::Avalonia.Input.KeyModifiers.None });

        var slot = Find($"{a.Name} hotkey 1");
        slot.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Press a key…", slot.Content);
        Press(slot, global::Avalonia.Input.Key.Escape);
        Assert.Equal(HotkeyCatalog.DefaultFor(a, 1), HotkeyCatalog.GestureFor(hotkeys, a.Id, 1));

        hotkeys.Set(b.Id, 1, free);
        slot = Find($"{a.Name} hotkey 1");
        slot.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Press(slot, System.Enum.Parse<global::Avalonia.Input.Key>(free));
        Assert.Equal("Conflict", slot.Content);
        Assert.Equal(HotkeyCatalog.DefaultFor(a, 1), HotkeyCatalog.GestureFor(hotkeys, a.Id, 1));
        Find($"Reassign {a.Name}").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(free, HotkeyCatalog.GestureFor(hotkeys, a.Id, 1));
        Assert.True(hotkeys.IsDisabled(b.Id, 1));

        Find($"Reset {a.Name} shortcuts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(HotkeyCatalog.DefaultFor(a, 1), HotkeyCatalog.GestureFor(hotkeys, a.Id, 1));
    }

    [AvaloniaFact]
    public void ResetCategoryWhileFilteredResetsOnlyTheShownCommands()
    {
        var settings = new AppSettings().Hotkeys;
        var (shown, hidden) = (HotkeyCatalog.All[0], HotkeyCatalog.All.First(a => a.Category != HotkeyCatalog.All[0].Category));
        settings.Set(shown.Id, 1, "F13");
        settings.Set(hidden.Id, 1, "F14");
        var cards = PreferencesSpecialPages.Shortcuts(settings, () => { }, new System.Collections.Generic.HashSet<string> { shown.Id }, commands: true).ToList();
        var reset = cards.SelectMany(c => c.GetLogicalDescendants().OfType<Button>()).First(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == SettingsCatalog.Special(SettingsCatalog.ActionResetCategory).Label);
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(HotkeyCatalog.DefaultFor(shown, 1), HotkeyCatalog.GestureFor(settings, shown.Id, 1));
        Assert.Equal("F14", HotkeyCatalog.GestureFor(settings, hidden.Id, 1));
    }

    [AvaloniaFact]
    public void OpeningAPageFromASearchResultLeavesTheSearch()
    {
        var window = Open();
        window.Search("all score settings");
        window.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "All score settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SettingsCatalog.Score, window.Page);
        Assert.Contains(window.ShownRows, r => r.Category == SettingsCatalog.Score);
    }

    [AvaloniaFact]
    public void AdvancedPageShowsVersionAndOffersReset()
    {
        var window = Open();
        window.ShowPage(SettingsCatalog.Advanced);
        var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains($"TabForge {AppInfo.DisplayVersion}", texts);
        Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == "Reset all settings…");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewPagesRenderPngsInEachTheme(bool light)
    {
        using var theme = UseTheme(light);
        var window = Open();
        foreach (var page in new[] { SettingsCatalog.Home, SettingsCatalog.Score, SettingsCatalog.Playback, SettingsCatalog.Hotkeys, SettingsCatalog.Advanced })
        {
            window.ShowPage(page);
            Assert.True(new FileInfo(SavePng(window, nameof(PreferencesPagesTests), $"preferences-{page.Split(' ')[0].ToLowerInvariant()}-{(light ? "light" : "dark")}.png")).Length > 0);
        }
        window.Close();
    }
}
