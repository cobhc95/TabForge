using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: the questions the preset manager asks and the "Ask before deleting presets" setting it reads and writes, behind small settable
//   members so a test answers them without a modal box.
// Does not own: the question words (PresetDeleteQuestionPresenter), the actions (PresetManagerActions) or the window.
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The manager's questions and its delete-setting seam; tests replace the members.</summary>
public sealed class PresetManagerDialogs
{
    /// <summary>A yes / no question (a replace of an existing name).</summary>
    public Func<string, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(false);
    /// <summary>The delete question over every doomed name: the answer and whether "Don't ask again" was ticked.</summary>
    public Func<string, Task<(bool Yes, bool Remember)>> AskDelete { get; set; } = _ => Task.FromResult((false, false));
    /// <summary>Reads General.ConfirmDeletePresets at the time of a delete (the window stays open).</summary>
    public Func<bool> ConfirmDeletePresets { get; set; } = () => true;
    /// <summary>Turns the setting off and saves it: only a Yes with the tick does.</summary>
    public Action<bool> RememberDeletePresets { get; set; } = _ => { };

    /// <summary>The real dialogs of the app, over <paramref name="owner"/>.</summary>
    public static PresetManagerDialogs For(AvaloniaDialogHost host, Window owner, Func<bool>? confirmDeletePresets, Action<bool>? rememberDeletePresets)
    {
        var dialogs = new PresetManagerDialogs
        {
            ConfirmDeletePresets = confirmDeletePresets ?? (() => true),
            RememberDeletePresets = rememberDeletePresets ?? (_ => { }),
        };
        dialogs.Confirm = async text =>
            await AvaloniaMessageBox.Show(host, owner, text, PresetManagerWindow.Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes;
        dialogs.AskDelete = text => PresetDeleteQuestion.AskAsync(host, owner, text);
        return dialogs;
    }
}
