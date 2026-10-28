using TabForge.Services;

namespace TabForge.Docking;

// Owns: the "Show fretboard at startup" choice (Remember last, Always, Never) and the start layout it gives.
// Does not own: the layout validation (DockLayoutTree) or the setting row (SettingsCatalog.Fretboard).
// Tests: DockStartupLayoutTests.
internal static class FretboardStartupLayout
{
    private const string InstrumentId = "instrument";

    /// <summary>The start layout for <paramref name="mode"/>: Always shows the fretboard, Never closes it, anything else keeps the saved state (a pane closed last time stays closed).</summary>
    internal static DockWorkspaceState Resolve(DockWorkspaceState? saved, string? mode, Func<DockWorkspaceState> defaultLayout)
    {
        var usable = DockLayoutTree.IsUsableAtStart(saved);
        if (mode == FretboardStartupModes.Always) return usable ? saved! : defaultLayout();
        var keepsClosed = KeptClosedUsable(saved);
        if (mode != FretboardStartupModes.Never) return keepsClosed || usable ? saved! : defaultLayout();
        var copy = DockLayoutTree.Clone(keepsClosed || usable ? saved! : defaultLayout());
        DockWorkspaceRules.Close(copy, InstrumentId);
        return copy;
    }

    /// <summary>True when the layout is valid, has every required pane but the fretboard, and records the fretboard as closed.</summary>
    private static bool KeptClosedUsable(DockWorkspaceState? state)
    {
        if (state is null || !state.ClosedPanels.Contains(InstrumentId, StringComparer.Ordinal)) return false;
        var copy = DockLayoutTree.Clone(state);
        DockWorkspaceRules.Close(copy, InstrumentId);
        var shown = DockLayoutTree.EnumerateAllPanels(copy).ToHashSet(StringComparer.Ordinal);
        return DockLayoutTree.StartupRequiredPanels.Where(id => id != InstrumentId).All(shown.Contains)
            && DockLayoutTree.Validate(copy, hasEditorContent: true, id => DockPaneTable.Ids.Contains(id));
    }
}
