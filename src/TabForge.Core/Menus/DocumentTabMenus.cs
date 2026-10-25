using System.Collections.Generic;
using System.Globalization;

namespace TabForge.Menus;

// Owns: the rows of the document tab's right-click menu (New tab, Duplicate, Close tab, Close others, Close tabs to the right, Move to new
//   window), and the tab each row acts on: the row's Arg is that tab's index, so the rows act on the tab under the pointer, not the shown one.
//   Rows with nothing to act on are disabled (no other tab, no tab to the right, a lone tab cannot move to a new window).
// Does not own: opening the menu (Avalonia ContextMenuBuilder), the handlers (MainWindowTabMenu), the tabs (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.DocumentTabMenuTests.
/// <summary>The document tab's right-click menu as plain data. UI-free.</summary>
public static class DocumentTabMenus
{
    public const string NewTab = "Tab.New";
    public const string Duplicate = "TabMenu.Duplicate", Close = "TabMenu.Close", CloseOthers = "TabMenu.CloseOthers",
        CloseToTheRight = "TabMenu.CloseToTheRight", MoveToNewWindow = "TabMenu.MoveToNewWindow";

    /// <summary>The rows for the tab at <paramref name="index"/> of <paramref name="count"/> tabs; <paramref name="key"/> maps a hotkey id to its shortcut text.</summary>
    public static List<MenuSpec> Build(int index, int count, System.Func<string, string> key)
    {
        var arg = index.ToString(CultureInfo.InvariantCulture);
        return new List<MenuSpec>
        {
            Item(NewTab, "New tab", key(NewTab)),
            Item(Duplicate, "Duplicate", key("Tab.Duplicate"), arg),
            MenuSpec.Separator(),
            Item(Close, "Close tab", key("Tab.Close"), arg),
            Item(CloseOthers, "Close others", "", arg, count > 1),
            Item(CloseToTheRight, "Close tabs to the right", "", arg, index < count - 1),
            MenuSpec.Separator(),
            Item(MoveToNewWindow, "Move to new window", "", arg, count > 1),
        };
    }

    private static MenuSpec Item(string id, string header, string shortcut, string? arg = null, bool enabled = true) =>
        new() { Id = id, Header = header, Shortcut = shortcut, Arg = arg, Enabled = enabled };
}
