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
    public const string OkButton = "OK";
    public const string CancelButton = "Cancel";

    public static string Prompt(string pluginName) => $"Preset name for {pluginName}:";

    public static string ReplaceQuestion(string name) => $"Replace preset \"{name}\"?";

    /// <summary>The saved name the text (trimmed) names, ignoring case, or null.</summary>
    public static string? Match(IEnumerable<string> names, string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : names.FirstOrDefault(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the text (trimmed) names one of the names, ignoring case.</summary>
    public static bool Exists(IEnumerable<string> names, string? text) => Match(names, text) is not null;
}
