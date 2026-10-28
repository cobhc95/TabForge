using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: when a built-in's editor is embedded in the editor area (BuiltinEditorHost) and when the generic parameter panel shows in the FX chain window (FxGenericControls decides): for a plug-in whose own window did not open (the fallback; VST3, CLAP, VST2), and in place of the
//   editor while the Parameters button is down or after "Show generic controls" (right-click menu / FxChain.ShowGenericControls); and that button.
// Does not own: the panel and its polling (FxParameterPane, FxParameterPanel), whether an editor opened (FxEditorController), the window layout (FxChainWindow.Bar.cs).
// Tests: TabForge.Avalonia.Tests.FxParameterTests, FxGenericControlsTests.
public sealed partial class FxChainWindow
{
    private const string ParametersTip = "Sliders for the parameters this plug-in lists, instead of its own window. Changes can be undone in the panel.";
    private readonly FxParameterPane _parameters;
    private readonly Button _parametersButton = Btn("Parameters", ParametersTip, "ParametersButton");
    private readonly BuiltinEditorHost _builtinEditor = new();
    private bool _parametersDown;
    private bool _fallbackOff;

    /// <summary>The generic parameter panel (visible while it shows).</summary>
    public FxParameterPanel ParameterPanel => _parameters.Panel;

    /// <summary>Whether the Parameters button is down.</summary>
    public bool ParametersDown => _parametersDown;

    private void WireParameters()
    {
        _parametersButton.Click += (_, _) =>
        {
            if (BuiltinEditors.IsBuiltin(Selected)) { _parametersDown = false; ShowSelected(); BuiltinEditors.TryOpen(this, _host, _track, Selected!); return; }   // a built-in: the button pops its editor out (from the generic controls it also brings the editor back)
            if (_parameters.Panel.IsVisible && !_parametersDown) _fallbackOff = true;   // the panel is only the fallback for a failed editor: the button brings the failure notice (Retry ...) back
            else { _parametersDown = !_parametersDown; _fallbackOff = false; }
            ShowSelected();
        };
        _editors.EditorFailed += slot => { if (FxGenericControls.Supports(slot) && ReferenceEquals(slot, Selected)) _ui.Post(ShowSelected, TabForge.Platform.UiPriority.ApplicationIdle); };
    }

    /// <summary>"Show generic controls": the panel opens for the focused plug-in even when its own editor works (the Parameters button goes down; it stays so for the other rows until pressed again).</summary>
    private bool ShowGenericControls()
    {
        if (!FxGenericControls.Supports(Selected)) return false;
        _parametersDown = !BuiltinEditors.IsBuiltin(Selected) || !_parametersDown;   // a built-in's editor and the generic controls swap with each use
        ShowSelected();
        return true;
    }

    /// <summary>Shows the editor area, or in its place the parameter panel; called with the focused plug-in after the editor state was settled.</summary>
    private void ShowParameters(PluginSlot? slot, bool viaButton)
    {
        var supported = FxGenericControls.Supports(slot);
        var builtin = BuiltinEditors.IsBuiltin(slot);
        _parametersButton.IsVisible = supported && (!builtin || BuiltinEditors.HasWindow(slot));
        _parametersButton.Content = builtin ? "Open editor" : "Parameters";
        ToolTip.SetTip(_parametersButton, builtin ? "Pops this effect's editor out into its own window." : ParametersTip);
        if (viaButton && !builtin) _parametersButton[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        else _parametersButton.ClearValue(Button.BackgroundProperty);
        var show = slot is not null && (viaButton || FxGenericControls.ShouldShow(supported, _editors.ModeOf(slot), false, _editors.HasFailed(slot) && !_fallbackOff));
        var embed = !show && slot is not null && BuiltinEditors.HasWindow(slot);   // a built-in's editor takes the editor area
        _area.IsVisible = !show && !embed;
        _parameters.Panel.IsVisible = show;
        if (embed) _builtinEditor.Show(_host, _track, slot!); else _builtinEditor.Clear();
        if (show) _parameters.Open(slot!);
        else _parameters.Close();
    }
}
