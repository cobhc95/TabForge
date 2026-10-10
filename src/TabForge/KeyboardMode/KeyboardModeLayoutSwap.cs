using TabForge.Docking;

namespace TabForge.KeyboardMode;

/// <summary>How much of the window Keyboard mode takes.</summary>
public enum KeyboardModeSize { Normal, Large, Full }

// Owns: the Keyboard mode dock arrangement as pure data: the layout for a size built from the layout in use,
//   and the sanitising of a saved layout that carries the pane. The pane id "learn" is never kept in a saved layout.
// Does not own: applying a layout (KeyboardModeController), the dock or the pane sizes (the dock's own height rules).
// Tests: TestKeyboardModeLayout.
internal static class KeyboardModeLayoutSwap
{
    public const string PaneId = "learn";
    private const string InstrumentId = "instrument";
    private const double KeysScoreNormal = 0.50, KeysScoreLarge = 0.30;

    /// <summary>The layout for Keyboard mode built from <paramref name="current"/> (which is not changed): the pane draws its own key strip, in the score's place (Full) or below the score (Normal, Large);
    /// the keyboard pane is not shown.</summary>
    public static DockWorkspaceState Build(DockWorkspaceState current, KeyboardModeSize size)
    {
        var state = DockLayoutTree.Clone(current);
        state.ClosedPanels.RemoveAll(id => id == PaneId);
        KeysLayout(state, size);
        var present = DockLayoutTree.EnumerateAllPanels(state).ToHashSet(StringComparer.Ordinal);
        if (!present.Contains(InstrumentId) && !state.ClosedPanels.Contains(InstrumentId)) state.ClosedPanels.Add(InstrumentId);
        state.ClosedPanels.RemoveAll(present.Contains);
        return state;
    }

    private static void KeysLayout(DockWorkspaceState state, KeyboardModeSize size)
    {
        // The Keyboard mode view draws its own key strip at the bottom, so the keyboard pane is not shown (the arrangement it came from is put back on exit).
        state.Root = DockLayoutTree.RemovePanel(state.Root, InstrumentId);
        var centre = KeyboardModeNode();
        var editor = FindEditor(state.Root);
        if (editor is null) { state.Root = state.Root is null ? centre : DockLayoutTree.Split("Vertical", 0.60, state.Root, centre); return; }
        var with = size == KeyboardModeSize.Full ? centre
            : DockLayoutTree.Split("Vertical", size == KeyboardModeSize.Large ? KeysScoreLarge : KeysScoreNormal, DockLayoutTree.CloneNode(editor)!, centre);
        DockLayoutTree.ReplaceNode(state, editor.HostId, with);
    }

    private static DockNodeState KeyboardModeNode() => DockLayoutTree.Tabs("learn-host", PaneId);

    private static DockNodeState? FindEditor(DockNodeState? node) =>
        node is null ? null : node.Kind == "editor" ? node : FindEditor(node.First) ?? FindEditor(node.Second);

    /// <summary>Takes the Keyboard mode pane out of a layout that is about to be saved or restored. False when what is left is not a layout (no score, no Band view).</summary>
    public static bool Sanitise(DockWorkspaceState state)
    {
        if (!DockLayoutTree.EnumerateAllPanels(state).Contains(PaneId)) return true;
        state.Root = DockLayoutTree.RemovePanel(state.Root, PaneId);
        foreach (var f in state.Floating) f.Root = DockLayoutTree.RemovePanel(f.Root, PaneId);
        state.Floating.RemoveAll(f => f.Root is null);
        state.ClosedPanels.RemoveAll(id => id == PaneId);
        return FindEditor(state.Root) is not null || DockLayoutTree.EnumerateAllPanels(state).Contains("band");
    }
}
