using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

// Owns: the mixer's group collapse (chevron, keys, menus, collapse-all button), the track colour chip and the group rules entry.
// Does not own: the collapsed state (MixerSettings.CollapsedGroups, shared with the track list; the host changes it), the rules editor (GroupRulesDialog).
// Tests: TestMixerGroupCollapse, TestMixerGroupRules.
public sealed partial class MixerWindow
{
    private readonly Button _collapseAll = new() { Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private bool IsCollapsed(string group) => _host.Project.Mixer.CollapsedGroups.Contains(group);

    private IEnumerable<string> GroupNames() => _groupPanels.Keys;

    /// <summary>Buttons beside "Group tracks": the rules editor and the collapse / expand-all toggle.</summary>
    private void AddGroupButtons(Panel row)
    {
        var rules = new Button { Content = "Group rules…", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        TooltipShortcuts.Bind(rules, "Choose which instruments go in which group: add, rename, reorder groups and set their rules", "Mixer.GroupRules");
        rules.Click += (_, _) => EditGroupRules();
        row.Children.Add(rules);
        _collapseAll.Click += (_, _) => _host.SetGroupsCollapsed(GroupNames().ToList(), !GroupNames().All(IsCollapsed));
        row.Children.Add(_collapseAll);
    }

    private void UpdateCollapseAll()
    {
        var all = _groupPanels.Count > 0 && GroupNames().All(IsCollapsed);
        _collapseAll.Content = all ? "Expand all" : "Collapse all";
        _collapseAll.IsEnabled = _groupPanels.Count > 0;
        TooltipShortcuts.Bind(_collapseAll, all ? "Show the tracks of every group" : "Show only the group rows", all ? "Mixer.ExpandAllGroups" : "Mixer.CollapseAllGroups");
    }

    /// <summary>The arrow at the left of a group row: click toggles the group.</summary>
    private Button Chevron(string group)
    {
        var collapsed = IsCollapsed(group);
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(collapsed ? "M0,0 L5,4 L0,8 Z" : "M0,0 L8,0 L4,5 Z"), Stretch = Stretch.Uniform,
            Width = collapsed ? 6 : 9, Height = collapsed ? 9 : 6
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextBrush");
        var button = new Button
        {
            Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Top, Focusable = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = path,
            ToolTip = collapsed ? "Expand the group (Right arrow)" : "Collapse the group (Left arrow)"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, (collapsed ? "Expand " : "Collapse ") + group);
        button.Click += (_, _) => _host.SetGroupsCollapsed(new[] { group }, null);
        return button;
    }

    /// <summary>Left / Right / Enter on a focused group row, a double-click on the row (its buttons and sliders keep their own clicks), and its right-click menu.</summary>
    private void AttachGroupCollapse(Border groupRow, string group)
    {
        groupRow.Focusable = true;
        groupRow.FocusVisualStyle = null;
        groupRow.KeyDown += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, groupRow)) return;
            var collapsed = IsCollapsed(group);
            if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Left && !collapsed || e.Key == Key.Right && collapsed)
            {
                e.Handled = true;
                _host.SetGroupsCollapsed(new[] { group }, null);
            }
        };
        var menu = new ContextMenu();
        var toggle = new MenuItem { Header = IsCollapsed(group) ? "Expand this group" : "Collapse this group" };
        toggle.Click += (_, _) => _host.SetGroupsCollapsed(new[] { group }, null);
        var collapseAll = new MenuItem { Header = "Collapse all groups" };
        collapseAll.Click += (_, _) => _host.SetGroupsCollapsed(GroupNames().ToList(), true);
        var expandAll = new MenuItem { Header = "Expand all groups" };
        expandAll.Click += (_, _) => _host.SetGroupsCollapsed(GroupNames().ToList(), false);
        menu.Items.Add(toggle);
        menu.Items.Add(collapseAll);
        menu.Items.Add(expandAll);
        groupRow.ContextMenu = menu;
    }

    /// <summary>Opens the app's colour palette menu under <paramref name="anchor"/>; <paramref name="apply"/> gets the chosen colour.</summary>
    private static void ShowColourMenu(FrameworkElement anchor, string? current, Action<string> apply)
    {
        var menu = ColourMenu(current, apply);
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    /// <summary>The track colour palette menu (also photographed by --capture "mixer-colour").</summary>
    internal static ContextMenu ColourMenu(string? current, Action<string> apply)
    {
        var menu = new ContextMenu();
        foreach (var (colourName, hex) in TrackControlWidgets.TrackColourPalette)
        {
            var item = new MenuItem
            {
                Header = colourName, IsCheckable = true, IsChecked = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase),
                Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = BrushOf(hex) }
            };
            item.Click += (_, _) => apply(hex);
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>The small colour square of a track row; opens the track colour palette (the track list's own).</summary>
    private Button ColourChip(TrackModel track)
    {
        var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = BrushOf(track.ColorHex) };
        var chip = new Button
        {
            Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(22, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top, Content = swatch,
            ToolTip = $"Colour of '{track.Name}': click to choose"
        };
        System.Windows.Automation.AutomationProperties.SetName(chip, $"Colour of {track.Name}");
        chip.Click += (_, _) => PickTrackColour(track, chip);
        _syncers.Add(() => swatch.Background = BrushOf(track.ColorHex));
        return chip;
    }

    private void PickTrackColour(TrackModel track, FrameworkElement anchor) =>
        ShowColourMenu(anchor, track.ColorHex, hex => { _host.SetTrackColour(track, hex); Rebuild(); });

    /// <summary>Right-click items of a track row: its colour, and returning a manually moved track to its rule group.</summary>
    private void AddTrackMenuItems(ContextMenu menu, TrackModel track, Button chip)
    {
        var colour = new MenuItem { Header = "Track colour…" };
        colour.Click += (_, _) => PickTrackColour(track, chip);
        menu.Items.Insert(0, colour);
        menu.Items.Insert(1, new Separator());
        if (track.MixerGroup is null) return;
        var back = new MenuItem { Header = "Return to rule group" };
        back.ToolTip = "This track was moved by hand; put it back in the group its instrument and the group rules give it";
        back.Click += (_, _) =>
        {
            _host.BeginMixerEdit();
            track.MixerGroup = null;
            _host.MixerChanged(recompile: true);
            Dispatcher.BeginInvoke(Rebuild);
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(back);
    }

    /// <summary>Opens the group rules editor; OK applies it as one undo step.</summary>
    public void EditGroupRules()
    {
        var mixer = _host.Project.Mixer;
        if (GroupRulesDialog.Show(this, mixer, out var result) is not true) return;
        if (result.Scope == GroupRulesScope.AllSongs) _host.SetAppGroupRules(result);
        else
        {
            _host.BeginMixerEdit();
            if (result.Scope == GroupRulesScope.ResetToApp) MixerRules.ResetToApp(_host.Project);
            else MixerRules.Apply(_host.Project, result.Groups, result.Fallback, result.Renamed);
            _host.MixerChanged(recompile: true);
        }
        foreach (var g in result.Groups.Where(g => g.Colour is not null)) _host.SetGroupColour(g.Name, g.Colour!);
        Rebuild();

    }

    /// <summary>Scrolls to the last group (used by the capture script).</summary>
    public void ScrollToEnd()
    {
        UpdateLayout();
        _scroller.ScrollToEnd();
    }
}
