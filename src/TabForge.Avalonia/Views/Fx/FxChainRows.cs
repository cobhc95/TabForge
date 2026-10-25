using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: what the rows of the FX chain list show: a plug-in row (tick, INST/FX, name, vendor) and a container row (chevron, folder
//   glyph, tick, name, number of plug-ins), indented by depth; the rows under a collapsed container are left out. Also the folder
//   drawings: the open folder of a drop target and the New container button.
// Does not own: the selection and drag of rows (FxChainList), what a tick or chevron does (FxChainWindow, FxChainContainers).
// Tests: TabForge.Avalonia.Tests.FxChainListTests.
public static class FxChainRows
{
    public const double KindWidth = 32, SmallFontSize = 11, ContainerMinHeight = 24, ChevronSize = 20, ChevronGlyphSize = 10, TickGap = 6;
    public const double FolderWidth = 16, FolderHeight = 14, FolderStroke = 1.3, ChevronStroke = 1.6;
    public const double NewButtonStroke = 1.3, NewButtonIconWidth = 17, NewButtonIconHeight = 16, NewButtonSize = 24;
    private const string FolderClosed = "M 1,3 L 1,12 L 15,12 L 15,5 L 8,5 L 6.5,3 Z", FolderOpen = "M 1,12 L 1,3 L 6.5,3 L 8,5 L 13,5 L 13,7 M 1,12 L 3.5,7 L 15.3,7 L 13,12 Z";
    private const string NewContainerIconData = "M 9,12 L 1,12 L 1,3 L 6.5,3 L 8,5 L 15,5 L 15,7.5 M 13,9 L 13,14.5 M 10.25,11.75 L 15.75,11.75";
    private const string ChevronOpenData = "M 0,2 L 4.5,7 L 9,2", ChevronCollapsedData = "M 2,0 L 7,4.5 L 2,9";
    public const string RowHelp = "Double-click: float its window · drag: reorder, or onto a container · Ctrl or Shift+click: select several";
    public const string ContainerHelp = "Drag plug-ins onto its middle to put them inside, onto its top or bottom edge to put them in front of or behind it · drag it to move it with its plug-ins";

    /// <summary>The words for a number of plug-ins.</summary>
    public static string Count(int count) => count == 1 ? "1 plug-in" : $"{count} plug-ins";

    /// <summary>Where a container row's name ends, in the list's coordinates (the "Drop inside" badge takes the room behind it).</summary>
    public static double NameRight(Control? row, Visual list)
    {
        if (row is not DockPanel panel || panel.Children.OfType<TextBlock>().LastOrDefault() is not { } name) return 0;
        name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (name.TranslatePoint(new Point(0, 0), list)?.X ?? 0) + name.DesiredSize.Width;
    }

    /// <summary>A container row's folder glyph opens while dragged rows would drop into it, and its count makes room for the badge.</summary>
    public static void ShowOpen(Control? row, bool open)
    {
        if (row is not DockPanel dock) return;
        if (dock.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "ContainerCount") is { } count) count.Opacity = open ? 0 : 1;
        if (dock.Children.OfType<Path>().FirstOrDefault(p => p.Tag is bool) is { } folder && (bool)folder.Tag! != open)
        {
            folder.Tag = open;
            folder.Data = Geometry.Parse(open ? FolderOpen : FolderClosed);
        }
    }

    /// <summary>Fills the list from the rig's tree. The ticks report (plug-in or container, on); the chevron reports the container.</summary>
    public static void Fill(FxChainList list, RigPreset rig, Action<PluginSlot, bool> setEnabled, Action<ChainContainer, bool> setContainerEnabled, Action<ChainContainer> toggleCollapsed)
    {
        void Walk(List<ChainItem> items, int depth)
        {
            foreach (var item in items)
            {
                if (item.Plugin is { } slot)
                {
                    list.Add(Plugin(slot, depth, setEnabled), slot, $"{slot.Name}\n{slot.Path}\n{RowHelp}", slot.Name, depth);
                    continue;
                }
                var c = item.Container!;
                var count = ChainTree.Plugins(item.Children).Count();
                list.Add(Container(c, depth, count, setContainerEnabled, toggleCollapsed), c,
                    $"Container “{c.Name}”: {Count(count)}{(c.Enabled ? "" : ", bypassed")}\n{ContainerHelp}", $"Container {c.Name}, {Count(count)}", depth, c.Name);
                if (!c.Collapsed) Walk(item.Children, depth + 1);
            }
        }
        Walk(ChainTree.Tree(rig), 0);
    }

    /// <summary>The square New container button under the list: a folder outline with a plus, in the stroke of the rows' folder glyph.</summary>
    public static Button NewContainerButton()
    {
        var icon = new Path
        {
            Data = Geometry.Parse(NewContainerIconData), StrokeThickness = NewButtonStroke, StrokeJoin = PenLineJoin.Round,
            Width = NewButtonIconWidth, Height = NewButtonIconHeight, IsHitTestVisible = false,
        };
        icon[!Shape.StrokeProperty] = new DynamicResourceExtension("IconActiveBrush");
        return new Button
        {
            Content = icon, Name = "NewContainerButton", Width = NewButtonSize, Height = NewButtonSize,
            Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
        };
    }

    private static CheckBox Tick(bool on, string what)
    {
        var check = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, TickGap, 0), MinWidth = 0 };
        ToolTip.SetTip(check, on ? $"On (untick to bypass{what})" : $"Bypassed (tick to enable{what})");
        AutomationProperties.SetHelpText(check, on ? "On" : "Bypassed");
        return check;
    }

    private static DockPanel Plugin(PluginSlot slot, int depth, Action<PluginSlot, bool> setEnabled)
    {
        var check = Tick(slot.Enabled, "; with several rows selected: all of them");
        check.Name = "RowTick";
        check.IsCheckedChanged += (_, _) => { if (check.IsChecked != slot.Enabled) setEnabled(slot, check.IsChecked == true); };
        var row = new DockPanel { Margin = new Thickness(depth * FxChainDropMath.Indent, 0, 0, 0) };
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);
        var instrument = slot.Type == PluginSlotType.Instrument;
        var kind = new TextBlock { Text = instrument ? "INST" : "FX", FontSize = SmallFontSize, Width = KindWidth, VerticalAlignment = VerticalAlignment.Center, Name = "RowKind" };
        kind[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(instrument ? "SecondaryTextBrush" : "MutedBrush");
        DockPanel.SetDock(kind, Dock.Left);
        row.Children.Add(kind);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = slot.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontStyle = slot.GroupBypassed ? FontStyle.Italic : FontStyle.Normal,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush"),
        });
        if (slot.Vendor.Length > 0)
        {
            var vendor = new TextBlock { Text = slot.Vendor, FontSize = SmallFontSize, TextTrimming = TextTrimming.CharacterEllipsis, Name = "RowVendor" };
            vendor[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
            names.Children.Add(vendor);
        }
        row.Children.Add(names);
        return row;
    }

    private static DockPanel Container(ChainContainer c, int depth, int count, Action<ChainContainer, bool> setEnabled, Action<ChainContainer> toggleCollapsed)
    {
        var row = new DockPanel { Margin = new Thickness(depth * FxChainDropMath.Indent, 0, 0, 0), MinHeight = ContainerMinHeight };
        var arrow = new Path { Data = Geometry.Parse(c.Collapsed ? ChevronCollapsedData : ChevronOpenData), StrokeThickness = ChevronStroke, Width = ChevronGlyphSize, Height = ChevronGlyphSize, Stretch = Stretch.None, IsHitTestVisible = false };
        arrow[!Shape.StrokeProperty] = new DynamicResourceExtension("IconDefaultBrush");
        var chevron = new Button
        {
            Content = arrow, Width = ChevronSize, Height = ChevronSize, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(0),
            Background = AvaloniaPalette.TransparentBrush, Name = "ContainerChevron",
        };
        ToolTip.SetTip(chevron, c.Collapsed ? "Show the plug-ins of this container" : "Hide the plug-ins of this container");
        AutomationProperties.SetName(chevron, c.Collapsed ? "Expand container" : "Collapse container");
        chevron.Click += (_, _) => toggleCollapsed(c);
        DockPanel.SetDock(chevron, Dock.Left);
        row.Children.Add(chevron);
        var check = Tick(c.Enabled, " the whole container");
        check.Name = "RowTick";
        check.IsCheckedChanged += (_, _) => { if (check.IsChecked != c.Enabled) setEnabled(c, check.IsChecked == true); };
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);
        var folder = new Path { Data = Geometry.Parse(FolderClosed), Tag = false, StrokeThickness = FolderStroke, StrokeJoin = PenLineJoin.Round, Width = FolderWidth, Height = FolderHeight, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Name = "ContainerFolder" };
        folder[!Shape.StrokeProperty] = new DynamicResourceExtension("IconActiveBrush");
        DockPanel.SetDock(folder, Dock.Left);
        row.Children.Add(folder);
        var number = new TextBlock
        {
            Text = count.ToString(CultureInfo.CurrentCulture), FontSize = SmallFontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0), Name = "ContainerCount",
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush"),
        };
        ToolTip.SetTip(number, Count(count) + " inside");
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(number);
        row.Children.Add(new TextBlock
        {
            Text = c.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            FontStyle = c.Enabled ? FontStyle.Normal : FontStyle.Italic, Name = "ContainerName", [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush"),
        });
        return row;
    }
}
