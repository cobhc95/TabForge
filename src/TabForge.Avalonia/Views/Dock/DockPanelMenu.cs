using TabForge.Avalonia.Platform;
using System;
using Avalonia.Controls;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the right-click menu of a dock tab: its items come from DockPanePresenter.MenuEntries.
// Does not own: what the actions do (the callbacks, from DockWorkspaceController) or when the menu shows (DockTabSurface).
// Tests: TabForge.Avalonia.Tests.DockWorkspaceControllerTests.
/// <summary>Builds the panel context menu: reset this panel, close it, float it, then reset all.</summary>
public static class DockPanelMenu
{
    public const string FloatLabel = "Float panel";

    public static ContextMenu Build(string panelId, Action<string> resetPanel, Action<string> closePanel, Action resetAll, Action<string> floatPanel)
    {
        var menu = MenuStability.CreateContextMenu();
        foreach (var entry in DockPanePresenter.MenuEntries)
        {
            if (entry.Action == DockPaneMenuAction.Separator)
            {
                // The tear-off of the dock is a drag; here it is this menu item, before the separator.
                var floatItem = new MenuItem { Header = FloatLabel };
                floatItem.Click += (_, _) => floatPanel(panelId);
                menu.Items.Add(floatItem);
            }
            if (entry.Action == DockPaneMenuAction.Separator) { menu.Items.Add(new Separator()); continue; }
            var item = new MenuItem { Header = entry.Label };
            item.Click += (_, _) =>
            {
                if (entry.Action == DockPaneMenuAction.ResetPane) resetPanel(panelId);
                else if (entry.Action == DockPaneMenuAction.ClosePane) closePanel(panelId);
                else resetAll();
            };
            menu.Items.Add(item);
        }
        return menu;
    }
}
