using Avalonia.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the pop-out window of a built-in effect editor: the frame (title, opening size, one per slot) around an editor panel; the editor does the rest.
// Does not own: the editors (BuiltinEffectPanel and its subclasses, IrLoaderEditor), the embedding in the FX chain window (BuiltinEditorHost).
// Tests: BuiltinEditorWindowTests.
public sealed class BuiltinEditorWindow : Window
{
    /// <summary>The chain slot the editor edits.</summary>
    public PluginSlot Slot { get; }

    /// <summary>The editor in this window.</summary>
    public Control Editor { get; }

    public BuiltinEditorWindow(PluginSlot slot, Control editor)
    {
        (Slot, Editor) = (slot, editor);
        var (title, size) = editor switch
        {
            BuiltinEffectPanel panel => (panel.EffectTitle, panel.PreferredSize),
            IrLoaderEditor ir => (ir.EffectTitle, ir.PreferredSize),
            _ => (slot.Name, (Width: 600d, Height: 400d, MinWidth: 300d, MinHeight: 200d)),
        };
        Title = title;
        (Width, Height, MinWidth, MinHeight) = size;
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        Content = editor;
    }
}
