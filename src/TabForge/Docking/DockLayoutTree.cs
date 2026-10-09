namespace TabForge.Docking;

/// <summary>The dock layout as pure data: building, cloning, searching, editing and validating a tree of <see cref="DockNodeState"/>. No UI types.</summary>
internal static class DockLayoutTree
{
    internal const double EdgeFraction = 0.28;

    internal static DockNodeState EditorNode(string hostId = "score-editor") =>
        new() { Kind = "editor", HostId = hostId };

    internal static DockNodeState Tabs(string hostId, params string[] panelIds) =>
        new()
        {
            Kind = "tabs",
            HostId = hostId,
            Panels = panelIds.ToList(),
            SelectedPanel = panelIds.FirstOrDefault()
        };

    internal static DockNodeState Split(string orientation, double ratio, DockNodeState first, DockNodeState second) =>
        new()
        {
            Kind = "split",
            Orientation = orientation,
            Ratio = ratio,
            First = first,
            Second = second
        };

    /// <summary>A captured layout carries the user's ratios, not the ones auto-fit set for the live window size.</summary>
    internal static void UseUserRatios(DockNodeState? node)
    {
        if (node is null) return;
        if (node.UserRatio is { } user && double.IsFinite(user)) node.Ratio = Math.Clamp(user, 0.02, 0.98);
        node.UserRatio = null;
        UseUserRatios(node.First);
        UseUserRatios(node.Second);
    }

    internal static DockNodeState? RemovePanel(DockNodeState? node, string id)
    {
        if (node is null) return null;
        if (node.Kind == "tabs")
        {
            node.Panels.RemoveAll(p => p == id);
            if (node.SelectedPanel == id) node.SelectedPanel = node.Panels.FirstOrDefault();
            return node.Panels.Count == 0 ? null : node;
        }
        if (node.Kind == "split")
        {
            node.First = RemovePanel(node.First, id);
            node.Second = RemovePanel(node.Second, id);
            if (node.First is null) return node.Second;
            if (node.Second is null) return node.First;
        }
        return node;
    }

    /// <summary>A saved layout may name panels that no longer exist (the removed Practice and Zoom panes): they are dropped and the rest is kept.</summary>
    internal static void DropPanels(DockWorkspaceState state, Func<string, bool> isRegistered)
    {
        foreach (var id in EnumerateAllPanels(state).Where(id => !isRegistered(id)).Distinct().ToList())
        {
            state.Root = RemovePanel(state.Root, id);
            foreach (var f in state.Floating) f.Root = RemovePanel(f.Root, id);
        }
        state.Floating.RemoveAll(f => f.Root is null);
    }

    internal static void AddToTabs(DockNodeState host, string panelId, int? index = null)
    {
        if (host.Kind != "tabs") return;
        host.Panels.RemoveAll(id => id == panelId);
        host.Panels.Insert(Math.Clamp(index ?? host.Panels.Count, 0, host.Panels.Count), panelId);
        host.SelectedPanel = panelId;
    }

    internal static DockWorkspaceState Clone(DockWorkspaceState state) => new()
    {
        Version = Math.Max(1, state.Version),
        Root = CloneNode(state.Root),
        Floating = (state.Floating ?? new()).Select(f => new DockFloatingState
        {
            Id = string.IsNullOrWhiteSpace(f.Id) ? Guid.NewGuid().ToString("N") : f.Id,
            Root = CloneNode(f.Root),
            Left = f.Left,
            Top = f.Top,
            Width = f.Width,
            Height = f.Height
        }).ToList(),
        ClosedPanels = (state.ClosedPanels ?? new()).ToList()
    };

    internal static DockNodeState? CloneNode(DockNodeState? node) => node is null ? null : new DockNodeState
    {
        Kind = node.Kind,
        HostId = node.HostId,
        Panels = (node.Panels ?? new()).ToList(),
        SelectedPanel = node.SelectedPanel,
        Orientation = node.Orientation,
        Ratio = double.IsFinite(node.Ratio) ? Math.Clamp(node.Ratio, 0.02, 0.98) : 0.5,
        UserRatio = node.UserRatio,
        First = CloneNode(node.First),
        Second = CloneNode(node.Second)
    };

