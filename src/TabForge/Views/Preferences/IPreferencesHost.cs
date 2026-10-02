using System.Windows;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>One built row of the page now showing: a setting or a shortcut, with the element to scroll to and highlight.</summary>
internal sealed record PageRow(string Category, SettingDescriptor? Setting, HotkeyAction? Hotkey, Border Element);

/// <summary>What the Preferences page builders need from the Settings window: the staged settings and the page plumbing.</summary>
internal interface IPreferencesHost
{
    /// <summary>The window itself: owner of dialogs and source of the theme resources.</summary>
    Window Dialog { get; }
    AppSettings Settings { get; }
    AppSettings Baseline { get; }
    Dictionary<string, SettingDescriptor> DescriptorsByKey { get; }
    Dictionary<string, SettingDescriptor> DefaultsByKey { get; }
    SettingsWindowActions? Actions { get; }

    void SettingChanged(SettingDescriptor? descriptor = null, object? value = null);
    void RebuildPage(string? focusSettingKey = null, string? focusHotkey = null);
    /// <summary>Rebuilds the descriptors from the staged settings (a new audio driver changes the device rows).</summary>
    void RefreshDescriptors();
    void SetStatus(string text);
    void AddTopCard(Border card, int columns);
    void AddRow(PageRow row);
    Border SettingRow(SettingDescriptor descriptor, bool searchResult);
    void NavigateToCategory(string category);
    void NavigateToHotkey(string id);
    void ResetAll();
    void ResetHotkeyCategory(IReadOnlyCollection<string> categories);
    void AddRecentColour(string hex);
}
