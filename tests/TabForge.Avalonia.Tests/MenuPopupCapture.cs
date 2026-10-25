using System.Collections.Generic;
using Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TabForge.Avalonia.Tests;

// Owns: opening one top-level menu of the MainWindow's main menu (MainWindowMenu) in a test and returning the open popup's content, so a
//   parity render can capture the popup itself the way the WPF menu shot does.
// Does not own: which menu each shot opens (ParityShotMap), the rendering and the saving of the content (ParityShotsTests).
// Tests: ParityShotsTests.RendersTwinInTheShotThemeAndSize (every menu shot), which fails when a popup does not open.
internal static class MenuPopupCapture
{
    /// <summary>The name MainWindowMenu gives the menu bar.</summary>
    private const string MainMenuName = "MainMenu";

    /// <summary>Opens the top-level menu with this header, such as "_File", and returns the content of its popup.</summary>
    /// <remarks>The window must be shown. The popup is laid out by the dispatcher jobs run here, so its content has bounds afterwards.</remarks>
    public static Control OpenTopLevel(Window window, string header)
    {
        var top = TopLevelItem(window, header);
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        return PopupContentOf(top, header);
    }

    /// <summary>Opens the top-level menu, then its submenu with this header, and returns the content of the submenu's popup.</summary>
    public static Control OpenSubmenu(Window window, string topHeader, string submenuHeader)
    {
        var top = TopLevelItem(window, topHeader);
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var submenu = top.Items.OfType<MenuItem>().Single(item => Equals(item.Header, submenuHeader));
        submenu.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        return PopupContentOf(submenu, submenuHeader);
    }

    /// <summary>Opens the context menu a right-click on <paramref name="target"/> at <paramref name="at"/> opens, and returns the content of its popup.</summary>
    public static Control OpenContext(Control target, Point at, Func<Point, IEnumerable<MenuSpec>> rowsAt, IMenuCommandRunner commands)
    {
        var menu = ContextMenuBuilder.Show(target, rowsAt(at), commands);
        Dispatcher.UIThread.RunJobs();
        return PopupContentOf(menu, "context");
    }

    private static MenuItem TopLevelItem(Window window, string header)
    {
        var menu = window.GetLogicalDescendants().OfType<Menu>().Single(candidate => candidate.Name == MainMenuName);
        return menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
    }

    // Walks up from the first item to the child of the overlay layer (or of the top level, for a popup window) that holds the menu: its
    // bounds are the menu's size, so rendering it alone gives the menu.
    private static Control PopupContentOf(ItemsControl menu, string header)
    {
        var firstItem = menu.ContainerFromIndex(0) ?? throw new InvalidOperationException($"The {header} menu opened with no item container.");
        var topLevel = TopLevel.GetTopLevel(firstItem) ?? throw new InvalidOperationException($"The {header} menu item is not in a window.");
        Control panel = firstItem;
        while (panel.GetVisualParent() is Control parent && parent is not OverlayLayer && !ReferenceEquals(parent, topLevel)) panel = parent;
        return panel;
    }
}
