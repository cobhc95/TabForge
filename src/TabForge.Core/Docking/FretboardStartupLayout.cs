using TabForge.Services;

namespace TabForge.Docking;

// Owns: the "Show fretboard at startup" choice (Remember last, Always, Never) and the start layout it gives; in Remember mode the
//   Tab mode's remembered value decides whether the instrument pane starts shown (the saved layout may have been written while the
//   piano roll or Band view hid it, so it must not decide alone).
// Does not own: the layout validation (DockLayoutTree) or the setting row (SettingsCatalog.Fretboard).
// Tests: DockStartupLayoutTests.
internal static class FretboardStartupLayout
{
    private const string InstrumentId = "instrument";

    /// <summary>The start layout for <paramref name="mode"/>: Always shows the fretboard, Never closes it, anything else keeps the saved state,
    /// with <paramref name="tabShown"/> (the Tab mode's remembered value) deciding the instrument pane in Remember mode.</summary>
    internal static DockWorkspaceState Resolve(DockWorkspaceState? saved, string? mode, Func<DockWorkspaceState> defaultLayout, bool? tabShown = null)
    {
        var usable = DockLayoutTree.IsUsableAtStart(saved);
        if (mode == FretboardStartupModes.Always) return Show(usable ? saved! : defaultLayout());
        var keepsClosed = KeptClosedUsable(saved);
        if (mode != FretboardStartupModes.Never)
        {
            var state = keepsClosed || usable ? saved! : defaultLayout();
            return tabShown is { } shown ? (shown ? Show(state) : Hide(state)) : state;
        }
        var copy = DockLayoutTree.Clone(keepsClosed || usable ? saved! : defaultLayout());
        DockWorkspaceRules.Close(copy, InstrumentId);
        return copy;
    }

    private static DockWorkspaceState Show(DockWorkspaceState state)
    {
        if (DockWorkspaceRules.PanelsOf(state).Contains(InstrumentId, StringComparer.Ordinal)) return state;
        var copy = DockLayoutTree.Clone(state);
        var row = DockPaneTable.Rows.First(row => row.Id == InstrumentId);
        DockWorkspaceRules.PlaceAtDefault(copy, InstrumentId, row.DefaultHost, row.DefaultAnchor);
        return copy;
    }

    private static DockWorkspaceState Hide(DockWorkspaceState state)
    {
        if (!DockWorkspaceRules.PanelsOf(state).Contains(InstrumentId, StringComparer.Ordinal))
        {
            if (state.ClosedPanels.Contains(InstrumentId, StringComparer.Ordinal)) return state;
        }
        var copy = DockLayoutTree.Clone(state);
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
