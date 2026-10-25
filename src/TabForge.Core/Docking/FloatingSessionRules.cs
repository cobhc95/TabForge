using TabForge.Platform;

namespace TabForge.Docking;

// Owns: the state changes of a floating dock window's life: open a pane floating, the user closing or redocking it (panels go back to their default places), and fitting restored bounds into the work area.
// Does not own: the floating windows (src/TabForge.Avalonia/Views/Dock/DockWorkspaceView.cs and DockFloatingWindow.cs) or the placement maths (FloatingPlacementRules).
// Tests: no dedicated test class.
internal static class FloatingSessionRules
{
    /// <summary>Takes the panel out of the layout and opens it in a new floating root (a new id, its tab host named <paramref name="hostId"/>); returns the floating roots left empty.</summary>
    internal static List<string> OpenFloating(DockWorkspaceState state, string panelId, string hostId, UiRect bounds, out DockFloatingState floating)
    {
        var emptied = DockWorkspaceRules.RemoveEverywhere(state, panelId);
        state.ClosedPanels.Remove(panelId);
        floating = new DockFloatingState
        {
            Root = DockLayoutTree.Tabs(hostId, panelId),
            Left = bounds.X,
            Top = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };
        state.Floating.Add(floating);
        return emptied;
    }

    /// <summary>The user closed (or redocked) the floating window: its root is removed and each panel goes back to its default host
    /// (a panel with no default place is dropped from the layout). Returns the floating roots left empty.</summary>
    internal static List<string> CloseByUser(DockWorkspaceState state, string floatingId, Func<string, (string Host, string Anchor)?> defaultPlace)
    {
        var emptied = new List<string>();
        var floating = state.Floating.FirstOrDefault(f => f.Id == floatingId);
        if (floating is null) return emptied;
        var panels = DockWorkspaceState.EnumeratePanelIds(floating.Root).ToList();
        state.Floating.Remove(floating);
        foreach (var panel in panels)
        {
            if (defaultPlace(panel) is { } place)
            {
                emptied.AddRange(DockWorkspaceRules.PlaceAtDefault(state, panel, place.Host, place.Anchor));
                continue;
            }
            emptied.AddRange(DockWorkspaceRules.RemoveEverywhere(state, panel));
            state.ClosedPanels.Remove(panel);
        }
        return emptied;
    }

    /// <summary>Makes every floating window's saved bounds finite and inside the work area near it (a restored position can sit on a monitor that is gone).</summary>
    internal static void FitToWorkAreas(DockWorkspaceState state, IMonitorWorkAreas areas, double minWidth, double minHeight)
    {
        foreach (var floating in state.Floating)
        {
            var bounds = FloatingPlacementRules.FiniteBounds(floating.Left, floating.Top, floating.Width, floating.Height, minWidth, minHeight);
            var fitted = FloatingPlacementRules.FitToWorkArea(bounds, areas.WorkAreaNear(bounds));
            floating.Left = fitted.X;
            floating.Top = fitted.Y;
            floating.Width = fitted.Width;
            floating.Height = fitted.Height;
        }
    }
}
