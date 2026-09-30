using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow, arrangement timeline context menus: turns the MenuSpec trees of TimelineMenus into WPF menus.
public partial class MainWindow
{
    /// <summary>Display text of a hotkey id from the user's bindings ("" when unbound).</summary>
    private TimelineDisplayState TimelineDisplay() =>
        new(Arrangement.ShowIndividualNotes, Arrangement.ShowContinuousBlocks, Arrangement.HideEmptyTimelineGrid, Arrangement.ShowBarGlow);

    /// <summary>Display toggles shared by the bar and the selection menu.</summary>
    private bool RunDisplayCommand(TimelineCommand command)
    {
        switch (command)
        {
            case TimelineCommand.ShowIndividualNotes: Arrangement.ShowIndividualNotes = !Arrangement.ShowIndividualNotes; break;
            case TimelineCommand.ShowContinuousLine: Arrangement.ShowContinuousBlocks = !Arrangement.ShowContinuousBlocks; break;
            case TimelineCommand.HideEmptyGrid: Arrangement.HideEmptyTimelineGrid = !Arrangement.HideEmptyTimelineGrid; return true;
            case TimelineCommand.SubtleBarGlow: Arrangement.ShowBarGlow = !Arrangement.ShowBarGlow; return true;
            default: return false;
        }
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        return true;
    }

    private ContextMenu NewTimelineMenu(string name, IEnumerable<MenuSpec> specs, Action<TimelineCommand> run)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (System.Windows.Media.Brush)FindResource("Panel2Brush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            PlacementTarget = Arrangement,
            Placement = PlacementMode.MousePoint
        };
        System.Windows.Automation.AutomationProperties.SetName(menu, name);
        foreach (var item in BuildMenuItems(specs, run)) menu.Items.Add(item);
        return menu;
    }

    private IEnumerable<Control> BuildMenuItems(IEnumerable<MenuSpec> specs, Action<TimelineCommand> run)
    {
        foreach (var spec in specs)
        {
            if (spec.IsSeparator) { yield return new Separator { Style = (Style)FindResource(typeof(Separator)) }; continue; }
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
                var command = spec.Command;
                item.Click += (_, _) => run(command);
            }
            yield return item;
        }
    }
}
