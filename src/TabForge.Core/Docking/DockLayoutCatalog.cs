using TabForge.Services;

namespace TabForge.Docking;

// Owns: the built-in layouts (Default, and the Band view's own arrangement), finding a saved or built-in layout by name, the name a typed
//   layout is saved under, the Layouts menu rows as data, and the score-back and side-panel comparison rules.
// Does not own: menus, the window and the dock (DockLayoutController builds the menus from these rows), saving settings.
// Tests: no dedicated test class.
/// <summary>Toolkit-free rules of the dock's built-in and saved layouts.</summary>
internal static class DockLayoutCatalog
{
    public static readonly string[] BuiltInLayoutNames = { DefaultName, BandName };
    public const string DefaultName = "Default";
    public const string BandName = "Band";
    public const int MaxSavedLayouts = 24;
    public const int MaxLayoutNameLength = 40;

    /// <summary>One Layouts menu entry: no layout has a hotkey now.</summary>
    public readonly record struct LayoutMenuRow(string Name, string? HotkeyId);

    /// <summary>A layout found by name: the name it is shown under, its panels, and the saved entry when it is one.</summary>
    public sealed record ResolvedLayout(string Name, DockWorkspaceState State, SavedLayout? Saved);

    private static readonly LayoutMenuRow[] BuiltInMenuRows =
    {
        new(DefaultName, null)
    };

    public static DockWorkspaceState BuiltInLayout(string name)
    {
        // Default is the standard panel arrangement; Band is the Band view's own arrangement (entered from View > Band view, not listed in the Layouts menu).
        if (!string.Equals(name, BandName, StringComparison.OrdinalIgnoreCase)) return InstrumentTopLayout();
        var timeline = DockLayoutTree.Tabs("default-timeline", "timeline");
        // Band view and arrangement only: no score, no fretboard pane, no side panel.
        var state = new DockWorkspaceState { Root = DockLayoutTree.Split("Vertical", 0.80, DockLayoutTree.Tabs("default-band", "band"), timeline) };
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
