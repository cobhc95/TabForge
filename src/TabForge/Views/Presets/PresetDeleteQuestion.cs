using System.Windows;
using TabForge.Services;

namespace TabForge.Views.Presets;

// Owns: the question before presets are deleted, wherever a delete is asked (the preset manager, the drop-down rows, the manage panel,
//   the Save preset dialog): the text for one preset or several, the one decision whether to ask ("Ask before deleting presets"), and
//   storing the "Don't ask again" tick.
// Does not own: the delete itself (the stores, PresetManagePanel, PresetRows), the box (ThemedMessageBox) or the setting's row (SettingsCatalog).
// Tests: TestPresetManagerWindow.
internal static class PresetDeleteQuestion
{
    internal const string RememberText = "Don't ask again when deleting presets";
    private const int ListedNames = 5;

    /// <summary>"Delete the preset "X"?" for one preset; "Delete 3 presets?" with up to five names, then "and N more", for several.</summary>
    internal static string TextFor(IReadOnlyList<string> names)
    {
        if (names.Count == 1) return $"Delete the preset \"{names[0]}\"?";
        var listed = string.Join("\n", names.Take(ListedNames));
        var more = names.Count > ListedNames ? $"\nand {names.Count - ListedNames} more" : "";
        return $"Delete {names.Count} presets?\n\n{listed}{more}";
    }

    /// <summary>
    /// True when the presets may be deleted. Asks through <paramref name="ask"/> (text in, answer and tick out) unless the setting is off.
    /// Ticking the box and answering Yes turns the setting off and saves it; answering No stores nothing.
    /// </summary>
    internal static bool Confirm(IReadOnlyList<string> names, Func<string, (bool Yes, bool Remember)> ask)
    {
        var store = AppSettingsStore.Shared;
        if (names.Count == 0 || !store.Settings.General.ConfirmDeletePresets) return true;
        var (yes, remember) = ask(TextFor(names));
        if (yes && remember)
        {
            store.Settings.General.ConfirmDeletePresets = false;
            store.MarkChanged();
        }
        return yes;
    }

    /// <summary>The themed Yes / No box with the "Don't ask again" tick, over <paramref name="owner"/>.</summary>
    internal static (bool Yes, bool Remember) Ask(Window? owner, string text, string caption)
    {
        var (result, ticked) = ThemedMessageBox.ShowWithCheckBox(owner, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question, RememberText);
        return (result == MessageBoxResult.Yes, ticked);
    }
}
