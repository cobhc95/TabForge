namespace TabForge.Avalonia.Views.Fx;

// Owns: the one decision of whether the docked plug-in editor (the native surface and the engine's editor popup) may show.
//   It hides while the BYPASSED stamp covers the area, and while another window (the MIDI processing window) covers the FX chain window:
//   a docked editor is another process's top-level popup, so it would otherwise paint above the covering window.
// Does not own: applying it (FxEditorArea) or the native show/hide call (PluginEditorWin32).
// Tests: PluginEditorVisibilityTests.
public static class PluginEditorVisibility
{
    /// <summary>Whether the editor shows: not under the BYPASSED stamp and not while another window covers the FX window.</summary>
    public static bool EditorShows(bool stampShown, bool covered) => !stampShown && !covered;
}
