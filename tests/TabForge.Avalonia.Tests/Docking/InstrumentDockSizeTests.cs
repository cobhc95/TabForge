using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests.Docking;

/// <summary>Owns: the instrument pane size memory across dock moves: top/bottom keeps height, left/right keeps width,
/// orientation changes reuse the remembered share, and hide/show, restart and floating keep it. Lock wins.</summary>
public class InstrumentDockSizeTests
{
    private static double ShareOf(DockWorkspaceState state)
    {
        var found = DockLayoutTree.FindInstrumentShare(state);
        Assert.NotNull(found);
        return found!.Value.Share;
    }

    private static DockWorkspaceState WithVerticalShare(double share)
    {
        var state = DockLayoutTree.CreateDefaultState();
        var found = DockLayoutTree.FindInstrumentShare(state);
        Assert.NotNull(found);
        var (node, inFirst, _, _) = found!.Value;
        node.Ratio = InstrumentDockSizeMemory.RatioForShare(share, inFirst);
        state.InstrumentVerticalShare = share;
        return state;
    }

    [Fact]
    public void TopToBottomKeepsHeight()
    {
        var state = WithVerticalShare(160.0 / 600.0);
        var moved = DockLayoutTree.WithInstrumentSide(state, bottom: true);
        Assert.Equal(ShareOf(state), ShareOf(moved), 6);
        Assert.Equal(160.0, InstrumentDockSizeMemory.PixelsForShare(ShareOf(moved), 600.0), 3);
    }

    [Fact]
    public void BottomToTopKeepsHeight()
    {
        var top = WithVerticalShare(0.26);
        var bottom = DockLayoutTree.WithInstrumentSide(top, bottom: true);
        var back = DockLayoutTree.WithInstrumentSide(bottom, bottom: false);
        Assert.Equal(ShareOf(top), ShareOf(back), 6);
    }

    [Fact]
    public void DraggedHeightSurvivesTheMove()
    {
        var state = WithVerticalShare(0.26);
        var found = DockLayoutTree.FindInstrumentShare(state)!;
        var dragged = 0.40;
        found.Value.Node.Ratio = InstrumentDockSizeMemory.RatioForShare(dragged, found.Value.InstrumentFirst);
        var moved = DockLayoutTree.WithInstrumentSide(state, bottom: true);
        Assert.Equal(dragged, ShareOf(moved), 6);
        Assert.NotNull(moved.InstrumentVerticalShare);
        Assert.Equal(dragged, moved.InstrumentVerticalShare!.Value, 6);
    }

    [Fact]
    public void LeftToRightKeepsWidth()
    {
        var state = DockLayoutTree.CreateDefaultState();
        var emptied = new List<string>();
        Assert.True(DockWorkspaceRules.DropPanel(state, "instrument", "score-editor", DockDropZone.Left, null, 0.30, null, emptied));
        var leftShare = DockLayoutTree.FindInstrumentShare(state)!.Value.Share;
        Assert.True(DockWorkspaceRules.DropPanel(state, "instrument", "score-editor", DockDropZone.Right, null, 0.28, null, emptied));
        var rightShare = DockLayoutTree.FindInstrumentShare(state)!.Value.Share;
        Assert.Equal(leftShare, rightShare, 6);
    }

    [Fact]
    public void OrientationChangeUsesTheRememberedSize()
    {
        var state = WithVerticalShare(0.30);
        var emptied = new List<string>();
        Assert.True(DockWorkspaceRules.DropPanel(state, "instrument", "score-editor", DockDropZone.Left, null, 0.28, null, emptied));
        var horizontalFirst = state.InstrumentHorizontalShare;
        Assert.NotNull(horizontalFirst);
        var hFound = DockLayoutTree.FindInstrumentShare(state)!;
        hFound.Value.Node.Ratio = InstrumentDockSizeMemory.RatioForShare(0.40, hFound.Value.InstrumentFirst);
        DockLayoutTree.RememberInstrumentShare(state);
        Assert.NotNull(state.InstrumentHorizontalShare);
        Assert.Equal(0.40, state.InstrumentHorizontalShare!.Value, 6);
        Assert.True(DockWorkspaceRules.DropPanel(state, "instrument", "score-editor", DockDropZone.Top, null, 0.28, null, emptied));
        Assert.Equal(0.30, ShareOf(state), 6);
        Assert.True(DockWorkspaceRules.DropPanel(state, "instrument", "score-editor", DockDropZone.Left, null, 0.28, null, emptied));
        Assert.Equal(0.40, ShareOf(state), 6);
    }

    [Fact]
    public void RestartRoundTripKeepsMemory()
    {
        var state = WithVerticalShare(0.33);
        state.InstrumentHorizontalShare = 0.37;
        var captured = DockWorkspaceRules.Capture(state);
        var restored = DockLayoutTree.Clone(captured);
        Assert.NotNull(restored.InstrumentVerticalShare);
        Assert.NotNull(restored.InstrumentHorizontalShare);
        Assert.Equal(0.33, restored.InstrumentVerticalShare!.Value, 6);
        Assert.Equal(0.37, restored.InstrumentHorizontalShare!.Value, 6);
        Assert.Equal(ShareOf(state), ShareOf(restored), 6);
    }

    [Fact]
    public void HideAndShowKeepsHeight()
    {
        var state = WithVerticalShare(0.34);
        DockWorkspaceRules.Close(state, "instrument");
        Assert.DoesNotContain("instrument", DockWorkspaceRules.PanelsOf(state));
        DockWorkspaceRules.PlaceAtDefault(state, "instrument", "instrument", "score-editor");
        Assert.Equal(0.34, ShareOf(state), 6);
    }

    [Fact]
    public void FloatingAndBackKeepsHeight()
    {
        var state = WithVerticalShare(0.32);
        FloatingSessionRules.OpenFloating(state, "instrument", "float-1", new TabForge.Platform.UiRect(0, 0, 360, 260), out _);
        Assert.Single(state.Floating);
        Assert.DoesNotContain("instrument", DockLayoutTree.EnumeratePanels(state.Root));
        FloatingSessionRules.CloseByUser(state, state.Floating.Single().Id, _ => ("instrument", "score-editor"));
        Assert.Empty(state.Floating);
        Assert.Equal(0.32, ShareOf(state), 6);
    }

    [Fact]
    public void LockWinsOverCurrentAndRemembered()
    {
        Assert.Equal(120.0, InstrumentDockSizeMemory.HeightToApply(160, 150, 150, locked: true, lockedHeight: 120));
        Assert.Equal(160.0, InstrumentDockSizeMemory.HeightToApply(160, 150, 150, locked: false, lockedHeight: 120));
        Assert.Equal(150.0, InstrumentDockSizeMemory.HeightToApply(null, 150, 182, locked: false, lockedHeight: null));
        Assert.Equal(360.0, InstrumentDockSizeMemory.WidthToApply(null, null, 360, locked: false, lockedWidth: null));
    }

    [Fact]
    public void NeverResetsToDefaultAfterFirstUse()
    {
        Assert.Equal(0.33, InstrumentDockSizeMemory.ShareToApply(null, 0.33));
        Assert.Equal(0.33, InstrumentDockSizeMemory.RememberShare(null, 0.33));
        Assert.Null(InstrumentDockSizeMemory.RememberShare(null, null));
    }
}
