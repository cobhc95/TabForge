using TabForge.Docking;

namespace TabForge.Presenters;

/// <summary>One entry of a dock pane's right-click menu; <see cref="DockPaneMenuAction.Separator"/> draws a line.</summary>
internal enum DockPaneMenuAction { ResetPane, ClosePane, Separator, ResetAllPanes }

/// <summary>A pane menu entry: its label (empty for a separator) and what it does.</summary>
internal sealed record DockPaneMenuEntry(string Label, DockPaneMenuAction Action);

/// <summary>
/// The dock panes' rules that need no toolkit types.
/// Owns: which panes are solo (their tab strip is hidden when alone), which can move, float and close, the tab tool tip,
///     the floating-window title and the pane menu entries.
/// Does not own: the pane rows, titles and tab order (DockPaneTable), the layout tree (DockLayoutTree), drag, drop, tear-off and
///     window placement (DockWorkspace, DockDragController, FloatingWindowPlacement).
/// Tests: no dedicated test class.
/// </summary>
internal static class DockPanePresenter
{
    // Panes that only ever sit alone in their host: one tab is pure chrome, so the strip hides and the content carries the menu.
    private static readonly string[] SoloPaneIds = { "instrument", "timeline", "learn", "band" };

    public static readonly IReadOnlyList<DockPaneMenuEntry> MenuEntries = new DockPaneMenuEntry[]
    {
        new("Reset this panel to default position", DockPaneMenuAction.ResetPane),
        new("Close panel", DockPaneMenuAction.ClosePane),
        new("", DockPaneMenuAction.Separator),
        new("Reset all panels to default positions", DockPaneMenuAction.ResetAllPanes),
    };

    public static bool IsSolo(string id) => SoloPaneIds.Contains(id);

    /// <summary>False for the fixed Band pane: it has no drag, no tear-off and no menu, so it cannot move, float or close.</summary>
    public static bool CanMove(string id) => !DockLayoutTree.IsFixedPanel(id);

    /// <summary>True when the host holds one solo pane, so its tab strip is hidden.</summary>
    public static bool HidesTabStrip(IReadOnlyList<string> ids) => ids.Count == 1 && IsSolo(ids[0]);

    /// <summary>The pane whose menu goes on the content area (a hidden strip that can move), or null.</summary>
    public static string? ContentMenuPane(IReadOnlyList<string> ids) => HidesTabStrip(ids) && CanMove(ids[0]) ? ids[0] : null;

    public static string TabToolTip(string title) => $"{title} · drag to move · right-click for panel options";

    /// <summary>The floating window's title: the first three panes' titles; an unknown id shows itself.</summary>
    public static string FloatingTitle(IEnumerable<string> ids, Func<string, string?> titleOf) =>
        string.Join(" · ", ids.Select(id => titleOf(id) ?? id).Take(3));
}
