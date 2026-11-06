using System.Linq;
using System.Text.Json;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the one side column every layout shares: showing the side panels in the Band layout gives them the default layout's arrangement and
///   width (palettes over Sections and Lyrics over View and tempo, beside the panes above the timeline), and the default layout with its side
///   panels closed and shown again is the default layout.
/// Does not own: the toolbar toggle's exact restore (MainWindowViewMenu.ToggleSidePanel) or the dock drawing (DockWorkspaceView).
/// Tests: this class.
/// </summary>
public class DockSideColumnTests
{
    private static string Shape(DockNodeState? node) => node is null ? "-" : node.Kind switch
    {
        "split" => $"{node.Orientation}({node.Ratio:0.##}:{Shape(node.First)},{Shape(node.Second)})",
        "tabs" => node.HostId + "[" + string.Join(" ", node.Panels) + "]",
        _ => node.HostId,
    };

    [Fact]
    public void TheBandLayoutsSidePanelsComeBackInTheDefaultColumn()
    {
        var band = DockLayoutCatalog.BuiltInLayout(DockLayoutCatalog.BandName);

        Assert.True(DockWorkspaceRules.ShowSideColumn(band));

        var column = Shape(DockLayoutTree.SideColumn());
        Assert.Equal($"Vertical(0.8:Horizontal({DockLayoutTree.SideColumnRatio:0.##}:default-band[band],{column}),default-timeline[timeline])", Shape(band.Root));
        Assert.DoesNotContain(band.ClosedPanels, id => DockPaneTable.SideIds.Contains(id));
    }

    [Fact]
    public void TheDefaultLayoutWithItsSidePanelsClosedAndShownAgainIsTheDefault()
    {
        var expected = DockLayoutCatalog.InstrumentTopLayout();
        var state = DockLayoutCatalog.InstrumentTopLayout();
        foreach (var id in DockPaneTable.SideIds) DockWorkspaceRules.Close(state, id);

        Assert.True(DockWorkspaceRules.ShowSideColumn(state));

        Assert.Equal(Shape(expected.Root), Shape(state.Root));
        Assert.Equal(expected.ClosedPanels.OrderBy(id => id), state.ClosedPanels.OrderBy(id => id));
    }

    [Fact]
    public void AShownSidePanelLeavesTheLayoutAlone()
    {
        var state = DockLayoutCatalog.InstrumentTopLayout();
        foreach (var id in DockPaneTable.SideIds.Skip(1)) DockWorkspaceRules.Close(state, id);
        var before = JsonSerializer.Serialize(state);

        Assert.False(DockWorkspaceRules.ShowSideColumn(state));
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }
}
