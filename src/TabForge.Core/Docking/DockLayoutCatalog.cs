using TabForge.Services;

namespace TabForge.Docking;

// Owns: the built-in layouts (Compose, Practice, Mix, Band), finding a saved or built-in layout by name, the name a typed
//   layout is saved under, the Layouts menu rows as data, and the score-back and side-panel comparison rules.
// Does not own: menus, the window and the dock (DockLayoutController builds the WPF menus from these rows), saving settings.
// Tests: no dedicated test class.
/// <summary>Toolkit-free rules of the dock's built-in and saved layouts.</summary>
internal static class DockLayoutCatalog
{
    public static readonly string[] BuiltInLayoutNames = { "Compose", "Practice", "Mix", "Band" };
    public const int MaxSavedLayouts = 24;
    public const int MaxLayoutNameLength = 40;

    /// <summary>One Layouts menu entry: a built-in layout has its hotkey id, a saved layout has none.</summary>
    public readonly record struct LayoutMenuRow(string Name, string? HotkeyId);

    /// <summary>A layout found by name: the name it is shown under, its panels, and the saved entry when it is one.</summary>
    public sealed record ResolvedLayout(string Name, DockWorkspaceState State, SavedLayout? Saved);

    private static readonly LayoutMenuRow[] BuiltInMenuRows =
    {
        new("Compose", "View.LayoutCompose"), new("Practice", "View.LayoutPractice"), new("Mix", "View.LayoutMix"), new("Band", "View.BandView")
    };

    public static DockWorkspaceState BuiltInLayout(string name)
    {
        static DockNodeState Tools() => DockLayoutTree.Tabs("default-tool-palette", "tools", "structure", "rhythm", "layout");
        static DockNodeState Side(string selected, params string[] ids)
        {
            var host = DockLayoutTree.Tabs("default-sections-practice", ids);
            host.SelectedPanel = selected;
            return host;
        }
        var score = DockLayoutTree.EditorNode();
        var instrument = DockLayoutTree.Tabs("default-instrument", "instrument");
        var timeline = DockLayoutTree.Tabs("default-timeline", "timeline");
        var root = name switch
        {
            // Score + tab editor large; fretboard/keyboard and tools beside it; arrangement small.
            "Compose" => DockLayoutTree.Split("Vertical", 0.86,
                DockLayoutTree.Split("Horizontal", 0.78, DockLayoutTree.Split("Vertical", 0.68, score, instrument), DockLayoutTree.WithViewTempo(Tools(), ratio: 0.84)),
                timeline),
            // Score/tab + fretboard large; sections panel visible; arrangement and mixer window closed.
            "Practice" => DockLayoutTree.Split("Horizontal", 0.78,
                DockLayoutTree.Split("Vertical", 0.60, score, instrument),
                DockLayoutTree.WithViewTempo(Side("sections", "sections", "lyrics"), ratio: 0.8)),
            // Band view and arrangement only: no score, no fretboard pane, no side panel.
            "Band" => DockLayoutTree.Split("Vertical", 0.80, DockLayoutTree.Tabs("default-band", "band"), timeline),
            // Arrangement + mixer large; score small.
            _ => DockLayoutTree.Split("Vertical", 0.30, score,
                DockLayoutTree.Split("Horizontal", 0.74, timeline, DockLayoutTree.WithViewTempo(Side("sections", "sections", "lyrics"), ratio: 0.8)))
        };
        var state = new DockWorkspaceState { Root = root };
        var present = DockLayoutTree.EnumerateAllPanels(state).ToHashSet(StringComparer.Ordinal);
        state.ClosedPanels = DockPaneTable.Ids.Where(id => !present.Contains(id)).ToList();
        return state;
    }

    /// <summary>The default workspace with the fretboard above the score (the Band view's return when no panels were kept before it).</summary>
    public static DockWorkspaceState InstrumentTopLayout()
    {
        var state = DockLayoutTree.CreateDefaultState();
        var present = DockLayoutTree.EnumerateAllPanels(state).ToHashSet(StringComparer.Ordinal);
        state.ClosedPanels = DockPaneTable.Ids.Where(id => !present.Contains(id)).ToList();
        return state;
    }

    public static bool IsBuiltIn(string name) => BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in name spelled as the app spells it, or null when <paramref name="name"/> is not built in.</summary>
    public static string? BuiltInName(string name) =>
        BuiltInLayoutNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    public static SavedLayout? FindSaved(IEnumerable<SavedLayout> saved, string name) =>
        saved.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A saved layout with panels wins; otherwise the built-in of that name; null when neither exists.</summary>
    public static ResolvedLayout? Resolve(IEnumerable<SavedLayout> saved, string name)
    {
        var found = FindSaved(saved, name);
        if (found?.State?.Root is not null) return new ResolvedLayout(found.Name, found.State, found);
        return BuiltInName(name) is { } builtIn ? new ResolvedLayout(builtIn, BuiltInLayout(builtIn), null) : null;
    }

    /// <summary>The name a typed (already trimmed, non-empty) layout name is saved under: cut to the length limit, built-in names in their own spelling.</summary>
    public static string SavedLayoutName(string typed)
    {
        var name = typed.Length > MaxLayoutNameLength ? typed[..MaxLayoutNameLength] : typed;
        return BuiltInName(name) ?? name;
    }

    /// <summary>The Layouts menu entries in order: the built-ins, then the saved layouts that are not built-in names.</summary>
    public static IReadOnlyList<LayoutMenuRow> LayoutMenuRows(IEnumerable<SavedLayout> saved) =>
        BuiltInMenuRows.Concat(saved.Where(l => !IsBuiltIn(l.Name)).Select(l => new LayoutMenuRow(l.Name, null))).ToList();

    /// <summary>True when a built layout has neither the score nor the Band view (an empty workspace before the first restore is not one): closing the Band view in the Band layout leaves that.</summary>
    public static bool NeedsScoreBack(DockWorkspaceState state)
    {
        var panels = DockLayoutTree.EnumerateAllPanels(state).ToList();
        return state.Root is not null && WorkspaceNormalizer.FindEditor(state.Root) is null && !panels.Contains("band") && !panels.Contains(WorkspaceNormalizer.KeyboardModePaneId);
    }

    /// <summary>True when every panel outside the side panel is the same in both layouts (so a saved layout can be restored whole).</summary>
    public static bool OnlySidePanelsDiffer(DockWorkspaceState saved, DockWorkspaceState now)
    {
        static HashSet<string> Others(DockWorkspaceState state) =>
            DockLayoutTree.EnumerateAllPanels(state).Where(p => !DockPaneTable.SideIds.Contains(p)).ToHashSet();
        return Others(saved).SetEquals(Others(now));
    }
}
