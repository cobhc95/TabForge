using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Services;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the rows that stand for the menu row's layout toggles inside another menu: one row per toggle (its title from the tooltip, its 16 px icon,
//   its live shortcut, a tick while the window's menu row for it is checked; a click clicks the toolbar button), and the separators the toolbar has.
// Does not own: which toggles a menu takes (the caller's filter), the toggles and their buttons (MenuRowToggles), the menu the rows go in, and the
//   lending of main-menu items (CompactModeMenu).
// Tests: TabForge.Avalonia.Tests.CompactBandHeaderTests, TabForge.Avalonia.Tests.CompactTrackStripTests.
public static class ToolbarMenuRows
{
    /// <summary>The icon size of a row, in DIP (the toolbar draws its icons smaller).</summary>
    private const double RowIconSize = 16;

    /// <summary>
    /// Appends the rows of the toggles <paramref name="include"/> accepts, in the toolbar's order, after a separator when the menu has rows already.
    /// Adds nothing when the window has no toolbar or no toggle is taken.
    /// </summary>
    /// <param name="beforeClick">Runs before a row's button is clicked (the compact menu hands its lent items back there).</param>
    public static void Append(ContextMenu menu, Window window, Func<string, bool> include, HotkeySettings hotkeys, Action beforeClick)
    {
        if (window.GetLogicalDescendants().OfType<StackPanel>().FirstOrDefault(panel => panel.Name == MenuRowToggles.PanelName) is not { } toolbar) return;
        var entries = new List<MenuItem?>();   // null is a separator
        foreach (var toggle in MenuRowToggles.Items)
        {
            if (toggle is null) entries.Add(null);
            else if (include(toggle.CommandId) && toolbar.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, toggle.CommandId)) is { } button)
                entries.Add(Row(window, toggle, button, hotkeys, beforeClick));
        }
        while (entries.Count > 0 && entries[^1] is null) entries.RemoveAt(entries.Count - 1);
        while (entries.Count > 0 && entries[0] is null) entries.RemoveAt(0);
        if (entries.Count == 0) return;
        AddSeparator(menu);
        foreach (var entry in entries)
        {
            if (entry is null) AddSeparator(menu);
            else menu.Items.Add(entry);
        }
    }

    /// <summary>Adds a separator unless the menu is empty or already ends with one.</summary>
    public static void AddSeparator(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
    }

    /// <summary>Sets the item's shortcut text to the live hotkey of <paramref name="commandId"/> (the gesture the menu row shows beside its title).</summary>
    public static void SetShortcut(MenuItem item, string commandId, HotkeySettings hotkeys)
    {
        var shortcut = HotkeyCatalog.DisplayAll(hotkeys, commandId);
        item.InputGesture = MainWindowMenu.GestureOfText(shortcut);
        AvaloniaMenuTemplates.SetGestureText(item, shortcut);
    }

    private static MenuItem Row(Window window, MenuRowToggles.Toggle toggle, Button button, HotkeySettings hotkeys, Action beforeClick)
    {
        var item = new MenuItem { Header = TitleOf(toggle.ToolTip), Icon = MenuRowToggles.Icon(toggle.IconData, RowIconSize), Opacity = button.Opacity };
        SetShortcut(item, toggle.CommandId, hotkeys);
        if (IsLit(window, toggle.CommandId)) { item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = true; }
        item.Click += (_, _) => { beforeClick(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        return item;
    }

    /// <summary>Whether the window's menu row for the command is ticked, or its toolbar toggle is lit (the mode's state, whichever carries it).</summary>
    private static bool IsLit(Window window, string commandId) =>
        window.GetLogicalDescendants().OfType<MenuItem>().Any(item => Equals(item.Tag, commandId) && item.IsChecked)
        || window.GetLogicalDescendants().OfType<ToggleButton>().Any(toggle => Equals(toggle.Tag, commandId) && toggle.Classes.Contains(MenuRowToggles.ToggleClass) && toggle.IsChecked == true);

    /// <summary>The row's name part of its tooltip: up to the first colon, bracket or full stop.</summary>
    private static string TitleOf(string tooltip)
    {
        var end = tooltip.IndexOfAny(new[] { ':', '(', '.' });
        return (end > 0 ? tooltip[..end] : tooltip).Trim();
    }
}
