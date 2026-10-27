using TabForge.Presenters;

namespace TabForge.Services;

// Owns: the one Preferences search: the registry of every visible entry, the text each entry is found by, the synonyms and the match rule.
// Does not own: the rows (the SettingsCatalog.*.cs files), the special entries' wording (SettingsCatalog.Special.cs) or showing results (PreferencesWindow).
// Tests: TabForge.Avalonia.Tests.SettingsSearchGuardTests.
public static partial class SettingsCatalog
{
    /// <summary>Everyday words that find an entry by another name: an alias -> the words the entry's text uses (the alias itself still matches).</summary>
    private static string[] HeadsOf(string alias) => alias switch
    {
        "dark" or "light" or "skin" or "mode" or "colour" => new[] { "theme" },
        "color" or "colors" => new[] { "colour", "theme" },
        "soundcard" or "speakers" or "headphones" or "interface" or "output" or "driver" or "device" => new[] { "audio" },
        "hotkey" or "hotkeys" or "keybinding" or "keybindings" => new[] { "shortcuts" },
        "latency" or "delay" or "lag" or "sync" => new[] { "buffer" },
        "scroll" or "jump" or "page-turn" => new[] { "follow" },
        "measure" => new[] { "bar" },
        "click" or "count-in" => new[] { "metronome" },
        "neck" or "frets" => new[] { "fretboard" },
        "input" or "take" => new[] { "recording" },
        "microphone" or "mic" => new[] { "recording", "input" },
        "plugin" or "plugins" or "plug-in" or "vst" => new[] { "plug-ins" },
        "backup" or "recovery" => new[] { "autosave" },
        "text" or "typeface" or "size" => new[] { "font" },
        _ => Array.Empty<string>(),
    };

    /// <summary>Words only a colour row's controls carry (the swatch's "Choose ..." name, the preset box and the hex box).</summary>
    private static string KindWords(SettingKind kind) => kind == SettingKind.Colour ? "choose colour picker swatch preset hex" : "";

    /// <summary>The lower-case text an entry is found by: its page and the page's other names, then every part given.</summary>
    private static string SearchTextOf(string page, params string?[] parts)
    {
        PageWords.TryGetValue(page, out var pageWords);
        return string.Join(' ', parts.Prepend(page).Append(pageWords).Where(part => !string.IsNullOrEmpty(part))).ToLowerInvariant();
    }

    /// <summary>
    /// A query matches when it passes the chosen <paramref name="category"/> (a Preferences page; null for all; shortcuts belong to the Shortcuts page) and every word of it
    /// is found (itself or through a synonym) in the entry's text, in any order.
    /// </summary>
    public static bool Matches(SettingsEntry entry, string lowerCaseQuery, string? category = null)
    {
        if (!PreferencesSearchRules.PassesCategory(category, entry.Page)) return false;
        if (lowerCaseQuery.Length == 0 || entry.SearchText.Contains(lowerCaseQuery, StringComparison.Ordinal)) return true;
        foreach (var word in lowerCaseQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.SearchText.Contains(word, StringComparison.Ordinal)) continue;
            if (HeadsOf(word).Any(head => entry.SearchText.Contains(head, StringComparison.Ordinal))) continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Every entry the Preferences pages show: the catalogue <paramref name="rows"/>, the buttons, links and info cards the special pages build,
    /// and one entry per shortcut found by its name, category and current keys.
    /// </summary>
    public static List<SettingsEntry> Registry(IEnumerable<SettingDescriptor> rows, HotkeySettings hotkeys)
    {
        var entries = rows.Select(row => new SettingsEntry(row.Key, SettingsEntryKind.Setting, row.Category, row.Group, row.Title, row.SearchText, row.Description, row: row)).ToList();
        entries.AddRange(SpecialEntries());
        entries.AddRange(HotkeyCatalog.All.Select(action => ShortcutEntry(action, hotkeys)));
        return entries;
    }

    private static SettingsEntry ShortcutEntry(HotkeyAction action, HotkeySettings hotkeys) =>
        new(action.Id, SettingsEntryKind.Shortcut, Hotkeys, action.Category, action.Name,
            SearchTextOf(Hotkeys, action.Name, action.Description, action.Category, action.Id, HotkeyCatalog.DisplayAll(hotkeys, action.Id), ShortcutButtonLabels(action)), action.Description);
}
