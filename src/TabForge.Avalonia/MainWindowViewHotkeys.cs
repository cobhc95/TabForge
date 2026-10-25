using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the View hotkeys for the three built-in workspace layouts (View.LayoutCompose, View.LayoutPractice, View.LayoutMix), which apply the
//   layout of that name to the dock, View.ShowTrackGroups, which runs the same toggle as the track list's "Show tracks in groups" row, and
//   Mixer.CollapseAllGroups / Mixer.ExpandAllGroups, which collapse or expand every group of the shown song.
// Does not own: the Layouts menu and its checkmarks (DockViewMenus), the layout catalogue (DockLayoutCatalog in TabForge.Core), the groups
//   toggle (MainWindowTrackListRows), the key table (MainWindowKeyboard).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (reads Commands by reflection and adds each id to a command table).
public static class MainWindowViewHotkeys
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "View.LayoutCompose", "View.LayoutPractice", "View.LayoutMix", "View.ShowTrackGroups",
        "Mixer.CollapseAllGroups", "Mixer.ExpandAllGroups",
    };

    /// <summary>The layout hotkey ids and the built-in layout each one applies.</summary>
    private static readonly IReadOnlyDictionary<string, string> LayoutOfCommand = new Dictionary<string, string>(System.StringComparer.Ordinal)
    {
        ["View.LayoutCompose"] = "Compose",
        ["View.LayoutPractice"] = "Practice",
        ["View.LayoutMix"] = "Mix",
    };

    /// <param name="commands">The window's command table; each id is added once.</param>
    /// <param name="songs">The tabs; the groups hotkey acts on the shown song.</param>
    /// <param name="dock">The dock the layout hotkeys apply their layout to.</param>
    /// <param name="settings">The saved layouts a layout name resolves against; null resolves the built-in layouts only.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, DockWorkspaceController dock, MainWindowSettings? settings)
    {
        foreach (var (commandId, layoutName) in LayoutOfCommand)
            commands.Add(commandId, () => ApplyLayout(dock, settings, layoutName));
        commands.Add("View.ShowTrackGroups", () => MainWindowTrackListRows.ToggleGroupsShown(songs.Active));
        commands.Add("Mixer.CollapseAllGroups", () => SetAllGroupsCollapsed(songs.Active, true));
        commands.Add("Mixer.ExpandAllGroups", () => SetAllGroupsCollapsed(songs.Active, false));
    }

    /// <summary>Collapses (or expands) every group of the song: a group is collapsed by listing its name in the mixer's collapsed groups.
    /// The collapse state is view state: it is saved and redraws, but it is never an undo step (an undo keeps the live value).</summary>
    private static void SetAllGroupsCollapsed(IEditableDocument document, bool collapsed)
    {
        var project = document.Project;
        var collapsedGroups = project.Mixer.CollapsedGroups;
        var changed = false;
        foreach (var group in TrackOrdering.Layout(project).Select(layout => layout.Group))
        {
            if (collapsed && !collapsedGroups.Contains(group)) { collapsedGroups.Add(group); changed = true; }
            else if (!collapsed && collapsedGroups.Remove(group)) changed = true;
        }
        if (!changed) return;
        project.IsDirty = true;
        project.MarkTimelineChanged();
    }

    /// <summary>Applies the named layout to the dock; a name that resolves to no layout leaves the dock as it is.</summary>
    private static void ApplyLayout(DockWorkspaceController dock, MainWindowSettings? settings, string layoutName)
    {
        var saved = settings?.Settings.SavedLayouts ?? new List<SavedLayout>();
        if (DockLayoutCatalog.Resolve(saved, layoutName) is { } layout) dock.ApplyLayout(layout.State);
    }
}
