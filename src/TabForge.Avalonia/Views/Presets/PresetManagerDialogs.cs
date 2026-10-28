using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: the one question the preset manager asks (replace an existing name), behind a settable member so a test answers it without a modal box.
// Does not own: the actions (PresetManagerActions) or the window. Deleting asks nothing: it is done at once and offered back with Undo.
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The manager's questions; tests replace the members.</summary>
public sealed class PresetManagerDialogs
{
    /// <summary>A yes / no question (a replace of an existing name).</summary>
    public Func<string, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(false);

    /// <summary>The real dialogs of the app, over <paramref name="owner"/>.</summary>
    public static PresetManagerDialogs For(AvaloniaDialogHost host, Window owner) => new()
    {
        Confirm = async text => await AvaloniaMessageBox.Show(host, owner, text, PresetManagerWindow.Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes,
    };
}
