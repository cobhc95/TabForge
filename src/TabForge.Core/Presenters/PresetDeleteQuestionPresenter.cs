// Owns: the words of the question asked before presets are deleted: the text for one preset or for several (up to five names, then
//       "and N more"), and the "Don't ask again" check box text.
// Does not own: the box (Avalonia PresetManagerWindow with AvaloniaMessageBox), the
//       "Ask before deleting presets" setting, or the delete itself.
// Tests: PresetManagerWindowTests (full suite), TabForge.Avalonia.Tests PresetManagerWindowTests.
namespace TabForge.Presenters;

public static class PresetDeleteQuestionPresenter
{
    /// <summary>The check box under the question; ticking it with Yes turns the question off.</summary>
    public const string RememberText = "Don't ask again when deleting presets";

    private const int ListedNames = 5;

    /// <summary>"Delete the preset "X"?" for one preset; "Delete 3 presets?" with the names, for several.</summary>
    public static string TextFor(IReadOnlyList<string> names)
    {
        if (names.Count == 1) return $"Delete the preset \"{names[0]}\"?";
        var listed = string.Join("\n", names.Take(ListedNames));
        var more = names.Count > ListedNames ? $"\nand {names.Count - ListedNames} more" : "";
        return $"Delete {names.Count} presets?\n\n{listed}{more}";
    }
}
