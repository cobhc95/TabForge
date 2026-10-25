using System;
using System.Linq;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the right-click menu of a Band pane's header: one row per row count (1 to BandRowRules.MaxRows), the pane's current count checked.
// Does not own: what a count does (BandView stores it with the song) or the limits (BandRowRules).
// Tests: TabForge.Avalonia.Tests.BandRowsTests.
internal static class BandRowsMenu
{
    /// <summary>The menu; <paramref name="current"/> is read each time it opens, <paramref name="pick"/> gets the chosen count.</summary>
    public static ContextMenu Build(Func<int> current, Action<int> pick)
    {
        var menu = MenuStability.CreateContextMenu();
        for (var rows = BandRowRules.MinRows; rows <= BandRowRules.MaxRows; rows++)
        {
            var count = rows;
            var item = new MenuItem { Header = $"Rows: {count}", ToggleType = MenuItemToggleType.Radio, Tag = count };
            item.Click += (_, _) => pick(count);
            menu.Items.Add(item);
        }
        menu.Opened += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.IsChecked = (int)item.Tag! == current();
        };
        return menu;
    }
}
