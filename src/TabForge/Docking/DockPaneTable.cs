namespace TabForge.Docking;

/// <summary>One dock pane: the id saved layouts use, its titles and its default placement.</summary>
/// <param name="Id">Stable id written into saved layouts; never rename one.</param>
/// <param name="MenuTitle">The View > Panels entry.</param>
/// <param name="PaneTitle">The tab and floating-window title.</param>
/// <param name="Side">True for the panes of the side panel (hidden together by the side-panel toggle).</param>
/// <param name="RegisterOrder">Position in the registration sequence (the dock places panes missing from a layout in that order).</param>
internal sealed record DockPaneRow(string Id, string MenuTitle, string PaneTitle, double MinWidth, double MinHeight,
    string DefaultHost, string DefaultAnchor, int RegisterOrder, bool Side = false, bool StartsClosed = false);

// Owns: the list of dock panes (id, titles, default placement, side-panel membership) that the Panels menu, the pane registration,
//   the built-in layouts' closed lists and the settings validator read. The rows are in Panels-menu order.
// Does not own: the pane content (the window hands each pane's element to DockPaneSetup), layout placement (DockLayoutTree).
// Tests: TestDockPaneTable.
internal static class DockPaneTable
{
    public static readonly IReadOnlyList<DockPaneRow> Rows = new DockPaneRow[]
    {
        new("tools", "Tools", "Tools", 210, 150, "tools", "structure", 3, Side: true),
        new("structure", "Structure", "Structure", 210, 150, "tools", "tools", 4, Side: true),
        new("rhythm", "Rhythm", "Rhythm", 210, 140, "tools", "tools", 5, Side: true),
        new("layout", "Layout", "Layout", 210, 140, "tools", "tools", 6, Side: true),
        new("sections", "Sections", "Sections", 190, 180, "side", "score-editor", 7, Side: true),
        new("instrument", "Fretboard", "Fretboard", 360, 150, "instrument", "score-editor", 0),
        new("timeline", "Arrangement", "Arrangement", 440, 112, "timeline", "score-editor", 1),
        new("band", "Band view", "Band", 520, 240, "band", "score-editor", 2, StartsClosed: true),
        new("learn", "Keyboard mode (experimental)", "Keyboard mode (experimental)", 440, 200, "band", "score-editor", 8, StartsClosed: true),
    };

    /// <summary>Ids that older settings files may still carry and the validator accepts without a pane.</summary>
    private static readonly string[] RetiredIds = { "practice", "playback" };

    public static IEnumerable<string> Ids => Rows.Select(r => r.Id);

    public static IEnumerable<string> SideIds => Rows.Where(r => r.Side).Select(r => r.Id);

    public static IEnumerable<DockPaneRow> InRegistrationOrder => Rows.OrderBy(r => r.RegisterOrder);

    /// <summary>True for an id a saved layout may hold: a current pane or a retired one.</summary>
    public static bool IsSavedId(string id) => Rows.Any(r => r.Id == id) || RetiredIds.Contains(id);
}
