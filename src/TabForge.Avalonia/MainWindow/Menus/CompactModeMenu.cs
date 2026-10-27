using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the menu that stands in for the window's menu row while compact mode hides it (the compact strip's right-click and caption menu): first the
//   toolbar's rows (ToolbarMenuRows: every layout toggle, with its icon, shortcut and tick), then the main menus as sub-menus, whose real items are
//   lent to it while it is open and handed back in their order when it closes.
// Does not own: the main menu and what its items do (MainWindowMenu, MainWindowCommands), the toolbar rows (ToolbarMenuRows), when the menu opens
//   (CompactTrackStrip), the mode itself (MainWindowCompactMode).
// Tests: TabForge.Avalonia.Tests.CompactTrackStripTests, TabForge.Avalonia.Tests.CompactBandHeaderTests.
/// <summary>The compact mode stand-in for the hidden menu row.</summary>
public sealed class CompactModeMenu
{
    private readonly Window _window;
    private readonly List<(MenuItem Top, MenuItem Stand)> _lent = new();

    public CompactModeMenu(Window window) => _window = window;

    /// <summary>Builds the stand-in menu from the toolbar and the main menu as they are now, lending the main menu's items until the menu closes.</summary>
    public ContextMenu Build()
    {
        Restore();
        var menu = MenuStability.CreateContextMenu();
        var hotkeys = HotkeyTip.GetSource(_window)?.Invoke() ?? new HotkeySettings();
        ToolbarMenuRows.Append(menu, _window, _ => true, hotkeys, Restore);
        ToolbarMenuRows.AddSeparator(menu);
        foreach (var top in _window.GetLogicalDescendants().OfType<Menu>().SelectMany(row => row.Items.OfType<MenuItem>()).ToList())
        {
            MainWindowMenu.RefreshGestures(top, hotkeys);
            var stand = new MenuItem { Header = top.Header };
            var items = top.Items.Cast<object>().ToList();
            top.Items.Clear();
            foreach (var item in items) stand.Items.Add(item);
            _lent.Add((top, stand));
            menu.Items.Add(stand);
        }
        menu.Closed += (_, _) => Restore();
        return menu;
    }

    /// <summary>Opens the stand-in menu on <paramref name="anchor"/> at <paramref name="placement"/>.</summary>
    public void Open(Control anchor, PlacementMode placement)
    {
        var menu = Build();
        menu.Placement = placement;
        menu.Open(anchor);
    }

    /// <summary>Hands every lent item back to its main menu, in its order.</summary>
    public void Restore()
    {
        foreach (var (top, stand) in _lent)
        {
            var items = stand.Items.Cast<object>().ToList();
            stand.Items.Clear();
            foreach (var item in items) top.Items.Add(item);
        }
        _lent.Clear();
    }
}
