using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: when the generic parameter panel shows in the FX chain window: for a CLAP plug-in whose window did not open (it has no editor), and in place of the editor while the
//   Parameters button is down; and that button.
// Does not own: the panel and its polling (FxParameterPane, FxParameterPanel), whether an editor opened (FxEditorController), the window layout (FxChainWindow.Bar.cs).
// Tests: TabForge.Avalonia.Tests.FxParameterTests.
public sealed partial class FxChainWindow
{
    private const string ParametersTip = "Sliders for the parameters this plug-in lists, instead of its own window (CLAP). Changes can be undone in the panel.";
    private readonly FxParameterPane _parameters;
    private readonly Button _parametersButton = Btn("Parameters", ParametersTip, "ParametersButton");
    private bool _parametersDown;

    /// <summary>The generic parameter panel (visible while it shows).</summary>
    public FxParameterPanel ParameterPanel => _parameters.Panel;

    /// <summary>Whether the Parameters button is down.</summary>
    public bool ParametersDown => _parametersDown;

    private static bool IsClap(PluginSlot? slot) => slot is not null && string.Equals(slot.Format, PluginFormats.Clap, System.StringComparison.OrdinalIgnoreCase);

    private void WireParameters()
    {
        _parametersButton.Click += (_, _) => { _parametersDown = !_parametersDown; ShowSelected(); };
        _editors.EditorFailed += slot => { if (IsClap(slot) && ReferenceEquals(slot, Selected)) _ui.Post(ShowSelected, TabForge.Platform.UiPriority.ApplicationIdle); };
    }

    /// <summary>Shows the editor area, or in its place the parameter panel; called with the focused plug-in after the editor state was settled.</summary>
    private void ShowParameters(PluginSlot? slot, bool viaButton)
    {
        _parametersButton.IsVisible = IsClap(slot);
        if (viaButton) _parametersButton[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
        else _parametersButton.ClearValue(Button.BackgroundProperty);
        var show = slot is not null && (viaButton || (IsClap(slot) && _editors.HasFailed(slot)));
        _area.IsVisible = !show;
        _parameters.Panel.IsVisible = show;
        if (show) _parameters.Open(slot!);
        else _parameters.Close();
    }
}
