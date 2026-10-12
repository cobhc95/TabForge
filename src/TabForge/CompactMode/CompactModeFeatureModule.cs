using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Views;

namespace TabForge.CompactMode;

// Owns: what compact mode adds to the central tables: the View.CompactMode hotkey row, command and View menu row, and the checked state of that row.
// Does not own: the state and the pane height (CompactModeController), the rows (ArrangementPanel.CompactMode.cs), the button's click (CompactModeButton), the header strip (CompactModeStrip).
// Tests: TestCompactMode, TestFeatureModuleContributions, TestMainMenuTreeGolden.
public sealed class CompactModeFeatureModule : IFeatureModule
{
    public const string CommandId = "View.CompactMode";

    public string Name => "Compact mode";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new(CommandId, HotkeyCatalog.CategoryView, "Compact mode", "Ctrl+Alt+C",
            "Hide every track row and show the master track lane at twice its height, so the score and the other panes get the room; again brings the tracks back (same as View > Compact mode)."), "View.LearnPopout"),
    };

    public IReadOnlyList<FeatureMenuRow> MenuRows { get; } = new FeatureMenuRow[]
    {
        new("_View", "View.LearnPopout", "Compact mode", CommandId),
    };

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        CompactModeController? controller = null;   // one per window: the command table is built once per host
        yield return new(CommandId, () => (controller ??= Create(host)).Toggle());
    }

    private static CompactModeController Create(IFeatureHost host)
    {
        var fit = host as ITrackListFitHost ?? throw new InvalidOperationException("The host does not implement ITrackListFitHost.");
        var window = host as Window;
        var controller = new CompactModeController((ICompactModeRows)fit.Arrangement, () => fit.Dock,
            () => fit.Dock is DockWorkspace dock && TabForge.Docking.DockPaneTable.SideIds.Any(dock.IsPanelVisible),
            () => (window?.FindName("SidePanelButton") as Button)?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));   // the side panel button's own path
        controller.Changed += () =>
        {
            if (window?.FindName("MainMenu") is Menu menu && MenuHotkey.Find(menu.Items, CommandId) is { } item) { item.IsCheckable = true; item.IsChecked = controller.IsOn; }   // the toolbar button follows the row
        };
        return controller;
    }
}
