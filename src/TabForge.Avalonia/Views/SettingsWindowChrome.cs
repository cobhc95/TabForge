using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Shared layout chrome for settings-style windows (Preferences, Fretboard settings):
/// left title block + navigation rail with right divider, rail rows, fixed page header,
/// scrollable card area, and bottom footer band.
/// </summary>
internal static class SettingsWindowChrome
{
    public static Control CreateTitleBlock(string title, string subtitle)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        };
        var sub = new TextBlock
        {
            Text = subtitle,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
        };
        sub[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedTextBrush");
        var panel = new StackPanel
        {
            Children = { heading, sub },
            Margin = new Thickness(ChromeMetrics.SettingsRailRowInset, 14, 12, 10),
        };
        var border = new Border
        {
            Child = panel,
            BorderThickness = DialogMetrics.PrefsRailBorder,
        };
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return border;
    }

    public static ListBox CreateNavigationRail(double rightPadding = 0)
    {
        var rail = new ListBox
        {
            Width = ChromeMetrics.SettingsRailWidth,
            Background = null,
            Padding = new Thickness(0, 0, rightPadding, 0),
            BorderThickness = DialogMetrics.PrefsRailBorder,
        };
        rail[!TemplatedControl.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return rail;
    }

    public static ListBoxItem CreateNavigationItem(string glyphKey, string label, object tag, out SettingsNavigationIconView icon)
    {
        icon = new SettingsNavigationIconView(glyphKey)
        {
            Width = ChromeMetrics.SettingsRailIconSize,
            Height = ChromeMetrics.SettingsRailIconSize,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = ChromeMetrics.SettingsRailLabelFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(ChromeMetrics.SettingsRailLabelGap, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ChromeMetrics.SettingsRailIconSize},*") };
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return new ListBoxItem
        {
            Content = row,
            Tag = tag,
            MinHeight = ChromeMetrics.SettingsRailRowHeight,
            Classes = { AvaloniaFormControlStyles.PrefsNavRowClass },
        };
    }

    public static Border CreateFooterBand(Control left, Control right)
    {
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        var footer = new DockPanel { LastChildFill = false, Children = { left, right } }.WithMargin(12, 8, 12, 8);
        var band = new Border { Child = footer, BorderThickness = FretboardChromeMetrics.FooterBandBorder };
        band[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        band[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return band;
    }

    public static ScrollViewer CreateCardsScroll(Control? content = null)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        return scroll;
    }

    /// <summary>Two-column Preferences-style setting row: title (+ optional muted description) on the left, control right-aligned.</summary>
    public static Grid CreateSettingRow(string title, Control control, string? description = null)
    {
        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        textCol.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
        {
            var desc = new TextBlock { Text = description, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            desc[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedTextBrush");
            textCol.Children.Add(desc);
        }
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 6, 0, 6),
        };
        row.Children.Add(textCol);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }
}
