namespace TabForge.Services;

public enum SettingsEntryKind { Setting, Action, Link, Info, Shortcut }

/// <summary>
/// One visible thing on a Preferences page, as the single registry declares it: a setting row, a button, a link, an info card or a shortcut.
/// Owns: the page, section, label, description and the lower-case text the search reads.
/// Does not own: the control that shows it (the Preferences window) or the match rule (SettingsCatalog.Matches).
/// Tests: TabForge.Avalonia.Tests.SettingsSearchGuardTests.
/// </summary>
public sealed class SettingsEntry
{
    public SettingsEntry(string id, SettingsEntryKind kind, string page, string section, string label, string searchText,
        string description = "", string? target = null, string? tip = null, SettingDescriptor? row = null)
    {
        Id = id;
        Kind = kind;
        Page = page;
        Section = section;
        Label = label;
        SearchText = searchText;
        Description = description;
        Target = target;
        Tip = tip;
        Row = row;
    }

    public string Id { get; }
    public SettingsEntryKind Kind { get; }
    public string Page { get; }
    public string Section { get; }
    public string Label { get; }
    public string Description { get; }
    /// <summary>The page a link opens; null for the other kinds.</summary>
    public string? Target { get; }
    public string? Tip { get; }
    /// <summary>The catalogue row of a Setting entry; null for the other kinds.</summary>
    public SettingDescriptor? Row { get; }
    /// <summary>Everything that finds the entry, lower case.</summary>
    public string SearchText { get; }
}
