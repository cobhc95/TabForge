using System.Collections.Generic;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: the section lock toggle of the timeline's section menu and bar menu ("Lock section position"): it flips LockPosition of the section
// the right-click was on (ContextMenuHost.ClickedSection) on the shown song, as one undo step.
// Does not own: the menu rows (TimelineMenus in Core), the row-to-id mapping (ContextMenuRows), the section lane drawing (TimelineView),
//   the commands table (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.MainWindowSectionLockTests.
public static class MainWindowSectionLock
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { ContextMenuRows.SectionLockId };

    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="rows">The timeline's menu host; its ClickedSection is the section the lock row acts on.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, ContextMenuHost rows) =>
        commands.Add(ContextMenuRows.SectionLockId, () => Toggle(songs.Active, rows.ClickedSection));

    /// <summary>Flips the position lock of the section at <paramref name="markerIndex"/>; nothing changes when no section is there.</summary>
    public static void Toggle(IEditableDocument document, int markerIndex) =>
        DocumentEdits.Run(document, project =>
        {
            if (markerIndex < 0 || markerIndex >= project.Markers.Count) return false;
            var marker = project.Markers[markerIndex];
            marker.LockPosition = !marker.LockPosition;
            return true;
        });
}
