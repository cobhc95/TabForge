using System.Windows;
using System.Windows.Controls;

namespace TabForge.Views;

/// <summary>One plain menu entry: a header, an optional catalogue command id and tag, and the handler a click runs. A separator has no header.</summary>
public sealed record MenuRow(string? Header, RoutedEventHandler? Click = null, string? Id = null, string? Tag = null)
{
    public static readonly MenuRow Sep = new((string?)null);
    public bool IsSeparator => Header is null;
    public static MenuRow Item(string header, RoutedEventHandler click, string? id = null, string? tag = null) => new(header, click, id, tag);
}

/// <summary>A run of rows inserted into the top-level menu <see cref="Menu"/> right after the XAML item named <see cref="After"/> (at the start when null).</summary>
public sealed record MenuGroup(string Menu, string? After, params MenuRow[] Rows);

// Owns: the data shape of the plain main-menu commands (header with access key, command id, optional tag, click handler, separators)
//   and the one-time build that inserts them into the skeleton menus declared in MainWindow.xaml.
// Does not own: the rows themselves (MainWindow.Menus.cs, where the handlers are), the menu skeleton and the complex items
//   (checkable, named, with dynamic content) in MainWindow.xaml, or gesture text (MenuHotkey).
// Tests: TestMainMenuTable, TestMainMenuTreeGolden.
public static class MenuTable
{
    /// <summary>Inserts every group's rows into <paramref name="menu"/>; a missing menu or anchor throws, so a typo fails at startup and in the tests.</summary>
    public static void Build(Menu menu, IEnumerable<MenuGroup> groups)
    {
        foreach (var group in groups)
        {
            var top = menu.Items.OfType<MenuItem>().First(m => m.Header as string == group.Menu);
            var at = group.After is null ? 0 : top.Items.IndexOf(top.Items.OfType<MenuItem>().First(m => m.Name == group.After)) + 1;
            foreach (var row in group.Rows) top.Items.Insert(at++, ToElement(row));
        }
    }

    /// <summary>Adds each feature-module row after the row holding its anchor command id, in the module's menu; a missing menu or anchor throws.</summary>
    public static MenuGroup[] AddFeatureRows(MenuGroup[] groups, Func<string, bool> run)
    {
        foreach (var f in Services.Features.FeatureRegistry.MenuRows)
        {
            var g = Array.FindIndex(groups, x => x.Menu == f.Menu && Array.Exists(x.Rows, r => r.Id == f.AfterId));
            if (g < 0) throw new InvalidOperationException($"Feature menu row '{f.Id}': anchor '{f.AfterId}' not found in menu '{f.Menu}'.");
            var rows = groups[g].Rows.ToList();
            rows.Insert(rows.FindIndex(r => r.Id == f.AfterId) + 1, MenuRow.Item(f.Header, (_, _) => run(f.Id), f.Id));
            groups[g] = groups[g] with { Rows = rows.ToArray() };
        }
        return groups;
    }

    private static Control ToElement(MenuRow row)
    {
        if (row.IsSeparator) return new Separator();
        var item = new MenuItem { Header = row.Header };
        if (row.Click is not null) item.Click += row.Click;
        if (row.Id is not null) MenuHotkey.SetId(item, row.Id);
        if (row.Tag is not null) item.Tag = row.Tag;
        return item;
    }
}
