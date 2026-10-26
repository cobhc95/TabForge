using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>What the special pages' buttons do: open a page, reset the theme, reset everything (the window asks first).</summary>
public sealed record SpecialActions(Action<string> Open, Action ResetTheme, Action ResetAll, Action<string> Footer);

/// <summary>
/// The three Settings pages that are not plain catalogue cards: Common settings (the most used rows plus links), Shortcuts (preset and the
/// bindings of HotkeyCatalog) and Advanced (version, settings file, reset).
/// Owns: composing those pages from the catalogue rows and the special entries of SettingsCatalog (their labels and texts come from the entries).
/// Does not own: the row editors (PreferencesWindow passes them in) or any setting rule (the Shortcuts rows are ShortcutsPage).
/// Tests: TabForge.Avalonia.Tests.PreferencesPagesTests.
/// </summary>
public static class PreferencesSpecialPages
{
    /// <summary>Row keys shown on Common settings, by card, and the ids of the link entries under them.</summary>
    public static readonly IReadOnlyList<(string Title, string[] Keys, string[] LinkIds)> HomeCards = new[]
    {
        ("Look", new[] { "appearance.thememode", "appearance.uiscale", "appearance.fretboard", "general.toolbar" }, new[] { SettingsCatalog.LinkAppearance }),
        ("Sound", new[] { "vst.driver", "vst.device", "audio.metronome", "audio.metrovolume", "audio.countin", "audio.speed" },
            new[] { SettingsCatalog.LinkAudio, SettingsCatalog.LinkPlayback }),
        ("Score", new[] { "score.defaultnotation", "follow.mode" }, new[] { SettingsCatalog.LinkScore }),
        ("Files", new[] { "general.autosave", "general.saveformat" }, new[] { SettingsCatalog.LinkFiles }),
    };

    public static IEnumerable<Control> Home(IReadOnlyDictionary<string, SettingDescriptor> rows, Func<SettingDescriptor, Control> rowPanel, SpecialActions actions)
    {
        foreach (var (title, keys, linkIds) in HomeCards)
        {
            var stack = new StackPanel { Spacing = 12 };
            foreach (var key in keys.Where(rows.ContainsKey)) stack.Children.Add(rowPanel(rows[key]));
            foreach (var id in linkIds) stack.Children.Add(Part(SettingsCatalog.Special(id), actions)!);
            yield return Card(title, stack);
        }
        var note = SettingsCatalog.Special(SettingsCatalog.InfoShortcutsNote);
        var shortcuts = new StackPanel { Spacing = 8 };
        shortcuts.Children.Add(Part(note, actions)!);
        shortcuts.Children.Add(Part(SettingsCatalog.Special(SettingsCatalog.LinkShortcuts), actions)!);
        yield return Card(note.Section, shortcuts);
        yield return InfoCard(SettingsCatalog.Special(SettingsCatalog.InfoSongSettings), actions);
    }

    public static IEnumerable<Control> Advanced(SpecialActions actions)
    {
        foreach (var id in new[] { SettingsCatalog.InfoVersion, SettingsCatalog.InfoSettingsFile, SettingsCatalog.InfoCompatibility, SettingsCatalog.InfoArrangement }) yield return InfoCard(SettingsCatalog.Special(id), actions);
        var reset = SettingsCatalog.Special(SettingsCatalog.ActionResetAll);
        yield return Card(reset.Section, Part(reset, actions)!);
    }

    /// <summary>
    /// The control of a special entry, the same on its page and in search results: a link button, an info text or a reset button with its note.
    /// Null for the entries the Shortcuts page builds itself.
    /// </summary>
    public static Control? Part(SettingsEntry entry, SpecialActions actions)
    {
        switch (entry.Kind)
        {
            case SettingsEntryKind.Link:
                return LinkButton(entry, () => actions.Open(entry.Target!));
            case SettingsEntryKind.Info:
                return ToolWindowParts.Small(entry.Description);
            case SettingsEntryKind.Action when entry.Id == SettingsCatalog.ActionResetTheme:
                var theme = LinkButton(entry, actions.ResetTheme);
                theme.Classes.Add(AvaloniaFormControlStyles.PrefsFieldClass);
                return Stack(theme, ToolWindowParts.Small(entry.Description));
            case SettingsEntryKind.Action when entry.Id == SettingsCatalog.ActionResetAll:
                return Stack(ToolWindowParts.Small(entry.Description), LinkButton(entry, actions.ResetAll));
            case SettingsEntryKind.Action when entry.Id == SettingsCatalog.ActionManage:
                return new Button { Content = entry.Label, HorizontalAlignment = HorizontalAlignment.Left, Flyout = ManageMenu(actions.Footer) };
            case SettingsEntryKind.Action when SettingsCatalog.FooterActions.Contains(entry.Id):
                return LinkButton(entry, () => actions.Footer(entry.Id));
            default:
                return null;
        }
    }

    /// <summary>The preset box and the editable bindings of every command, grouped by category; <paramref name="changed"/> runs after each edit.
    /// With <paramref name="actionIds"/> only those commands show (and the preset box only when <paramref name="commands"/>), their cards captioned "Shortcuts › Category".</summary>
    public static IEnumerable<Control> Shortcuts(HotkeySettings settings, Action changed, IReadOnlySet<string>? actionIds = null, bool commands = true) =>
        new ShortcutsPage(settings, changed).Cards(actionIds, commands);

    private static Control InfoCard(SettingsEntry entry, SpecialActions actions) => Card(entry.Label, Part(entry, actions)!);

    private static StackPanel Stack(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.AddRange(children);
        return stack;
    }

    /// <summary>The Manage settings menu: import, export and reset all, each running its footer command through <paramref name="footer"/>.</summary>
    public static MenuFlyout ManageMenu(Action<string> footer) => new()
    {
        Items = { MenuCommand(SettingsCatalog.ActionImport, footer), MenuCommand(SettingsCatalog.ActionExport, footer), MenuCommand(SettingsCatalog.ActionResetAll, footer) },
    };

    private static MenuItem MenuCommand(string id, Action<string> footer)
    {
        var item = new MenuItem { Header = SettingsCatalog.Special(id).Label };
        item.Click += (_, _) => footer(id);
        return item;
    }

    private static Button LinkButton(SettingsEntry entry, Action click)
    {
        var button = new Button { Content = entry.Label, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => click();
        if (entry.Tip is { } tip) ToolTip.SetTip(button, tip);
        return button;
    }

    internal static Border Card(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        stack.Children.Add(content);
        var border = new Border { Name = title, Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = stack };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return border;
    }
}
