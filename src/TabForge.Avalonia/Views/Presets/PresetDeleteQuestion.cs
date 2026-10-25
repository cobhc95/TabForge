using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: asking before presets are deleted: the one decision whether to ask ("Ask before deleting presets"), the question's words
//   (PresetDeleteQuestionPresenter) and storing the "Don't ask again" tick. Only a Yes with the tick turns the setting off.
// Does not own: the delete itself (PresetManagerActions and the stores) or the box (AvaloniaMessageBox, through AvaloniaDialogHost).
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The delete question and its "Ask before deleting presets" setting, in the view layer.</summary>
public static class PresetDeleteQuestion
{
    /// <summary>True when the presets may be deleted; asks unless the setting is already off.</summary>
    public static async Task<bool> ConfirmAsync(IReadOnlyList<string> names,
        Func<string, Task<(bool Yes, bool Remember)>> ask, Func<bool> confirmSetting, Action<bool> rememberNoAsk)
    {
        if (names.Count == 0 || !confirmSetting()) return true;
        var (yes, remember) = await ask(PresetDeleteQuestionPresenter.TextFor(names));
        if (yes && remember) rememberNoAsk(false);
        return yes;
    }

    /// <summary>The real question over <paramref name="owner"/>: Yes / No with the "Don't ask again" tick.</summary>
    public static async Task<(bool Yes, bool Remember)> AskAsync(AvaloniaDialogHost host, Window owner, string text)
    {
        var (answer, ticked) = await AvaloniaMessageBox.ShowWithCheckBox(host, owner, text, PresetManagerWindow.Caption,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, PresetDeleteQuestionPresenter.RememberText);
        return (answer == MessageBoxAnswer.Yes, ticked);
    }
}
