using TabForge.Services;

namespace TabForge.Docking;

// Owns: validating a workspace layout read from settings (limits, ids, tree shape) and clamping its sizes in place;
//   taking the Keyboard mode pane out of a layout that is saved or restored. Validation fails closed: any doubt refuses the layout.
// Does not own: reading the settings file (SettingsFileService), the dock views, building the Keyboard mode layout (KeyboardModeLayoutSwap).
// Tests: TestSecurityInputBoundaries, TestKeyboardModeLayout.
/// <summary>Validates and clamps a saved dock workspace; false means the layout is refused.</summary>
internal static class WorkspaceNormalizer
{
    /// <summary>The Keyboard mode pane id; it is never part of a saved layout.</summary>
    public const string KeyboardModePaneId = "learn";

    public static bool Normalize(DockWorkspaceState workspace)
    {
        // A list that JSON left null (hand-edited file) is an empty one, as a missing one is. This runs before anything enumerates the lists.
        workspace.Floating ??= new();
        workspace.ClosedPanels ??= new();
        if (!CoalescePanelLists(workspace.Root, 1)) return false;
        if (workspace.Floating.Any(floating => floating is null)) return false;
        foreach (var floating in workspace.Floating)
            if (!CoalescePanelLists(floating.Root, 1)) return false;
        if (!RemoveKeyboardModePane(workspace)) return false;
        if (workspace.Version != 1 ||
            workspace.Floating.Count > InputLimits.MaxWorkspaceFloatingWindows ||
            workspace.ClosedPanels.Count > InputLimits.MaxWorkspaceClosedPanels ||
            workspace.ClosedPanels.Any(id => !DockPaneTable.IsSavedId(id))) return false;

        var nodes = 0;
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        var panels = new HashSet<string>(StringComparer.Ordinal);
        var floatingIds = new HashSet<string>(StringComparer.Ordinal);
        if (!ValidateNode(workspace.Root, 1, ref nodes, hosts, panels)) return false;
        foreach (var floating in workspace.Floating)
        {
            if (!InputLimits.IsSafeText(floating.Id, 64, allowLineBreaks: false) ||
                string.IsNullOrWhiteSpace(floating.Id) || !floatingIds.Add(floating.Id) ||
                !ValidateNode(floating.Root, 1, ref nodes, hosts, panels)) return false;
            floating.Left = InputLimits.ClampFinite(floating.Left, -32_768, 32_768, 180);
            floating.Top = InputLimits.ClampFinite(floating.Top, -32_768, 32_768, 140);
            floating.Width = InputLimits.ClampFinite(floating.Width, 80, 4_096, 360);
            floating.Height = InputLimits.ClampFinite(floating.Height, 80, 4_096, 260);
        }
        return true;
    }

    /// <summary>Takes the Keyboard mode pane out of a layout that is about to be saved or restored. False when what is left is not a layout (no score, no Band view).</summary>
    public static bool RemoveKeyboardModePane(DockWorkspaceState state)
    {
        if (!DockLayoutTree.EnumerateAllPanels(state).Contains(KeyboardModePaneId)) return true;
        state.Root = DockLayoutTree.RemovePanel(state.Root, KeyboardModePaneId);
        foreach (var f in state.Floating) f.Root = DockLayoutTree.RemovePanel(f.Root, KeyboardModePaneId);
        state.Floating.RemoveAll(f => f.Root is null);
        state.ClosedPanels.RemoveAll(id => id == KeyboardModePaneId);
        return FindEditor(state.Root) is not null || DockLayoutTree.EnumerateAllPanels(state).Contains("band");
    }

    /// <summary>The score editor node of a tree, or null.</summary>
    public static DockNodeState? FindEditor(DockNodeState? node) =>
        node is null ? null : node.Kind == "editor" ? node : FindEditor(node.First) ?? FindEditor(node.Second);

    /// <summary>Gives each node of a tree an empty panel list where JSON left it null. Deeper than validation allows is refused here, which also stops a cyclic in-memory tree.</summary>
    private static bool CoalescePanelLists(DockNodeState? node, int depth)
    {
        if (node is null) return true;
        if (depth > InputLimits.MaxWorkspaceDepth) return false;
        node.Panels ??= new();
        return CoalescePanelLists(node.First, depth + 1) && CoalescePanelLists(node.Second, depth + 1);
    }

    private static bool ValidateNode(DockNodeState? node, int depth, ref int count,
        HashSet<string> hosts, HashSet<string> panels)
    {
        if (node is null) return true;
        if (++count > InputLimits.MaxWorkspaceNodes || depth > InputLimits.MaxWorkspaceDepth ||
            !InputLimits.IsSafeText(node.Kind, 16, allowLineBreaks: false) ||
            !InputLimits.IsSafeText(node.HostId, 64, allowLineBreaks: false) || string.IsNullOrWhiteSpace(node.HostId) ||
            !hosts.Add(node.HostId) || node.Panels.Count > InputLimits.MaxWorkspacePanelsPerNode ||
            !InputLimits.IsSafeText(node.Orientation, 16, allowLineBreaks: false)) return false;
        node.Ratio = InputLimits.ClampFinite(node.Ratio, 0.05, 0.95, 0.5);

        if (node.Kind == "split")
        {
            if (node.Orientation is not ("Horizontal" or "Vertical") || node.First is null || node.Second is null || node.Panels.Count != 0)
                return false;
        }
        else if (node.Kind == "tabs")
        {
            if (node.First is not null || node.Second is not null ||
                node.Panels.Any(id => !DockPaneTable.IsSavedId(id) || !panels.Add(id))) return false;
            if (node.SelectedPanel is not null && !node.Panels.Contains(node.SelectedPanel, StringComparer.Ordinal)) return false;
        }
        else if (node.Kind == "editor")
        {
            if (node.First is not null || node.Second is not null || node.Panels.Count != 0) return false;
        }
        else return false;

        return ValidateNode(node.First, depth + 1, ref count, hosts, panels) &&
               ValidateNode(node.Second, depth + 1, ref count, hosts, panels);
    }
}
