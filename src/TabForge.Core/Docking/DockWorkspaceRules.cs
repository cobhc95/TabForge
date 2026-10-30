namespace TabForge.Docking;

// Owns: the workspace rules on the saved layout: restore (saved state to a valid tree), capture (tree to the state that is saved),
//   PanelsOf / IsVisible, the show / hide / close / reset changes with the ClosedPanels list, and DropPanel (a panel dropped in a zone).
// Does not own: the tree editing primitives (DockLayoutTree), the views, floating windows and their geometry (DockWorkspace in the
//   app), or the height and fit maths.
// Tests: no dedicated test class.
/// <summary>Toolkit-free rules that turn a saved <see cref="DockWorkspaceState"/> into the live tree and back. Each change returns
/// the ids of floating roots it emptied so the host can close their windows.</summary>
internal static class DockWorkspaceRules
{
    /// <summary>The live tree for a saved layout: unknown panels dropped, an invalid tree replaced by the default, closed ids cleaned,
    /// and every registered panel that is neither present, closed nor closed-by-default placed at its default.</summary>
    internal static DockWorkspaceState Restore(DockWorkspaceState? saved, bool hasEditorContent, IReadOnlyCollection<string> registered,
        Func<string, bool> startsClosed, Func<string, (string Host, string Anchor)> defaultPlace, List<string> emptiedFloating)
    {
        bool IsRegistered(string id) => registered.Contains(id);
        var state = saved?.Root is null ? DockLayoutTree.CreateDefaultState() : DockLayoutTree.Clone(saved);
        DockLayoutTree.DropPanels(state, IsRegistered);
        if (!DockLayoutTree.Validate(state, hasEditorContent, IsRegistered)) state = DockLayoutTree.CreateDefaultState();
        state.ClosedPanels = state.ClosedPanels.Where(IsRegistered).Distinct(StringComparer.Ordinal).ToList();

        var present = DockLayoutTree.EnumerateAllPanels(state).ToHashSet(StringComparer.Ordinal);
        foreach (var id in registered)
        {
            if (present.Contains(id) || state.ClosedPanels.Contains(id, StringComparer.Ordinal) || startsClosed(id)) continue;
            var (host, anchor) = defaultPlace(id);
            emptiedFloating.AddRange(PlaceAtDefault(state, id, host, anchor));
        }
        return state;
    }

    /// <summary>The state that is saved: a copy whose splits carry the user's ratios, not the live auto-fitted ones.</summary>
    internal static DockWorkspaceState Capture(DockWorkspaceState state)
    {
        var result = DockLayoutTree.Clone(state);
        DockLayoutTree.UseUserRatios(result.Root);
        foreach (var floating in result.Floating) DockLayoutTree.UseUserRatios(floating.Root);
        return result;
    }

    /// <summary>Every panel in a layout (docked and floating).</summary>
    internal static IEnumerable<string> PanelsOf(DockWorkspaceState state) => DockLayoutTree.EnumerateAllPanels(state);

    internal static bool IsVisible(DockWorkspaceState state, string id) => PanelsOf(state).Contains(id, StringComparer.Ordinal);

    /// <summary>Removes the panel from the main root and every floating root; returns the ids of the floating roots left empty (removed).</summary>
    internal static List<string> RemoveEverywhere(DockWorkspaceState state, string id)
    {
        var emptied = new List<string>();
        state.Root = DockLayoutTree.RemovePanel(state.Root, id);
        foreach (var floating in state.Floating.ToArray())
        {
            floating.Root = DockLayoutTree.RemovePanel(floating.Root, id);
            if (floating.Root is not null) continue;
            state.Floating.Remove(floating);
            emptied.Add(floating.Id);
        }
        return emptied;
    }

    /// <summary>Takes the panel out of the layout and remembers it as closed. The instrument pane's share is remembered first so showing it again keeps its size.</summary>
    internal static List<string> Close(DockWorkspaceState state, string id)
    {
        if (id == InstrumentDockSizeMemory.InstrumentId) DockLayoutTree.RememberInstrumentShare(state);
        var emptied = RemoveEverywhere(state, id);
        if (!state.ClosedPanels.Contains(id, StringComparer.Ordinal)) state.ClosedPanels.Add(id);
        return emptied;
    }

    /// <summary>Closes every registered, visible panel of <paramref name="ids"/>; false when none was visible.</summary>
    internal static bool Hide(DockWorkspaceState state, IEnumerable<string> ids, Func<string, bool> isRegistered, List<string> emptiedFloating)
    {
        var any = false;
        foreach (var id in ids)
        {
            if (!isRegistered(id) || !IsVisible(state, id)) continue;
            emptiedFloating.AddRange(Close(state, id));
            any = true;
        }
        return any;
    }

