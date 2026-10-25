using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Menus;

namespace TabForge.Avalonia.Views;

// Owns: turning a Core menu (the MenuSpec rows of TimelineMenus, TrackRowMenus and ScoreMenus) into an Avalonia ContextMenu, and opening it on a
//   control's right-click. A row runs only when the window's command runner handles its command id (ContextMenuRows.CommandIdOf); any other row
//   renders disabled, so nothing runs that the Avalonia build does not run. Ticks, radio dots, submenus, separators, tooltips and shortcut gestures
//   come from the row.
// Does not own: the rows (Core), the rows for each control (ContextMenuRows), the command handlers (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.ContextMenuTests.
public static class ContextMenuBuilder
{
    /// <summary>Right-click on <paramref name="target"/> builds the rows for the pointer's position and opens them there, runnable through the host's commands.</summary>
    public static void Attach(Control target, ContextMenuHost host, Func<Point, IEnumerable<MenuSpec>> rowsAt) =>
        target.ContextRequested += (_, e) =>
        {
            if (!e.TryGetPosition(target, out var at)) return;
            var rows = rowsAt(at).ToList();
            if (rows.Count == 0) return;
            Show(target, rows, host.Commands);
            e.Handled = true;
        };

    /// <summary>Builds the menu for <paramref name="rows"/> and opens it over <paramref name="target"/> at the pointer; returns it.</summary>
    public static ContextMenu Show(Control target, IEnumerable<MenuSpec> rows, IMenuCommandRunner commands)
    {
        var menu = Build(rows, commands);
        menu.Open(target);
        return menu;
    }

    /// <summary>The menu for <paramref name="rows"/>, not opened.</summary>
    public static ContextMenu Build(IEnumerable<MenuSpec> rows, IMenuCommandRunner commands)
    {
        var menu = MenuStability.CreateContextMenu();
        foreach (var row in rows) menu.Items.Add(ToElement(row, commands));
        return menu;
    }

    private static Control ToElement(MenuSpec row, IMenuCommandRunner commands)
    {
        if (row.IsSeparator) return new Separator();
        var item = new MenuItem { Header = row.Header, InputGesture = MainWindowMenu.GestureOfText(row.Shortcut) };
        AvaloniaMenuTemplates.SetGestureText(item, row.Shortcut);
        if (row.ToolTip is not null) ToolTip.SetTip(item, row.ToolTip);
        if (row.Children is not null)
        {
            foreach (var child in row.Children) item.Items.Add(ToElement(child, commands));
            return item;
        }
        if (row.Checkable)
        {
            item.ToggleType = row.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox;
            item.IsChecked = row.Checked;
        }
        var commandId = row.IsLabel ? null : ContextMenuRows.CommandIdOf(row);
        var runnable = commandId is not null && commands.Handles(commandId);
        item.IsEnabled = row.Enabled && runnable;
        if (runnable) item.Click += (_, _) => commands.Run(commandId!, row.Arg);
        return item;
    }
}
