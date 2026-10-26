using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System.Collections.Generic;
using TabForge.Avalonia.Views.Docking;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the menu row (the menu bar plus MenuRowToggles docked right): the top-level menus in the app's order, filled from MainMenuTable.WithFeatureRows(), each item tagged with its
//   command id, enabled when MainWindowCommands has a handler for it, showing its hotkey as gesture text (re-read from the live hotkeys when a menu opens); the View menu's Layouts and Panels items (DockViewMenus) above
//   its Reset all panels row; the preview dialogs no row names, in one submenu at the end of Tools.
// Does not own: the rows (MainMenuTable in Core), the handlers (MainWindowCommands), the dialogs, or the Layouts and Panels rows (DockViewMenus).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
/// <summary>Builds the Avalonia main menu from the Core menu table.</summary>
public static class MainWindowMenu
{
    /// <summary>The menu bar height.</summary>
    public const double BarHeight = 30;

    /// <summary>The narrowest menu row (DIP) that still shows the Piano / Tab pills beside the twelve menus and the quick buttons.</summary>
    public const double PillsMinRow = 1180;

    /// <summary>The command of the View menu row that the Layouts and Panels items sit above.</summary>
    private const string ResetPanelsCommandId = "View.ResetPanels";

    /// <summary>The top-level menus in the order the app shows them (the order of the menu skeleton).</summary>
    public static readonly IReadOnlyList<string> TopLevelOrder = new[] { "_File", "_Edit", "_Bar", "_Track", "_Note", "_Effects", "_Sections", "_Tools", "_Sound", "_View", "_Options", "_Help" };

    /// <param name="owner">The window the track prompts open over.</param>
    /// <param name="viewMenus">The Layouts and Panels items the View menu holds, before its Reset all panels row.</param>
    /// <param name="hotkeys">The hotkeys whose gesture text the rows show.</param>
    /// <returns>The menu row: the menu on the left, the layout toggles (MenuRowToggles) docked right.</returns>
    public static Control Build(MainWindowCommands commands, Window owner, DockViewMenus viewMenus, HotkeySettings hotkeys, Control? modePills = null)
    {
        var groups = MainMenuTable.WithFeatureRows();
        var unknown = groups.Select(g => g.Menu).Distinct().Except(TopLevelOrder).ToList();
        if (unknown.Count > 0) throw new InvalidOperationException($"MainMenuTable names menus the window does not list: {string.Join(", ", unknown)}.");
        // The bar: 30 px on the panel colour with a soft 1 px bottom line; top-level headers 16 px in, and the same inset after the label (AvaloniaMenuTemplates centres it).
        var menu = MenuStability.CreateMenu();
        menu.Name = "MainMenu"; menu[!Layoutable.HeightProperty] = new DynamicResourceExtension(UiDensityResources.MenuInnerHeight); menu.Margin = new global::Avalonia.Thickness(8, 0, 0, 0);
        menu[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        var toggles = Views.MenuRowToggles.Build(commands);
        DockPanel.SetDock(toggles, Dock.Right);
        // The Open / Save group sits just left of the layout icons (added after them, so it docks beside them), set off by the icons' own separator.
        var divider = Views.MenuRowToggles.Separator();
        DockPanel.SetDock(divider, Dock.Right);
        var quick = Views.QuickActionsGroup.Build(() => commands);
        DockPanel.SetDock(quick, Dock.Right);
        if (modePills is not null) DockPanel.SetDock(modePills, Dock.Right);   // added after the quick buttons, so it sits just left of them
        var menuRow = new Border
        {
            Name = "MainMenuRow", BorderThickness = new global::Avalonia.Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = true, Children = { toggles, divider, quick } },
        };
        var dock = (DockPanel)menuRow.Child!;
        if (modePills is not null) dock.Children.Add(modePills);
        dock.Children.Add(menu);
        // The menus keep priority: a row narrower than PillsMinRow hides the pills.
        if (modePills is not null) menuRow.SizeChanged += (_, e) => modePills.IsVisible = e.NewSize.Width >= PillsMinRow;
        menuRow[!Layoutable.HeightProperty] = new DynamicResourceExtension(UiDensityResources.MenuBarHeight);
        menuRow[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        menuRow[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        foreach (var name in TopLevelOrder)
        {
            // Vertical padding 0 keeps the bar height; the side insets are the shared top-level inset (AvaloniaMenuTemplates). The menu itself starts 8 DIP in so File's text stays 16 DIP from the window edge.
            var top = new MenuItem { Header = name, Padding = new global::Avalonia.Thickness(0), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch };
            TabForge.Avalonia.Platform.HoverShade.Attach(top, hovered => top.Background = hovered ? TabForge.Avalonia.Platform.HoverShade.Brush(top) : global::Avalonia.Media.Brushes.Transparent);
            foreach (var row in groups.Where(g => g.Menu == name).SelectMany(g => g.Rows))
            {
                if (row.CommandId == ResetPanelsCommandId) foreach (var item in viewMenus.Items) top.Items.Add(item);
                top.Items.Add(ToElement(row, commands, hotkeys));
            }
            if (name == "_Tools")
            {
                top.Items.Add(PreviewPromptsMenu.Build(owner));
                if (commands.Unclaimed.Count > 0) top.Items.Add(PreviewsMenu(commands.Unclaimed));
            }
            // Gesture text is re-read from the live hotkeys (the window's HotkeyTip source) each time the menu opens, so a changed shortcut shows at once.
            top.SubmenuOpened += (_, _) => RefreshGestures(top, Platform.Controls.HotkeyTip.GetSource(top)?.Invoke() ?? hotkeys);
            menu.Items.Add(top);
        }
        return menuRow;
    }

    /// <summary>The gesture a menu item shows beside its caption: the hotkey's display text as a key gesture (display only; the window's keyboard runs the key).</summary>
    public static KeyGesture? GestureOf(string commandId, HotkeySettings hotkeys) => GestureOfText(HotkeyCatalog.DisplayAll(hotkeys, commandId));

    /// <summary>The gesture for display text such as "Ctrl+Shift+V", or null when the text is empty or is not a key gesture.</summary>
    public static KeyGesture? GestureOfText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // Several gestures joined ("T, Ctrl") or a punctuation key the parser has no name for ("[", "/") show no gesture text;
        // they are skipped here so the start-up menu build does not throw and catch for them.
        var last = text[(text.LastIndexOf('+') + 1)..];
        if (text.Contains(", ", StringComparison.Ordinal) || (last.Length == 1 && char.IsPunctuation(last[0]))) return null;
        // A digit key reads as a number, and the key parser maps "1" to the enum value 1 (Cancel): the parser's name for it is "D1".
        var key = text[(text.LastIndexOf('+') + 1)..];
        if (key.Length == 1 && char.IsDigit(key[0])) text = text[..^1] + "D" + key;
        try { return KeyGesture.Parse(text); }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            // Display text the key parser does not read (several gestures joined, or a key name it lacks such as "[") shows no gesture text.
            return null;
        }
    }

