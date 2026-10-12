using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Plugins;

namespace TabForge.Views.Fx;

// Owns: what the rows of the FX chain list show: a plug-in row (tick, INST/FX, name, vendor) and a container row (chevron, folder
//   glyph, tick, name, number of plug-ins), indented by depth; the rows under a collapsed container are left out. Also the folder
//   drawings: the open folder of a drop target and the New container button.
// Does not own: the selection and drag of rows (FxChainList), what a tick or chevron does (FxChainWindow, FxChainContainers).
// Tests: TestFxChainContainerRows.
internal static class FxChainRows
{
    private const double Indent = FxChainDropTarget.Indent;
    private const string FolderClosed = "M 1,3 L 1,12 L 15,12 L 15,5 L 8,5 L 6.5,3 Z", FolderOpen = "M 1,12 L 1,3 L 6.5,3 L 8,5 L 13,5 L 13,7 M 1,12 L 3.5,7 L 15.3,7 L 13,12 Z";

    /// <summary>Where a container row's name ends, in the list's coordinates (the "Drop inside" badge takes the room behind it).</summary>
    public static double NameRight(UIElement row, UIElement list)
    {
        if (row is not DockPanel panel || panel.Children.OfType<TextBlock>().LastOrDefault() is not { } name) return 0;
        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return name.TranslatePoint(new Point(0, 0), list).X + name.DesiredSize.Width;
    }

    /// <summary>A container row's folder glyph opens while dragged rows would drop into it, and its count makes room for the badge.</summary>
    public static void ShowOpen(UIElement row, bool open)
    {
        if (row is DockPanel dock && dock.Children.OfType<TextBlock>().FirstOrDefault() is { } count) count.Visibility = open ? Visibility.Hidden : Visibility.Visible;
        if (row is DockPanel panel && panel.Children.OfType<Path>().FirstOrDefault(p => p.Tag is bool) is { } folder && (bool)folder.Tag != open)
        { folder.Tag = open; folder.Data = Geometry.Parse(open ? FolderOpen : FolderClosed); }
    }

    /// <summary>Fills the list from the rig's tree. The ticks report (plug-in or container, on).</summary>
    public static void Fill(FxChainList list, RigPreset rig, Action<PluginSlot, bool> setEnabled, Action<ChainContainer, bool> setContainerEnabled, Action<ChainContainer> toggleCollapsed)
    {
        void Walk(List<ChainItem> items, int depth)
        {
            foreach (var item in items)
            {
                if (item.Plugin is { } slot)
                {
                    list.Add(Plugin(slot, depth, setEnabled), slot, $"{slot.Name}\n{slot.Path}\nDouble-click: float its window · drag: reorder, or onto a container · Ctrl or Shift+click: select several", slot.Name, depth);
                    continue;
                }
                var c = item.Container!;
                var count = ChainTree.Plugins(item.Children).Count();
                list.Add(Container(c, depth, count, setContainerEnabled, toggleCollapsed), c,
                    $"Container “{c.Name}”: {Count(count)}{(c.Enabled ? "" : ", bypassed")}\nDrag plug-ins onto its middle to put them inside, onto its top or bottom edge to put them in front of or behind it · drag it to move it with its plug-ins", $"Container {c.Name}, {Count(count)}", depth, c.Name);
                if (!c.Collapsed) Walk(item.Children, depth + 1);
            }
        }
        Walk(ChainTree.Tree(rig), 0);
    }

    /// <summary>The square New container button under the list: a folder outline with a plus, in the stroke of the rows' folder glyph.</summary>
    public static Button NewContainerButton(Action click)
    {
        var icon = new Path { Data = Geometry.Parse("M 9,12 L 1,12 L 1,3 L 6.5,3 L 8,5 L 15,5 L 15,7.5 M 13,9 L 13,14.5 M 10.25,11.75 L 15.75,11.75"), StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round, Width = 17, Height = 16, IsHitTestVisible = false };
        icon.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var button = new Button { Content = icon, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0) };
        button.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualHeight") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.Self) });   // as tall as the text buttons beside it, and square
        TooltipShortcuts.Bind(button, "New container: an empty container behind the focused row; drag plug-ins onto it", null);
        button.Click += (_, _) => click();
        return button;
    }

    internal static string Count(int count) => count == 1 ? "1 plug-in" : $"{count} plug-ins";

    private static CheckBox Tick(bool on, string what)
    {
        var check = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = on ? $"On (untick to bypass{what})" : $"Bypassed (tick to enable{what})" };
        System.Windows.Automation.AutomationProperties.SetHelpText(check, on ? "On" : "Bypassed");
        return check;
    }

    private static DockPanel Plugin(PluginSlot slot, int depth, Action<PluginSlot, bool> setEnabled)
    {
        var check = Tick(slot.Enabled, "; with several rows selected: all of them");
        check.Click += (_, _) => setEnabled(slot, check.IsChecked == true);
        var row = new DockPanel { Margin = new Thickness(depth * Indent, 0, 0, 0) };
        row.Children.Add(check);
        var kind = new TextBlock { Text = slot.Type == PluginSlotType.Instrument ? "INST" : "FX", FontSize = Services.ThemeService.MinFontSize, Width = 32, VerticalAlignment = VerticalAlignment.Center };
        kind.SetResourceReference(TextBlock.ForegroundProperty, slot.Type == PluginSlotType.Instrument ? "AccentBrush" : "MutedBrush");
        row.Children.Add(kind);
        var names = new StackPanel();
        names.Children.Add(new TextBlock { Text = slot.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontStyle = slot.GroupBypassed ? FontStyles.Italic : FontStyles.Normal });   // italic: its container is bypassed
        if (slot.Vendor.Length > 0)
        {
            var vendor = new TextBlock { Text = slot.Vendor, FontSize = Services.ThemeService.MinFontSize, TextTrimming = TextTrimming.CharacterEllipsis };
            vendor.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            names.Children.Add(vendor);
        }
        row.Children.Add(names);
        return row;
    }

    private static DockPanel Container(ChainContainer c, int depth, int count, Action<ChainContainer, bool> setEnabled, Action<ChainContainer> toggleCollapsed)
    {
        var row = new DockPanel { Margin = new Thickness(depth * Indent, 0, 0, 0), MinHeight = 24 };
        // The chevron: points right when collapsed, down when open.
        var arrow = new Path { Data = Geometry.Parse(c.Collapsed ? "M 2,0 L 7,4.5 L 2,9" : "M 0,2 L 4.5,7 L 9,2"), StrokeThickness = 1.6, Width = 10, Height = 10, Stretch = Stretch.None };
        arrow.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        var chevron = new Button { Content = arrow, Width = 20, Height = 20, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = c.Collapsed ? "Show the plug-ins of this container" : "Hide the plug-ins of this container" };
        System.Windows.Automation.AutomationProperties.SetName(chevron, c.Collapsed ? "Expand container" : "Collapse container");
        chevron.Click += (_, _) => toggleCollapsed(c);
        row.Children.Add(chevron);
        var check = Tick(c.Enabled, " the whole container");
        check.Click += (_, _) => setEnabled(c, check.IsChecked == true);
        row.Children.Add(check);
        // A folder outline in the line style of the app's icons.
        var folder = new Path { Data = Geometry.Parse(FolderClosed), Tag = false, StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round, Width = 16, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        folder.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        row.Children.Add(folder);
        var number = new TextBlock { Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = Services.ThemeService.MinFontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0), ToolTip = Count(count) + " inside" };
        number.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(number);
        row.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontStyle = c.Enabled ? FontStyles.Normal : FontStyles.Italic });
        return row;
    }
}
