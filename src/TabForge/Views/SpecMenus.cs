using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TabForge.Views;

// Owns: turning MenuSpec trees into themed WPF context menus, the menu separator, and opening a menu at the mouse or (from the keyboard) at an anchor.
// Does not own: what a menu holds (the *Menus spec builders) or what its items do (the caller's run action).
// Tests: TestKeyboardContextMenu, TestBandMenu.
internal static class SpecMenus
{
    /// <summary>The one menu separator style (App.xaml, MenuItem.SeparatorStyleKey) for menus that build their items by hand.</summary>
    public static Separator Separator(FrameworkElement owner) => new() { Style = (Style)owner.FindResource(MenuItem.SeparatorStyleKey) };

    public static ContextMenu New(string name, IEnumerable<MenuSpec> specs, Action<MenuSpec> run, FrameworkElement target, PlacementMode placement = PlacementMode.MousePoint)
    {
        var menu = new ContextMenu
        {
            Style = (Style)target.FindResource(typeof(ContextMenu)),
            Background = (System.Windows.Media.Brush)target.FindResource("Panel2Brush"),
            Foreground = (System.Windows.Media.Brush)target.FindResource("TextBrush"),
            PlacementTarget = target,
            Placement = placement
        };
        System.Windows.Automation.AutomationProperties.SetName(menu, name);
        foreach (var item in Items(specs, run, target)) menu.Items.Add(item);
        return menu;
    }

    /// <summary>
    /// Puts <paramref name="menu"/> just below <paramref name="at"/>, a point in <paramref name="target"/>'s own coordinates. A
    /// PlacementRectangle is carried through the target's whole transform chain (scroll position, the UI-scale layout transform, DPI);
    /// HorizontalOffset / VerticalOffset are not, and put the menu in the wrong place on a scaled display.
    /// </summary>
    public static void PlaceAt(ContextMenu menu, FrameworkElement target, Point at)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.PlacementRectangle = new Rect(at.X, at.Y, 0, 0);
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
    }

    /// <summary>
    /// Opens a context menu. Opened from the keyboard (Shift+F10, the Menu key) it sits at <paramref name="anchor"/> in
    /// <paramref name="target"/>'s coordinates instead of at the mouse, and its first enabled item has the focus so arrows and Enter work.
    /// </summary>
    public static void Open(ContextMenu menu, FrameworkElement target, Point? anchor, bool fromKeyboard)
    {
        if (MainWindow.ContextMenuCapture is { } capture) { capture(menu); return; }   // --capture photographs the menu without opening a popup
        if (fromKeyboard && anchor is { } at) PlaceAt(menu, target, at);
        menu.IsOpen = true;
        if (!fromKeyboard) return;
        menu.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                if (item.IsEnabled && item.IsVisible) { item.Focus(); return; }
        }));
    }

    private static IEnumerable<Control> Items(IEnumerable<MenuSpec> specs, Action<MenuSpec> run, FrameworkElement owner)
    {
        foreach (var spec in specs)
        {
            if (spec.IsSeparator) { yield return Separator(owner); continue; }
            var item = new MenuItem
            {
                Header = spec.Header,
                InputGestureText = spec.Shortcut,
                IsEnabled = spec.Enabled,
                Style = (Style)owner.FindResource(typeof(MenuItem)),
                ToolTip = spec.ToolTip
            };
            if (spec.Children is not null)
                foreach (var child in Items(spec.Children, run, owner)) item.Items.Add(child);
            else if (!spec.IsLabel)
            {
                item.IsCheckable = spec.Checkable;
                item.IsChecked = spec.Checked;
                MenuMarks.SetIsRadio(item, spec.Radio);
                var chosen = spec;
                item.Click += (_, _) => run(chosen);
            }
            yield return item;
        }
    }
}
