// Owns: the plug-in "Save preset" dialog's logic without UI types: its texts, the case-insensitive name match (which saved name a typed
//       name hits, so whether Delete is on and whether OK asks to replace) and the replace question.
// Does not own: the window (Avalonia PluginPresetWindow), reading the plug-in state or the preset files (PluginLibrary).
// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
namespace TabForge.Presenters;

public static class PluginPresetNamePresenter
{
    public const string Title = "Save preset";
    public const string DefaultName = "My preset";
    public const string DeleteButton = "Delete";
    public const string DeleteTip = "Delete the chosen preset";
    public const string BuiltInError = "A preset that comes with the app has this name. Choose another name.";    public const string OkButton = "OK";
    public const string CancelButton = "Cancel";

    public static string Prompt(string pluginName) => $"Preset name for {pluginName}:";

    public const string BuiltInSuffix = " (built in)";

    /// <summary>How a built-in name shows in the shared save dialog's list: it cannot be picked as-is.</summary>
    public static string DisplayBuiltIn(string name) => name + BuiltInSuffix;

    /// <summary>Whether the row text is a built-in row (made by <see cref="DisplayBuiltIn"/>).</summary>
    public static bool IsBuiltInRow(string? row) => row is not null && row.EndsWith(BuiltInSuffix, StringComparison.Ordinal);

    /// <summary>The built-in name behind a built-in row, or null.</summary>
    public static string? BuiltInOfRow(string? row) =>
        IsBuiltInRow(row) ? row![..^BuiltInSuffix.Length] : null;

    /// <summary>The built-in name the text (trimmed) names, ignoring case, or null.</summary>
    public static string? BuiltInMatch(IEnumerable<string> builtIns, string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : builtIns.FirstOrDefault(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>"name copy", then "name copy 2"…: the first one no user preset has yet.</summary>
    public static string CopySuggestion(string name, IReadOnlyList<string> existing) => PresetRules.CopyName(name, existing);

    /// <summary>The inline note under the box: what typing this would do, or null when it cannot be saved.</summary>
    public static string ReplaceNote(string name) => $"This replaces the preset \"{name}\".";

    public static string ReplaceQuestion(string name) => $"Replace preset \"{name}\"?";

    /// <summary>The saved name the text (trimmed) names, ignoring case, or null.</summary>
    public static string? Match(IEnumerable<string> names, string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : names.FirstOrDefault(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the text (trimmed) names one of the names, ignoring case.</summary>
    public static bool Exists(IEnumerable<string> names, string? text) => Match(names, text) is not null;
}
