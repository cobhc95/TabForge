using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.CompactMode;

// Owns: the menu that stands in for the menu row hidden by compact mode (the strip's right-click and caption menu, and the rows added to the Band view's roll-down): one row per icon-row button
//   (same icon, text, key and lit state; a click clicks the button) and the main menus as sub-menus, whose real items are lent
//   to it while it is open and handed back in their order when it closes.
// Does not own: the buttons, the main menu and what their items do; when the menu opens (CompactModeStrip).
// Tests: TestCompactMode.
internal sealed class CompactModeMenu
{
    private readonly Panel _iconRow;
    private readonly Menu _mainMenu;
    private readonly List<(MenuItem Top, MenuItem Stand)> _lent = new();

    public CompactModeMenu(Panel iconRow, Menu mainMenu)
    {
        _iconRow = iconRow;
        _mainMenu = mainMenu;
    }

    /// <summary>Fills <paramref name="menu"/> from the icon row and the main menu as they are now.</summary>
    public void Fill(ContextMenu menu)
    {
        Restore();
        menu.Items.Clear();
        Append(menu);
    }

    /// <summary>Adds the same rows below the rows <paramref name="menu"/> already has (after a separator); the caller calls <see cref="Restore"/> when that menu closes.</summary>
    public void Append(ContextMenu menu)
    {
        Restore();
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
        var first = menu.Items.Count;
        foreach (var child in _iconRow.Children)
        {
            if (child is ButtonBase button) menu.Items.Add(IconRow(button));
            else if (child is Rectangle && menu.Items.Count > first && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
        }
        if (menu.Items.Count > first && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
        foreach (var top in _mainMenu.Items.OfType<MenuItem>().ToList())
        {
            top.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, top));   // the menu's own refresh of its checks and dynamic rows
            var stand = Styled(new MenuItem { Header = top.Header });
            var items = top.Items.Cast<object>().ToList();
            top.Items.Clear();
            foreach (var item in items) stand.Items.Add(item);
            _lent.Add((top, stand));
            menu.Items.Add(stand);
        }
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

    private MenuItem IconRow(ButtonBase button)
    {
        var item = Styled(new MenuItem { Header = Title(TooltipShortcuts.GetBaseText(button) ?? button.ToolTip as string ?? "") });
        if (button.Content is Path path)
        {
            // A second path with the same geometry: the button keeps its own.
            var icon = new Path { Data = path.Data };
            icon.SetResourceReference(FrameworkElement.StyleProperty, "IconPath");
            icon.Width = icon.Height = 16;
            item.Icon = icon;
        }
        if (TooltipShortcuts.GetCommand(button) is { Length: > 0 } command) item.InputGestureText = HotkeyCatalog.DisplayAll(TooltipShortcuts.Hotkeys, command);
        // Lit: the button's own "on" mark, a checked toggle, or the accent background its style gives it.
        var lit = button.Tag as string == "on" || button is ToggleButton { IsChecked: true }
            || (button.TryFindResource("AccentSoftBrush") is { } accent && ReferenceEquals(button.Background, accent));
        if (lit) { item.IsCheckable = true; item.IsChecked = true; }
        // The lent items go home first: a button that works through its main-menu row (and ticks it) must find that row in its menu.
        item.Click += (_, _) => { Restore(); button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); };
        return item;
    }

    /// <summary>The tooltip's name part: up to the first colon, bracket or full stop.</summary>
    private static string Title(string text)
    {
        var end = text.IndexOfAny(new[] { ':', '(', '.' });
        return (end > 0 ? text[..end] : text).Trim();
    }

    private static MenuItem Styled(MenuItem item)
    {
        if (Application.Current?.TryFindResource(typeof(MenuItem)) is Style style) item.Style = style;
        return item;
    }
}
