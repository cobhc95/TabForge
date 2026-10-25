using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia.Shell;
using TabForge.Platform;
using TabForge.Shell;

namespace TabForge.Avalonia.Views;

// Owns: the strip geometry the drag flow asks for: tab screen rectangles, the insertion slot at a screen x, the strip's own screen hit, and
//   the reorder slide/displacement computed from the laid-out tab widths and TabSpacing. TabStripMotion only applies the resulting offsets.
// Does not own: the visual transforms (TabStripMotion), the drop rules (TabDragRules) or the drag state (DocumentTabDragFlow).
// Tests: TabForge.Avalonia.Tests.TabStripDragVisualTests, TabForge.Avalonia.Tests.TabTearOffTests.
public sealed partial class TabStripView
{
    /// <summary>The insertion slot at a screen x among this strip's tabs, skipping the dragged tab.</summary>
    public int InsertionSlot(double screenX, int excludedIndex = -1) =>
        TabDragRules.InsertIndex(screenX, ScreenTabRects(), excludedIndex);

    /// <summary>True when the screen point lies over this strip's full rectangle (empty space included).</summary>
    public bool ContainsScreenPoint(PixelPoint screen) =>
        TabDragRules.ResolveDropTarget(screen.X, screen.Y, new[] { TabWindowRegistry.ScreenRectOf(this) }) == 0;

    /// <summary>Moves the neighbours by the offsets of a reorder from <paramref name="fromIndex"/> into <paramref name="slot"/>.</summary>
    public void SlideForReorder(int fromIndex, int slot, bool animate) =>
        _motion.Slide(TabDragRules.ReorderSlideOffsets(fromIndex, slot, TabWidths(), TabSpacing), animate, _draggedTab);

    /// <summary>How far the dragged tab's left edge moves when reordered from <paramref name="fromIndex"/> to <paramref name="destination"/>.</summary>
    public double ReorderDisplacement(int fromIndex, int destination) =>
        TabDragRules.ReorderDisplacement(fromIndex, destination, TabWidths(), TabSpacing);

    private List<double> TabWidths() => _tabs.Children.Select(tab => tab.Bounds.Width).ToList();

    private List<UiRect> ScreenTabRects() => _tabs.Children.Select(TabWindowRegistry.ScreenRectOf).ToList();
}
