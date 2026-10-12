using System.Windows;
using TabForge.Services;

namespace TabForge.Views;

// Owns: registering the main-menu rows that carry a command id and a click handler as commands, so a key assigned in
//   Preferences > Shortcuts runs the same handler as the menu item.
// Does not own: the rows (MainWindow.Menus.cs), the registry (Services/CommandRegistry.cs), the feature-module rows
//   (FeatureRegistry registers their ids, and their handlers route back through the registry, so they are skipped here).
// Tests: TestAddTrackKeys.
public static class MenuRowCommands
{
    /// <summary>Adds each row's id that the table does not hold yet, running the row's handler with <paramref name="sender"/>; returns the table.</summary>
    public static CommandRegistry AddTo(CommandRegistry table, object sender, IEnumerable<MenuGroup> groups, IEnumerable<string> featureIds)
    {
        var skip = featureIds.ToHashSet();
        foreach (var row in groups.SelectMany(g => g.Rows))
        {
            if (row.Id is null || row.Click is null || skip.Contains(row.Id) || table.Ids.Contains(row.Id)) continue;
            var click = row.Click;
            table.Add(row.Id, () => click(sender, new RoutedEventArgs()));
        }
        return table;
    }
}
