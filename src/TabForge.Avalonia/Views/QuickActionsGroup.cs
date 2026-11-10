using System;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TabForge.Avalonia.Views;

// Owns: the quick action icon button (a command's flat icon button with its hotkey tip, dimmed while the command has no handler), used by the left strip.
// Does not own: the commands (MainWindowCommands), the icons' look (MenuRowToggles.Icon), where the buttons sit (LeftStripFeature).
// Tests: TabForge.Avalonia.Tests.QuickActionsGroupTests.
/// <summary>Builds the quick action icon buttons.</summary>
public static class QuickActionsGroup
{
    /// <summary>The class of a quick action button.</summary>
    public const string ButtonClass = "QuickActionButton";

    internal static Button Button(Func<MainWindowCommands?> commands, string commandId, string name, string iconData)
    {
        var button = new Button
        {
            Tag = commandId, Content = MenuRowToggles.Icon(iconData, IconSizing.Current),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = true,
        };
        UiMetrics.StyleQuickCell(button);
        button.Classes.Add(ButtonClass);
        // The menu row's flat icon button look (transparent at rest, the hover and pressed fills inside its own corner, the theme's tokens).
        PianoRollToolLook.Apply(button);
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
