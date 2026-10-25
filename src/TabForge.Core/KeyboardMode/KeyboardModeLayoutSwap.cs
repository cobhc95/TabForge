using System;
using System.Linq;
using TabForge.Docking;

namespace TabForge.KeyboardMode;

// Owns: the Keyboard mode dock arrangement as pure data: the layout for a size built from the layout in use;
//   the pane id "learn" is never kept in a saved layout (WorkspaceNormalizer removes it).
// Does not own: applying a layout (the window's Keyboard mode controller), the dock or the pane sizes (the dock's own height rules).
// Tests: TestKeyboardModeLayout, TabForge.Avalonia.Tests.MainWindowMenuTests.
public static class KeyboardModeLayoutSwap
{
    public const string PaneId = WorkspaceNormalizer.KeyboardModePaneId;
    private const string InstrumentId = "instrument";
    private const double KeysScoreNormal = 0.50, KeysScoreLarge = 0.30;

    /// <summary>The layout for Keyboard mode built from <paramref name="current"/> (which is not changed): the pane draws its own key strip, in the score's place (Full) or below the score (Normal, Large);
    /// the instrument pane is not shown docked or floating.</summary>
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
        // The Keyboard mode view draws its own key strip at the bottom, so the instrument pane is not shown docked or floating
        // (the arrangement it came from is put back on exit).
        state.Root = DockLayoutTree.RemovePanel(state.Root, InstrumentId);
        foreach (var floating in state.Floating) floating.Root = DockLayoutTree.RemovePanel(floating.Root, InstrumentId);
        state.Floating.RemoveAll(floating => floating.Root is null);
        var centre = KeyboardModeNode();
        var editor = WorkspaceNormalizer.FindEditor(state.Root);
        if (editor is null) { state.Root = state.Root is null ? centre : DockLayoutTree.Split("Vertical", 0.60, state.Root, centre); return; }
        var with = size == KeyboardModeSize.Full ? centre
            : DockLayoutTree.Split("Vertical", size == KeyboardModeSize.Large ? KeysScoreLarge : KeysScoreNormal, DockLayoutTree.CloneNode(editor)!, centre);
        DockLayoutTree.ReplaceNode(state, editor.HostId, with);
    }

    private static DockNodeState KeyboardModeNode() => DockLayoutTree.Tabs("learn-host", PaneId);
}
