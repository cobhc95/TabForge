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

    public static ListBoxItem CreateBandHeader(string band)
    {
        var header = new ListBoxItem
        {
            Content = band,
            IsEnabled = false,
            FontSize = ChromeMetrics.SettingsRailCaptionFontSize,
            FontWeight = FontWeight.Bold,
            Margin = default,
            Padding = new Thickness(ChromeMetrics.SettingsRailCaptionIndent, ChromeMetrics.SettingsRailCaptionTop, 0, ChromeMetrics.SettingsRailCaptionBottom),
        };
        header[!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension("MutedTextBrush");
        return header;
    }

    /// <summary>Preferences-style pull-down ComboBox (same PrefsFieldClass and 200px minimum width as TabForge Settings).</summary>
    public static ComboBox CreateChoiceBox(string name, System.Collections.IEnumerable items, object? selected) => new()
    {
        Name = name,
        ItemsSource = items,
        SelectedItem = selected,
        MinWidth = 200,
        Classes = { AvaloniaFormControlStyles.PrefsFieldClass },
    };

    /// <summary>Two-column Preferences-style setting row matching PreferencesWindow.RowPanel identically: title + ToolWindowParts.Small description on the left, control right-aligned in column 2.</summary>
    public static Grid CreateSettingRow(string title, Control control, string? description = null)
    {
        var textCol = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textCol.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
            textCol.Children.Add(ToolWindowParts.Small(description));
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto") };
        row.Children.Add(textCol);
        Grid.SetColumn(control, 2);
        row.Children.Add(control);
        TooltipPlacement.SetTip(control, !string.IsNullOrWhiteSpace(description) ? description : title);
        return row;
    }
}
