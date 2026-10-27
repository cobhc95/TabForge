using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Menus;

namespace TabForge.Avalonia;

// Owns: the document tab's right-click menu on the window's strip: the tab under the pointer (the rows act on that tab, not on the shown
//   one), and the commands those rows run (Duplicate, Close tab, Close others, Close tabs to the right, Move to new window).
// Does not own: the rows (DocumentTabMenus in Core), the menu items (ContextMenuBuilder), the tab list and its save questions (MainWindowTabs),
//   the window tear-off (DocumentTabDragFlow.TearOff).
// Tests: TabForge.Avalonia.Tests.DocumentTabMenuTests.
/// <summary>The tab right-click menu of one window. UI thread only.</summary>
public static class MainWindowTabMenu
{
    /// <summary>The command ids this class handles; the tab's index is the row's argument.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        DocumentTabMenus.Duplicate, DocumentTabMenus.Close, DocumentTabMenus.CloseOthers, DocumentTabMenus.CloseToTheRight, DocumentTabMenus.MoveToNewWindow,
    };

    /// <summary>Opens the tab menu on a right-click over a tab of the window's strip.</summary>
    public static void Attach(MainWindow window, ContextMenuHost host) =>
        ContextMenuBuilder.Attach(window.TabStrip, host, at => RowsAt(window, at));

    /// <summary>Adds the menu's command handlers; <paramref name="report"/> shows a failed close.</summary>
    public static void Register(MainWindowCommands commands, MainWindow window, Action<string> report)
    {
        var songs = window.Songs;
        commands.AddWithArgument(DocumentTabMenus.Duplicate, arg => songs.Duplicate(IndexOf(arg)));
        commands.AddWithArgument(DocumentTabMenus.Close, arg =>
        {
            songs.Switch(IndexOf(arg));
            Platform.UiTasks.Forget(songs.CloseActive(), "Closing the tab", report);
        });
        commands.AddWithArgument(DocumentTabMenus.CloseOthers, arg => Platform.UiTasks.Forget(CloseOthersAsync(songs, IndexOf(arg)), "Closing other tabs", report));
        commands.AddWithArgument(DocumentTabMenus.CloseToTheRight, arg =>
            Platform.UiTasks.Forget(songs.CloseMany(songs.Songs.Skip(IndexOf(arg) + 1).ToList()), "Closing tabs to the right", report));
        commands.AddWithArgument(DocumentTabMenus.MoveToNewWindow, arg => MoveToNewWindow(window, IndexOf(arg)));
    }

    // The rows of the tab under the pointer (the strip is the menu's target, so the point is in its coordinates); none off a tab.
    private static IEnumerable<MenuSpec> RowsAt(MainWindow window, Point at)
    {
        var strip = window.TabStrip;
        for (var index = 0; index < strip.Tabs.Count; index++)
        {
            var tab = strip.Tabs[index];
            var origin = tab.TranslatePoint(default, strip) ?? default;
            if (new Rect(origin, tab.Bounds.Size).Contains(at))
                return DocumentTabMenus.Build(index, window.Songs.Songs.Count, ContextMenuRows.Shortcuts());
        }
        return Array.Empty<MenuSpec>();
    }

    private static int IndexOf(string argument) => int.Parse(argument, CultureInfo.InvariantCulture);

    // The kept tab is found by reference, since the others' closes move the indexes; it is shown afterwards.
    private static async Task CloseOthersAsync(MainWindowTabs songs, int index)
    {
        var kept = songs.Songs[index];
        await songs.CloseMany(songs.Songs.Where(song => !ReferenceEquals(song, kept)).ToList());
        songs.Switch(songs.Songs.ToList().IndexOf(kept));
    }

    // The tab leaves for a window placed where the tab sits on the strip; the single tab's row is disabled, so the window stays.
    private static void MoveToNewWindow(MainWindow window, int index)
    {
        var strip = window.TabStrip;
        var origin = strip.Tabs[index].TranslatePoint(default, strip) ?? default;
        window.TabDrag.TearOff(index, strip.PointToScreen(origin));
    }
}