    /// <summary>True when the layout has one editor in the main root, only registered panels, no panel twice and no closed panel also shown.</summary>
    internal static bool Validate(DockWorkspaceState state, bool hasEditorContent, Func<string, bool> isRegistered)
    {
        if (state.Root is null || !hasEditorContent) return false;
        var ids = new List<string>();
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        bool Walk(DockNodeState? node, bool isMain)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.HostId) || !hosts.Add(node.HostId)) return false;
            if (node.Kind == "editor")
            {
                ids.Add("$editor");
                return isMain;
            }
            if (node.Kind == "tabs")
            {
                foreach (var id in node.Panels)
                {
                    if (!isRegistered(id)) return false;
                    ids.Add(id);
                }
                return true;
            }
            if (node.Kind != "split" || node.First is null || node.Second is null) return false;
            if (node.Orientation is not ("Horizontal" or "Vertical")) return false;
            if (!double.IsFinite(node.Ratio) || node.Ratio is < 0.02 or > 0.98) return false;
            return Walk(node.First, isMain) && Walk(node.Second, isMain);
        }
        if (!Walk(state.Root, true)) return false;
        foreach (var floating in state.Floating)
            if (floating.Root is null || !Walk(floating.Root, false)) return false;
        // The Band view fills the editor's place in the Band layout, so a layout holding it may have no editor.
        var editors = ids.Count(i => i == "$editor");
        if (editors > 1 || (editors == 0 && !ids.Contains("band"))) return false;
        var panels = ids.Where(i => i != "$editor").ToList();
        if (panels.Count != panels.Distinct(StringComparer.Ordinal).Count()) return false;
        foreach (var id in state.ClosedPanels)
            if (!isRegistered(id) || panels.Contains(id, StringComparer.Ordinal)) return false;
        return true;
    }

    internal static IEnumerable<string> EnumerateAllPanels(DockWorkspaceState state) =>
        EnumeratePanels(state.Root).Concat(state.Floating.SelectMany(f => EnumeratePanels(f.Root)));

    internal static IEnumerable<string> EnumeratePanels(DockNodeState? node)
    {
        if (node is null) yield break;
        if (node.Kind == "tabs")
        {
            foreach (var id in node.Panels) yield return id;
        }
        if (node.Kind == "split")
        {
            foreach (var id in EnumeratePanels(node.First)) yield return id;
            foreach (var id in EnumeratePanels(node.Second)) yield return id;
        }
    }

    internal static bool ContainsPanel(DockNodeState node, string id) => EnumeratePanels(node).Contains(id, StringComparer.Ordinal);

    internal static DockNodeState? FindPanelHost(DockNodeState? node, string id)
    {
        if (node is null) return null;
        if (node.Kind == "tabs" && node.Panels.Contains(id, StringComparer.Ordinal)) return node;
        return FindPanelHost(node.First, id) ?? FindPanelHost(node.Second, id);
    }

    internal static DockNodeState? FindNode(DockNodeState? node, string hostId)
    {
        if (node is null) return null;
        if (node.HostId == hostId) return node;
        return FindNode(node.First, hostId) ?? FindNode(node.Second, hostId);
    }

    internal static DockNodeState? ReplaceNode(DockNodeState? root, string hostId, DockNodeState replacement)
    {
        if (root is null) return null;
        if (root.HostId == hostId) return replacement;
        if (root.First is not null) root.First = ReplaceNode(root.First, hostId, replacement);
        if (root.Second is not null) root.Second = ReplaceNode(root.Second, hostId, replacement);
        return root;
    }

    internal static void ReplaceNode(DockWorkspaceState state, string hostId, DockNodeState replacement)
        => state.Root = ReplaceNode(state.Root, hostId, replacement);

    /// <summary>The built-in layout: instrument above the editor, tool and section palettes at the right, timeline below.</summary>
    internal static DockWorkspaceState CreateDefaultState()
    {
        var toolHost = Tabs("default-tool-palette", "tools", "structure", "rhythm", "layout");
        toolHost.SelectedPanel = "tools";
        var sideHost = Tabs("default-sections-practice", "sections");
        sideHost.SelectedPanel = "sections";
        var right = Split("Vertical", 0.44, toolHost, sideHost);
        var instrumentAndScore = Split("Vertical", 0.26, Tabs("default-instrument", "instrument"), EditorNode());
        var upper = Split("Horizontal", 0.79, instrumentAndScore, right);
        var root = Split("Vertical", 0.74, upper, Tabs("default-timeline", "timeline"));
        root.Second!.SelectedPanel = "timeline";
        var state = new DockWorkspaceState { Root = root };
        return state;
    }

    /// <summary>Puts a panel back where it lives by default: into its shared tab host, or beside the editor, above it, or along the bottom.</summary>
    internal static void PlaceAtDefault(DockWorkspaceState state, string id, string defaultHost, string defaultAnchor)
    {
        // Palette and right-side utility panels restore into their original shared tab host.
        var host = FindPanelHost(state.Root, defaultAnchor) ??
                   state.Floating.Select(f => FindPanelHost(f.Root, defaultAnchor)).FirstOrDefault(h => h is not null);
        if (defaultHost == "tools" || defaultHost == "side")
        {
            if (host is not null) AddToTabs(host, id);
            else InsertAtRightOfEditor(state, id);
        }
        else if (defaultHost == "instrument") InsertAroundEditor(state, id, DockDropZone.Top);
        else if (defaultHost == "instrument-bottom") InsertAroundEditor(state, id, DockDropZone.Bottom);
        else if (defaultHost is "timeline" or "band") InsertAtRootEdge(state, id, DockDropZone.Bottom);
        else InsertAtRightOfEditor(state, id);
    }

    private static void InsertAtRightOfEditor(DockWorkspaceState state, string id)
    {
        var target = FindNode(state.Root, "score-editor");
        if (target is null) { InsertAtRootEdge(state, id, DockDropZone.Right); return; }   // a layout without the score (Band)
        ReplaceNode(state, target.HostId,
            Split("Horizontal", 0.70, CloneNode(target)!, Tabs("restore-" + Guid.NewGuid().ToString("N"), id)));
    }

    private static void InsertAroundEditor(DockWorkspaceState state, string id, DockDropZone zone)
    {
        var target = FindNode(state.Root, "score-editor");
        if (target is null) { InsertAtRootEdge(state, id, zone); return; }
        var panelNode = Tabs("restore-" + Guid.NewGuid().ToString("N"), id);
        var split = zone is DockDropZone.Top or DockDropZone.Bottom ? "Vertical" : "Horizontal";
        var before = zone is DockDropZone.Left or DockDropZone.Top;
        ReplaceNode(state, target.HostId, before
            ? Split(split, EdgeFraction, panelNode, CloneNode(target)!)
            : Split(split, 1 - EdgeFraction, CloneNode(target)!, panelNode));
    }

    private static void InsertAtRootEdge(DockWorkspaceState state, string id, DockDropZone zone)
    {
        var root = state.Root;
        if (root is null) return;
        var first = zone is DockDropZone.Left or DockDropZone.Top;
        var vertical = zone is DockDropZone.Top or DockDropZone.Bottom;
        var panel = Tabs("restore-" + Guid.NewGuid().ToString("N"), id);
        state.Root = first
            ? Split(vertical ? "Vertical" : "Horizontal", EdgeFraction, panel, CloneNode(root)!)
            : Split(vertical ? "Vertical" : "Horizontal", 1 - EdgeFraction, CloneNode(root)!, panel);
    }
}
