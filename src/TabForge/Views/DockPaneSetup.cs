using System.Windows;
using TabForge.Docking;

namespace TabForge.Views;

// Owns: registering every row of DockPaneTable with the dock workspace.
// Does not own: the table, the pane content, layout restore.
// Tests: TestDockPaneTable.
internal static class DockPaneSetup
{
    /// <summary>Registers each table row in registration order; <paramref name="content"/> returns the element of a pane id.</summary>
    public static void RegisterAll(DockWorkspace dock, Func<string, FrameworkElement> content)
    {
        foreach (var row in DockPaneTable.InRegistrationOrder)
            dock.RegisterPanel(row.Id, row.PaneTitle, content(row.Id), row.MinWidth, row.MinHeight, row.DefaultHost, row.DefaultAnchor, row.StartsClosed);
    }
}
