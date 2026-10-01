using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow, right-click menus built from MenuSpec trees (timeline, fretboard, score): turns the data into WPF menus.
public partial class MainWindow
{
    /// <summary>The one menu separator style (App.xaml, MenuItem.SeparatorStyleKey) for menus that build their items by hand.</summary>
    private Separator MenuSeparator() => new() { Style = (Style)FindResource(MenuItem.SeparatorStyleKey) };

    /// <summary>Saves the timeline's four appearance toggles (they are Preferences rows now) after the View menu or Preferences changed them.</summary>
    private void SaveTimelineAppearance()
    {
        var t = _settings.Timeline;
        t.ShowIndividualNotes = Arrangement.ShowIndividualNotes;
        t.ShowContinuousLine = Arrangement.ShowContinuousBlocks;
        t.HideEmptyGrid = Arrangement.HideEmptyTimelineGrid;
        t.BarGlow = Arrangement.ShowBarGlow;
        SaveSettings();
    }

    private ContextMenu NewTimelineMenu(string name, IEnumerable<MenuSpec> specs, Action<TimelineCommand> run) =>
        NewSpecMenu(name, specs, spec => run(spec.Command), Arrangement, PlacementMode.MousePoint);

    private ContextMenu NewSpecMenu(string name, IEnumerable<MenuSpec> specs, Action<MenuSpec> run, FrameworkElement target, PlacementMode placement = PlacementMode.MousePoint)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (System.Windows.Media.Brush)FindResource("Panel2Brush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            PlacementTarget = target,
            Placement = placement
        };
        System.Windows.Automation.AutomationProperties.SetName(menu, name);
        foreach (var item in BuildMenuItems(specs, run)) menu.Items.Add(item);
        return menu;
    }

    /// <summary>
    /// Opens a context menu. Opened from the keyboard (Shift+F10, the Menu key) it sits at <paramref name="anchor"/> in
    /// <paramref name="target"/>'s coordinates instead of at the mouse, and its first enabled item has the focus so arrows and Enter work.
    /// </summary>
    /// <summary>
    /// Puts <paramref name="menu"/> just below <paramref name="at"/>, a point in <paramref name="target"/>'s own coordinates. A
    /// PlacementRectangle is carried through the target's whole transform chain (scroll position, the UI-scale layout transform, DPI);
    /// HorizontalOffset / VerticalOffset are not, and put the menu in the wrong place on a scaled display.
    /// </summary>
    internal static void PlaceContextMenuAt(ContextMenu menu, FrameworkElement target, Point at)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.PlacementRectangle = new Rect(at.X, at.Y, 0, 0);
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
    }

    private static void OpenContextMenu(ContextMenu menu, FrameworkElement target, Point? anchor, bool fromKeyboard)
    {
        if (ContextMenuCapture is { } capture) { capture(menu); return; }   // --capture photographs the menu without opening a popup
        if (fromKeyboard && anchor is { } at) PlaceContextMenuAt(menu, target, at);
        menu.IsOpen = true;
        if (!fromKeyboard) return;
        menu.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                if (item.IsEnabled && item.IsVisible) { item.Focus(); return; }
        }));
    }

    private IEnumerable<Control> BuildMenuItems(IEnumerable<MenuSpec> specs, Action<MenuSpec> run)
    {
        foreach (var spec in specs)
        {
            if (spec.IsSeparator) { yield return MenuSeparator(); continue; }
            var item = new MenuItem
            {
                Header = spec.Header,
                InputGestureText = spec.Shortcut,
                IsEnabled = spec.Enabled,
                Style = (Style)FindResource(typeof(MenuItem)),
                ToolTip = spec.ToolTip
            };
            if (spec.Children is not null)
                foreach (var child in BuildMenuItems(spec.Children, run)) item.Items.Add(child);
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
