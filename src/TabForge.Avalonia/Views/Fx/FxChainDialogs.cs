using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the questions, messages and file pickers the FX chain window asks: one place, so a test replaces them and no test opens a modal box.
// Does not own: when a question is asked (the window and its helpers) or the boxes themselves (AvaloniaMessageBox, TextPromptWindow, IFileDialogs).
// Tests: TabForge.Avalonia.Tests.FxChainWindowTests (they replace the members and record the calls).
public sealed class FxChainDialogs
{
    /// <summary>Asks a question with buttons: owner, text, caption, buttons, icon.</summary>
    public Func<Window, string, string, MessageBoxButtons, MessageBoxIcon, Task<MessageBoxAnswer>> Ask { get; set; } =
        (owner, text, caption, buttons, icon) => AvaloniaMessageBox.Show(new AvaloniaDialogHost(), owner, text, caption, buttons, icon);

    /// <summary>Asks for one line of text: owner, title, label, initial text; null when cancelled.</summary>
    public Func<Window, string, string, string, Task<string?>> Prompt { get; set; } =
        (owner, title, label, initial) => new AvaloniaDialogHost().ShowModal<string?>(new TextPromptWindow(title, label, initial), owner);

    /// <summary>Shows the Add plug-in window on the host's real scan and returns the chosen plug-in, or null: owner, host.</summary>
    public Func<Window, IFxChainHost, Task<VstPluginInfo?>> ChoosePlugin { get; set; } = (owner, host) =>
        PluginBrowserWindow.ChooseAsync(owner, new PluginBrowserSession(host.PluginSettings, host.Trust, host.PluginFiles, host.SaveSettings), new AvaloniaFileDialogs(), host.OpenAudioSettings);

    /// <summary>Asks for the name of a preset of a plug-in (the Save preset window); null when cancelled: owner, plug-in.</summary>
    public Func<Window, PluginSlot, Task<string?>> PresetName { get; set; } = (owner, slot) =>
        new AvaloniaDialogHost().ShowModal<string?>(new PluginPresetWindow(new AvaloniaDialogHost(), slot.Name, () => PluginLibrary.PresetNames(slot), name => PluginLibrary.DeletePreset(slot, name)), owner);

    /// <summary>The file pickers (the Avalonia port in the app, a fake in tests).</summary>
    public IFileDialogs Files { get; set; } = new AvaloniaFileDialogs();

    /// <summary>Tells the user something: owner, text, caption.</summary>
    public Task Tell(Window owner, string text, string caption) => Ask(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
}
