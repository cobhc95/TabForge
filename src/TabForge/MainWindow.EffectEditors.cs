using System.Windows;
using TabForge.Services;
using TabForge.Views.EffectEditors;
using TabForge.Views.Score;

namespace TabForge;

// The note-effect editors' window side: the host interface and the menu / palette / hotkey entry (EffectEditorFlow does the work).
public partial class MainWindow : IEffectEditorHost
{
    private EffectEditorFlow? _effectEditors;

    private void OpenEffectEditor(EffectEditorKind kind)
    {
        var flow = _effectEditors ??= new EffectEditorFlow(this);
        if (kind is EffectEditorKind.Bend or EffectEditorKind.TremoloBar) flow.Open(kind); else flow.OpenOrnament(kind);
    }

    private bool RunEffectEditorHotkey(string id)
    {
        if (EffectEditorFlow.KindOf(id) is not { } kind) return false;
        if (!(_effectEditors ??= new EffectEditorFlow(this)).ToggledOff(id)) OpenEffectEditor(kind);
        return true;
    }

    /// <summary>Effects menu items: the item's Tag is the editor kind.</summary>
    private void EffectEditor_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<EffectEditorKind>(tag, out var kind)) OpenEffectEditor(kind); }

    Window IEffectEditorHost.Owner => this;
    AppSettings IEffectEditorHost.Settings => _settings;
    ScoreEditCommands? IEffectEditorHost.Edits => Editor?.Effects;
    void IEffectEditorHost.SaveSettings() => SaveSettings();
    void IEffectEditorHost.Say(string text) => StatusText.Text = text;
}
