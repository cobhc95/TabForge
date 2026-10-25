using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace TabForge.Avalonia.Views;

// Owns: the quick actions group (Open and Save icon buttons on one neutral grey rounded fill) at the end of the menu row.
// Does not own: the commands (MainWindowCommands, File.Open / File.Save), the icons' look (MenuRowToggles.Icon), the separator before it (MenuRowToggles).
// Tests: TabForge.Avalonia.Tests.QuickActionsGroupTests.
/// <summary>The Open / Save group next to the menu row's layout icons.</summary>
public static class QuickActionsGroup
{
    /// <summary>The group's element name.</summary>
    public const string GroupName = "QuickActionsGroup";

    /// <summary>The theme token of the group's neutral grey fill.</summary>
    public const string FillToken = "QuickActionsGroupFill";

    /// <summary>The class of a group's action button.</summary>
    public const string ButtonClass = "QuickActionButton";

    private const double IconSize = 14;
    private const string OpenIcon = "M3,6 H9 L11,8 H21 V19 H3 Z";
    private const string SaveIcon = "M4,3 H17 L21,7 V21 H4 Z M8,3 V8 H15 V3 M8,21 V14 H17 V21";

    /// <param name="commands">The window's commands; resolved when a button is used or shown.</param>
    public static Border Build(Func<MainWindowCommands?> commands)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        row.Children.Add(Button(commands, "File.Open", "Open", OpenIcon));
        row.Children.Add(Button(commands, "File.Save", "Save", SaveIcon));
        var group = new Border
        {
            Name = GroupName, Child = row, VerticalAlignment = VerticalAlignment.Center,
        };
        UiMetrics.StyleQuickGroup(group);
        group[!Border.BackgroundProperty] = new DynamicResourceExtension(FillToken);
        group[!Border.BorderBrushProperty] = new DynamicResourceExtension("QuickActionsGroupBorder");   // light: the subtle edge; dark: the fill colour, so unchanged
        return group;
    }

    /// <summary>The group in <paramref name="menuRow"/>, or null.</summary>
    public static Border? In(Control menuRow) =>
        (menuRow as Border)?.Child is DockPanel panel ? panel.Children.OfType<Border>().FirstOrDefault(b => b.Name == GroupName) : null;

    private static Button Button(Func<MainWindowCommands?> commands, string commandId, string name, string iconData)
    {
        var button = new Button
        {
            Tag = commandId, Content = MenuRowToggles.Icon(iconData, IconSizing.Current),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = Platform.AvaloniaFormControlStyles.ControlCornerRadius,
            Template = Platform.AvaloniaButtonTemplates.SingleBorder("Bd"), Background = Platform.AvaloniaPalette.TransparentBrush,
            Focusable = true,
        };
        UiMetrics.StyleQuickCell(button);
        button.Classes.Add(ButtonClass);
        Platform.HoverShade.Attach(button, hover => button.Background = hover && button.IsEnabled ? Platform.HoverShade.IconBrush(button) : Platform.AvaloniaPalette.TransparentBrush);
        // Disabled as the menu item is: dimmed while no handler exists for the command (resolved once the window has its commands).
        button.AttachedToVisualTree += (_, _) =>
        {
            var handled = commands()?.Handles(commandId) == true;
            button.IsEnabled = handled;
            button.Opacity = handled ? 1 : 0.45;
        };
        Platform.Controls.HotkeyTip.Set(button, commandId, name);
        global::Avalonia.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => { if (commands() is { } c && c.Handles(commandId)) c.Run(commandId); };
        return button;
    }
}
