using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the dock layout snapshot that View > Compact mode takes on enter and restores on exit, so the panels keep their places and sizes.
/// Does not own: the compact mode command (MainWindowCompactMode), the layout maths (DockLayoutTree in Core).
/// Tests: DockWorkspaceController.SnapshotLayout, DockWorkspaceController.ApplyLayout.
/// </summary>
public class DockLayoutSnapshotTests
{
    private static DockWorkspaceController Open()
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        return new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup);
    }

    private static string Json(DockWorkspaceState state) => JsonSerializer.Serialize(state);

    [AvaloniaFact]
    public void HidingSidePanelsAndFittingThenRestoringTheSnapshotLeavesTheLayoutEqual()
    {
        var dock = Open();
        var before = Json(dock.State);
        var snapshot = dock.SnapshotLayout();
        foreach (var id in DockPaneTable.SideIds.Where(dock.IsPanelVisible).ToList()) dock.SetPanelVisible(id, false);
        dock.FitPanelHeight("timeline", 112);
        dock.ApplyLayout(snapshot);
        Assert.Equal(before, Json(dock.State));
    }
}