    /// <summary>Moves a visible panel into a zone of a host in the main root (<paramref name="floatingId"/> null) or a floating root:
    /// Center joins the host's tabs at <paramref name="tabIndex"/>, an edge splits the host with a new "dock-&lt;guid&gt;" tab group
    /// taking <paramref name="fraction"/> (clamped 0.02-0.98). False when the panel is hidden, the host is missing, Center targets
    /// anything but tabs, or the panel sits alone on the host; the tree is untouched then. The panel's emptied floating roots are added
    /// to <paramref name="emptiedFloating"/>.</summary>
    internal static bool DropPanel(DockWorkspaceState state, string panelId, string targetHostId, DockDropZone zone,
        string? floatingId, double fraction, int? tabIndex, List<string> emptiedFloating)
    {
        DockNodeState? FindTarget() => floatingId is null
            ? DockLayoutTree.FindNode(state.Root, targetHostId)
            : state.Floating.FirstOrDefault(f => f.Id == floatingId)?.Root is { } floatRoot ? DockLayoutTree.FindNode(floatRoot, targetHostId) : null;

        if (!IsVisible(state, panelId)) return false;
        var initialTarget = FindTarget();
        if (initialTarget is null || zone == DockDropZone.Center && initialTarget.Kind != "tabs") return false;
        if (initialTarget.Kind == "tabs" && initialTarget.Panels.Count == 1 && initialTarget.Panels[0] == panelId) return false;
        if (initialTarget.Kind == "tabs" && initialTarget.Panels.Contains(panelId, StringComparer.Ordinal) && tabIndex > initialTarget.Panels.IndexOf(panelId))
            tabIndex--;

        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0.02, 0.98) : DockLayoutTree.EdgeFraction;
        if (panelId == InstrumentDockSizeMemory.InstrumentId && zone != DockDropZone.Center)
        {
            var targetVertical = InstrumentDockSizeMemory.IsVerticalZone(zone);
            var before = DockLayoutTree.FindInstrumentShare(state);
            double? currentWhenSame = before is { } b && b.Vertical == targetVertical ? b.Share : null;
            if (before is { } found) DockLayoutTree.RememberInstrumentShare(state);
            var rememberedTarget = targetVertical ? state.InstrumentVerticalShare : state.InstrumentHorizontalShare;
            fraction = InstrumentDockSizeMemory.ShareToApply(currentWhenSame, rememberedTarget);
        }
        emptiedFloating.AddRange(RemoveEverywhere(state, panelId));
        state.ClosedPanels.Remove(panelId);
        var target = FindTarget();
        if (target is null) return false;   // the target was a floating root the panel emptied
        if (zone == DockDropZone.Center)
        {
            DockLayoutTree.AddToTabs(target, panelId, tabIndex);
            return true;
        }
        var panelNode = DockLayoutTree.Tabs("dock-" + Guid.NewGuid().ToString("N"), panelId);
        var orientation = zone is DockDropZone.Left or DockDropZone.Right ? "Horizontal" : "Vertical";
        var split = zone is DockDropZone.Left or DockDropZone.Top
            ? DockLayoutTree.Split(orientation, fraction, panelNode, DockLayoutTree.CloneNode(target)!)
            : DockLayoutTree.Split(orientation, 1 - fraction, DockLayoutTree.CloneNode(target)!, panelNode);
        if (floatingId is null) DockLayoutTree.ReplaceNode(state, target.HostId, split);
        else
        {
            var floatState = state.Floating.First(f => f.Id == floatingId);
            floatState.Root = DockLayoutTree.ReplaceNode(floatState.Root, target.HostId, split);
        }
        if (panelId == InstrumentDockSizeMemory.InstrumentId)
        {
            var vertical = InstrumentDockSizeMemory.IsVertical(orientation);
            if (vertical) state.InstrumentVerticalShare = InstrumentDockSizeMemory.RememberShare(fraction, state.InstrumentVerticalShare);
            else state.InstrumentHorizontalShare = InstrumentDockSizeMemory.RememberShare(fraction, state.InstrumentHorizontalShare);
        }
        return true;
    }

    /// <summary>Moves the panel back to its default host (out of the closed list). The instrument pane's share is remembered first so it keeps its size.</summary>
    internal static List<string> PlaceAtDefault(DockWorkspaceState state, string id, string defaultHost, string defaultAnchor)
    {
        if (id == InstrumentDockSizeMemory.InstrumentId) DockLayoutTree.RememberInstrumentShare(state);
        var emptied = RemoveEverywhere(state, id);
        state.ClosedPanels.Remove(id);
        DockLayoutTree.PlaceAtDefault(state, id, defaultHost, defaultAnchor);
        return emptied;
    }
}
