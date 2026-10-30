namespace TabForge.Services.Reset;

// Owns: the eight owner-approved ticks of "Reset entire app" and their wording.
// Does not own: what each tick maps to (ResetAppPlan) or the dialog (ResetAppWindow).
// Tests: ResetAppPlanTests, ResetAppPromptTests.
/// <summary>One group of app stores the reset dialog can wipe.</summary>
public enum ResetAppItemKind
{
    Settings,
    Layouts,
    Presets,
    PluginLibrary,
    Recents,
    Autosave,
    SoundFonts,
    LogsAndCaches,
}

/// <summary>The label and description of each tick; the eight labels are the owner's drawing, in order.</summary>
public static class ResetAppItemCatalog
{
    public static IReadOnlyList<ResetAppItemKind> All => Enum.GetValues<ResetAppItemKind>();

    public static string Title(ResetAppItemKind kind) => kind switch
    {
        ResetAppItemKind.Settings => "Settings and keyboard shortcuts",
        ResetAppItemKind.Layouts => "Window layouts and saved layouts",
        ResetAppItemKind.Presets => "Presets (effect presets, FX chains/containers, articulation maps, user templates and other preset stores)",
        ResetAppItemKind.PluginLibrary => "Plug-in library (approvals, scan cache)",
        ResetAppItemKind.Recents => "Remembered folders, recent files and last-used export/render options",
        ResetAppItemKind.Autosave => "Autosave and recovery files",
        ResetAppItemKind.SoundFonts => "Downloaded SoundFonts",
        ResetAppItemKind.LogsAndCaches => "Logs and caches",
        _ => kind.ToString(),
    };

    public static string Description(ResetAppItemKind kind) => kind switch
    {
        ResetAppItemKind.Settings => "Every option and keyboard shortcut in Settings, plus window sizes.",
        ResetAppItemKind.Layouts => "The workspace, closed panels, floating windows and every layout you saved.",
        ResetAppItemKind.Presets => "Your effect presets, FX chains/containers, MIDI presets, articulation maps, drum maps, templates and chain states.",
        ResetAppItemKind.PluginLibrary => "Plug-in approvals, fingerprints, scan cache and the list of plug-ins switched off after a crash.",
        ResetAppItemKind.Recents => "The last folder of each dialog, recent impulse-response files and last-used render/export options.",
        ResetAppItemKind.Autosave => "Crash recovery copies and their error lists.",
        ResetAppItemKind.SoundFonts => "The downloaded General MIDI SoundFont; the copy shipped beside the program is not touched.",
        ResetAppItemKind.LogsAndCaches => "Diagnostic and trace logs, crash logs, stale instance locks and rebuildable cache files.",
        _ => "",
    };
}
