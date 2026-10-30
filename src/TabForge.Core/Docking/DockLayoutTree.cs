namespace TabForge.Docking;

// Owns: the dock layout as pure data (build, clone, search, edit, validate).
// Does not own: any UI types or the workspace persistence.
// Tests: docking layout tests.

/// <summary>The dock layout as pure data: building, cloning, searching, editing and validating a tree of <see cref="DockNodeState"/>. No UI types.</summary>
internal static class DockLayoutTree
{
    internal const double EdgeFraction = 0.28;

    /// <summary>True for a pane that stays where its layout puts it: no tab to drag, no panel menu, and nothing can be dropped onto it.</summary>
    internal static bool IsFixedPanel(string id) => id == "band";

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
        // A fixed pane (the Band view) lives only in its own layout: one that holds it beside the score, in a shared tab host or in a floating
        // window has it closed, and the Band view command opens it in its standard place.
        foreach (var id in EnumerateAllPanels(state).Where(IsFixedPanel).Distinct().ToList())
        {
            if (FindPanelHost(state.Root, id) is { Panels.Count: 1 } && FindNode(state.Root, "score-editor") is null) continue;
            state.Root = RemovePanel(state.Root, id);
            foreach (var f in state.Floating) f.Root = RemovePanel(f.Root, id);
            if (!state.ClosedPanels.Contains(id)) state.ClosedPanels.Add(id);
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
        ClosedPanels = (state.ClosedPanels ?? new()).ToList(),
        InstrumentVerticalShare = ClampShareOrNull(state.InstrumentVerticalShare),
        InstrumentHorizontalShare = ClampShareOrNull(state.InstrumentHorizontalShare)
    };