    /// <summary>Sets every command item under <paramref name="parent"/> to show its current hotkey from <paramref name="hotkeys"/>.</summary>
    public static void RefreshGestures(MenuItem parent, HotkeySettings hotkeys)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            if (item.Tag is string id)
            {
                var shortcut = HotkeyCatalog.DisplayAll(hotkeys, id);
                item.InputGesture = GestureOfText(shortcut);
                AvaloniaMenuTemplates.SetGestureText(item, shortcut);
            }
            RefreshGestures(item, hotkeys);
        }
    }

    private static Control ToElement(MainMenuRow row, MainWindowCommands commands, HotkeySettings hotkeys)
    {
        if (row.IsSeparator) return new Separator();
        var id = row.CommandId ?? throw new InvalidOperationException($"Main menu row '{row.Header}' has no command id.");
        var toggle = row.IsCheckable ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;
        var shortcut = HotkeyCatalog.DisplayAll(hotkeys, id);
        var item = new MenuItem { Header = row.Header, Tag = id, ToggleType = toggle, IsEnabled = commands.Handles(id), InputGesture = GestureOfText(shortcut) };
        if (!item.IsEnabled) { ToolTip.SetTip(item, Views.ToolWindowParts.NotAvailableTip); ToolTip.SetShowOnDisabled(item, true); }
        AvaloniaMenuTemplates.SetGestureText(item, shortcut);
        item.Click += (_, _) => commands.Run(id);
        return item;
    }

    /// <summary>The preview dialogs no row names, in one submenu: they are Avalonia's own entries, so they stay out of the Tools popup.</summary>
    private static MenuItem PreviewsMenu(IEnumerable<PreviewDialogEntry> entries)
    {
        var menu = new MenuItem { Header = "Preview dialogs" };
        foreach (var entry in entries)
        {
            var item = new MenuItem { Header = entry.Header };
            item.Click += (_, _) => entry.Open();
            menu.Items.Add(item);
        }
        return menu;
    }
}
