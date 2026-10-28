using System;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the decision when the generic plug-in controls panel (FxParameterPanel) takes the place of a plug-in's own editor in the FX chain window:
//   which plug-ins have parameters to show, when the panel is forced (menu item "Show generic controls" or the Parameters button) and when it is the fallback
//   because the native editor could not open (the engine's "editor failed / not supported on this system" outcome, FxEditorController.EditorFailed).
// Does not own: the panel and its polling (FxParameterPanel, FxParameterPane), the failure detection (FxEditorController), the parameters (the engine).
// Tests: TabForge.Avalonia.Tests.FxGenericControlsTests.
public static class FxGenericControls
{
    /// <summary>The right-click entry of a plug-in row that opens the panel even when the plug-in's own editor works.</summary>
    public const string MenuText = "Show generic controls";

    /// <summary>Whether the engine can list parameters for a plug-in of this format (VST3 through the shim, CLAP, VST2). An empty format is a VST2 slot saved before formats existed.</summary>
    public static bool Supports(string? format) => string.IsNullOrEmpty(format) || PluginFormats.IsKnown(format);

    public static bool Supports(PluginSlot? slot) => slot is not null && Supports(slot.Format);

    /// <summary>Integration point for the native editor on this system: true when a docked plug-in editor can get a native window in the FX window (Windows: always). A system without one
    /// (Linux / macOS until their editor host exists) opens the generic panel at once instead of showing an empty area; whoever adds an editor host there widens this expression (or gives the area a handle).</summary>
    public static bool NativeEditorAreaSupported => OperatingSystem.IsWindows();

    /// <summary>Whether the docked editor cannot open here: the area has no native window and this system will not give it one.</summary>
    public static bool NativeEditorMissing(IntPtr area, bool supported) => area == IntPtr.Zero && !supported;

    /// <summary>Whether the panel may replace the editor area now: the plug-in is loaded (docked or in its own window); a blocked, quarantined or unloaded plug-in keeps its notice.</summary>
    public static bool CanForce(bool supported, FxEditorMode mode) => supported && mode is FxEditorMode.Docked or FxEditorMode.Floating;

    /// <summary>Whether the panel shows: forced by the user, or the fallback when the docked editor failed to open.</summary>
    public static bool ShouldShow(bool supported, FxEditorMode mode, bool forced, bool editorFailed) =>
        supported && ((forced && CanForce(supported, mode)) || (editorFailed && mode == FxEditorMode.Docked));
}