    private static double? ClampShareOrNull(double? share) =>
        share is { } s && double.IsFinite(s) ? Math.Clamp(s, 0.02, 0.98) : null;

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
        // The Band view (and the Keyboard mode pane in its full-size layouts) fills the editor's place, so a layout holding it may have no editor.
        var editors = ids.Count(i => i == "$editor");
        if (editors > 1 || (editors == 0 && !ids.Contains("band") && !ids.Contains("learn"))) return false;
        var panels = ids.Where(i => i != "$editor").ToList();
        if (panels.Count != panels.Distinct(StringComparer.Ordinal).Count()) return false;
        foreach (var id in state.ClosedPanels)
            if (!isRegistered(id) || panels.Contains(id, StringComparer.Ordinal)) return false;
        return true;
    }

    /// <summary>The panes a saved layout must show to be used at start: the fretboard, the timeline and the tools palette.</summary>
    internal static readonly string[] StartupRequiredPanels = { "instrument", "timeline", "tools" };

    /// <summary>True when a saved layout can be used at start: it validates, and every required pane is shown in its main tree (not closed, not floating).</summary>
    internal static bool IsUsableAtStart(DockWorkspaceState? state)
    {
        if (state is null || !Validate(state, hasEditorContent: true, id => DockPaneTable.Ids.Contains(id))) return false;
        var shown = EnumerateAllPanels(state).ToHashSet(StringComparer.Ordinal);
        return StartupRequiredPanels.All(shown.Contains);
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
        var sideHost = Tabs("default-sections-practice", "sections", "lyrics");
        sideHost.SelectedPanel = "sections";
        var right = Split("Vertical", 0.44, toolHost, WithViewTempo(sideHost));
        var instrumentAndScore = Split("Vertical", 0.26, Tabs("default-instrument", "instrument"), EditorNode());
        var upper = Split("Horizontal", 0.79, instrumentAndScore, right);
        var root = Split("Vertical", 0.74, upper, Tabs("default-timeline", "timeline"));
        root.Second!.SelectedPanel = "timeline";
        var state = new DockWorkspaceState { Root = root };
        return state;
    }

    /// <summary>A column with <paramref name="above"/> over the View and tempo pane (or <paramref name="id"/>), which takes the part below <paramref name="ratio"/>.</summary>
    internal static DockNodeState WithViewTempo(DockNodeState above, string id = DockPaneTable.ViewTempoId, double ratio = 0.84)
        => Split("Vertical", ratio, above, Tabs("default-below-" + id, id));

    /// <summary>A copy of <paramref name="layout"/> with the fretboard pane on the chosen side of the score when the layout stacks it on the
    /// other one. A fretboard beside the score, in a floating window, closed, or in a layout without the score stays as it is. The pane keeps its
    /// pixel height: the split ratio is set from the remembered vertical share, which the move itself remembers first.</summary>
    internal static DockWorkspaceState WithInstrumentSide(DockWorkspaceState layout, bool bottom)
    {
        static bool Has(DockNodeState? n, Func<DockNodeState, bool> f) => n is not null && (f(n) || Has(n.First, f) || Has(n.Second, f));
        static bool Instrument(DockNodeState n) => n.Kind == "tabs" && n.Panels.Contains("instrument");
        static bool Editor(DockNodeState n) => n.Kind == "editor";
        var state = Clone(layout);
        RememberInstrumentShare(state);
        // Walk down to the split that separates the two; only a vertical one says above or below.
        for (var node = state.Root; node is { Kind: "split" } && Has(node, Instrument) && Has(node, Editor);)
        {
            var instrumentFirst = Has(node.First, Instrument);
            if (instrumentFirst == Has(node.First, Editor)) { node = instrumentFirst ? node.First : node.Second; continue; }
            if (node.Orientation != "Vertical" || instrumentFirst != bottom) break;
            state.Root = RemovePanel(state.Root, "instrument");
            PlaceAtDefault(state, "instrument", bottom ? "instrument-bottom" : "instrument", "score-editor");
            ApplyInstrumentShare(state, vertical: true);
            break;
        }
        return state;
    }

    /// <summary>The split that separates the instrument pane from the score editor and the instrument's share of it, searching the main root then the floating roots.</summary>
    internal static (DockNodeState Node, bool InstrumentFirst, double Share, bool Vertical)? FindInstrumentShare(DockWorkspaceState state)
    {
        static bool Has(DockNodeState? n, Func<DockNodeState, bool> f) => n is not null && (f(n) || Has(n.First, f) || Has(n.Second, f));
        static bool Instrument(DockNodeState n) => n.Kind == "tabs" && n.Panels.Contains(InstrumentDockSizeMemory.InstrumentId);
        static bool Editor(DockNodeState n) => n.Kind == "editor";
        foreach (var root in new[] { state.Root }.Concat(state.Floating.Select(f => f.Root)))
        {
            for (var node = root; node is { Kind: "split" } && Has(node, Instrument) && Has(node, Editor);)
            {
                var firstHasInstrument = Has(node.First, Instrument);
                if (firstHasInstrument == Has(node.First, Editor)) { node = firstHasInstrument ? node.First : node.Second; continue; }
                var share = InstrumentDockSizeMemory.ShareOf(node.Ratio, firstHasInstrument);
                return (node, firstHasInstrument, share, InstrumentDockSizeMemory.IsVertical(node.Orientation));
            }
        }
        return null;
    }

    /// <summary>Remembers the instrument pane's current share into its orientation slot (a dragged size is kept for the next move).</summary>
    internal static void RememberInstrumentShare(DockWorkspaceState state)
    {
        if (FindInstrumentShare(state) is not { } found) return;
        if (found.Vertical) state.InstrumentVerticalShare = InstrumentDockSizeMemory.RememberShare(found.Share, state.InstrumentVerticalShare);
        else state.InstrumentHorizontalShare = InstrumentDockSizeMemory.RememberShare(found.Share, state.InstrumentHorizontalShare);
    }

    /// <summary>Sets the split that separates the instrument from the editor to the remembered share for <paramref name="vertical"/>.</summary>
    internal static void ApplyInstrumentShare(DockWorkspaceState state, bool vertical)
    {
        if (FindInstrumentShare(state) is not { } found) return;
        var remembered = vertical ? state.InstrumentVerticalShare : state.InstrumentHorizontalShare;
        var share = InstrumentDockSizeMemory.ShareToApply(null, remembered);
        found.Node.Ratio = InstrumentDockSizeMemory.RatioForShare(share, found.InstrumentFirst);
        if (vertical) state.InstrumentVerticalShare = InstrumentDockSizeMemory.RememberShare(share, state.InstrumentVerticalShare);
        else state.InstrumentHorizontalShare = InstrumentDockSizeMemory.RememberShare(share, state.InstrumentHorizontalShare);
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
        else if (defaultHost == "tab")
        {
            // A tab after its anchor that leaves the selected tab alone; a layout without the anchor gets it beside the editor.
            var selected = host?.SelectedPanel;
            if (host is null) InsertAtRightOfEditor(state, id);
            else { AddToTabs(host, id, host.Panels.IndexOf(defaultAnchor) + 1); host.SelectedPanel = selected; }
        }
        else if (defaultHost == "below")
        {
            // Below its anchor's pane in the main window; a layout without that pane gets it beside the editor.
            var above = FindPanelHost(state.Root, defaultAnchor);
            if (above is not null) ReplaceNode(state, above.HostId, WithViewTempo(CloneNode(above)!, id));
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
        var share = ShareForNewPanel(state, id, split);
        ReplaceNode(state, target.HostId, before
            ? Split(split, share, panelNode, CloneNode(target)!)
            : Split(split, 1 - share, CloneNode(target)!, panelNode));
    }

    /// <summary>The share a newly placed panel takes: the instrument pane reuses its remembered share for that orientation (the default only the first time), every other pane the default.</summary>
    private static double ShareForNewPanel(DockWorkspaceState state, string id, string orientation)
    {
        if (id != InstrumentDockSizeMemory.InstrumentId) return EdgeFraction;
        var vertical = InstrumentDockSizeMemory.IsVertical(orientation);
        var remembered = vertical ? state.InstrumentVerticalShare : state.InstrumentHorizontalShare;
        var share = InstrumentDockSizeMemory.ShareToApply(null, remembered);
        if (vertical) state.InstrumentVerticalShare = InstrumentDockSizeMemory.RememberShare(share, state.InstrumentVerticalShare);
        else state.InstrumentHorizontalShare = InstrumentDockSizeMemory.RememberShare(share, state.InstrumentHorizontalShare);
        return share;
    }

    private static void InsertAtRootEdge(DockWorkspaceState state, string id, DockDropZone zone)
    {
        var root = state.Root;
        if (root is null) return;
        var first = zone is DockDropZone.Left or DockDropZone.Top;
        var vertical = zone is DockDropZone.Top or DockDropZone.Bottom;
        var orientation = vertical ? "Vertical" : "Horizontal";
        var share = id == InstrumentDockSizeMemory.InstrumentId ? ShareForNewPanel(state, id, orientation) : EdgeFraction;
        var panel = Tabs("restore-" + Guid.NewGuid().ToString("N"), id);
        state.Root = first
            ? Split(orientation, share, panel, CloneNode(root)!)
            : Split(orientation, 1 - share, CloneNode(root)!, panel);
    }
}
