using System.Linq;
using TabForge.Docking;
using TabForge.KeyboardMode;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Keyboard mode layout swap hiding the instrument pane when it floats (the pop-out case).
// Does not own: the docked case (TestKeyboardModeLayout in the full suite), the dock restore or the panes.
// Tests: KeyboardModeLayoutSwap.Build.
public sealed class KeyboardModeLayoutSwapTests
{
    [Theory]
    [InlineData(KeyboardModeSize.Full)]
    [InlineData(KeyboardModeSize.Normal)]
    [InlineData(KeyboardModeSize.Large)]
    public void BuildHidesAFloatingInstrumentPane(KeyboardModeSize size)
    {
        var start = DockWorkspaceController_DefaultLayout();
        FloatingSessionRules.OpenFloating(start, "instrument", "float-instrument", new UiRect(200, 160, 400, 300), out _);
        var startJson = System.Text.Json.JsonSerializer.Serialize(start);

        var swapped = KeyboardModeLayoutSwap.Build(start, size);

        Assert.DoesNotContain("instrument", DockLayoutTree.EnumerateAllPanels(swapped));
        Assert.Contains("instrument", swapped.ClosedPanels);
        Assert.Contains("learn", DockLayoutTree.EnumerateAllPanels(swapped));
        Assert.DoesNotContain("learn", swapped.ClosedPanels);
        Assert.True(DockLayoutTree.Validate(swapped, true, id => DockPaneTable.Ids.Contains(id)));
        Assert.Equal(startJson, System.Text.Json.JsonSerializer.Serialize(start));
    }

    private static DockWorkspaceState DockWorkspaceController_DefaultLayout() =>
        TabForge.Avalonia.Views.Docking.DockWorkspaceController.DefaultLayout();
}
