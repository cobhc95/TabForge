using TabForge.Platform;
using TabForge.Shell;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the pure tab drag rules on fake rectangles: the tear-off band, the drop window found under a point (topmost first, the dragged
/// window skipped), the insertion slot among tab centres and the neighbour slide offsets of an in-strip reorder.
/// Tests: TabDragRules (src/TabForge.Core/Shell/TabDragRules.cs). No window is opened.
/// </summary>
public class TabDragRulesTests
{
    private static readonly UiRect Left = new(0, 0, 500, 36);
    private static readonly UiRect Right = new(600, 0, 500, 36);

    [Theory]
    [InlineData(100, 58, false)]    // the band edge (36 px strip + 22 px tolerance) is still inside
    [InlineData(100, 59, true)]     // one pixel past the band below the strip
    [InlineData(100, -22, false)]   // the band edge above the strip
    [InlineData(100, -23, true)]
    [InlineData(-1, 10, true)]      // left of the window
    [InlineData(800, 10, true)]     // right edge of an 800 px window is outside
    [InlineData(799, 10, false)]
    public void TabDragTearOffBandFollowsTheStripAndTheWindowEdges(double x, double y, bool beyond) =>
        Assert.Equal(beyond, TabDragRules.IsBeyondTearOff(x, y, 800, 0, 36));

    [Theory]
    [InlineData(100, 10, 0)]    // inside the left window's strip
    [InlineData(700, 10, 1)]    // inside the right window's strip, empty space included
    [InlineData(520, 10, -1)]   // between the two windows
    [InlineData(100, 40, -1)]   // below the strip
    [InlineData(100, -1, -1)]   // above the strip
    public void TabDragDropTargetIsTheWindowWhoseStripContainsThePoint(double x, double y, int expected) =>
        Assert.Equal(expected, TabDragRules.ResolveDropTarget(x, y, new[] { Left, Right }));

    [Fact]
    public void TabDragDropTargetPrefersTheTopmostWindowAndSkipsTheDraggedOne()
    {
        var wide = new UiRect(0, 0, 800, 36);
        var rects = new[] { Left, wide };                       // Left is topmost and overlaps the wider strip
        Assert.Equal(0, TabDragRules.ResolveDropTarget(100, 10, rects));
        Assert.Equal(1, TabDragRules.ResolveDropTarget(100, 10, rects, excludedIndex: 0));
        Assert.Equal(-1, TabDragRules.ResolveDropTarget(100, 10, new[] { Left }, excludedIndex: 0));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(60, 1)]         // past the first centre (50)
    [InlineData(160, 2)]        // past the second centre (150)
    [InlineData(260, 3)]        // past the last centre (250): slot after the last tab
    [InlineData(-5, 0)]
    public void TabDragInsertIndexComparesTheTabCentres(double x, int expected)
    {
        var tabs = new[] { new UiRect(0, 0, 100, 36), new UiRect(100, 0, 100, 36), new UiRect(200, 0, 100, 36) };
        Assert.Equal(expected, TabDragRules.InsertIndex(x, tabs));
    }

    [Fact]
    public void TabDragInsertIndexSkipsTheDraggedTab()
    {
        var tabs = new[] { new UiRect(0, 0, 100, 36), new UiRect(100, 0, 100, 36), new UiRect(200, 0, 100, 36) };
        // The dragged tab 0 is skipped: x = 10 lies before tab 1's centre, so the slot is 1 (just after the dragged tab).
        Assert.Equal(1, TabDragRules.InsertIndex(10, tabs, excludedIndex: 0));
        Assert.Equal(0, TabDragRules.ReorderDestination(0, 1, 3));
    }

    [Theory]
    [InlineData(0, 2, new[] { 0.0, -103.0, 0.0 })]        // dragged right past the middle tab: the middle tab slides left
    [InlineData(0, 3, new[] { 0.0, -103.0, -103.0 })]     // to the end: both others slide left
    [InlineData(2, 0, new[] { 83.0, 83.0, 0.0 })]         // dragged left to the start: both others slide right by its width + spacing
    [InlineData(1, 1, new[] { 0.0, 0.0, 0.0 })]           // dropped where it was: nothing slides
    [InlineData(1, 2, new[] { 0.0, 0.0, 0.0 })]           // the slot right after itself is also no move
    public void TabDragReorderSlideOffsetsOpenTheGapForTheDraggedTab(int fromIndex, int insertionIndex, double[] expected)
    {
        var widths = new double[] { 100, 50, 80 };
        Assert.Equal(expected, TabDragRules.ReorderSlideOffsets(fromIndex, insertionIndex, widths, spacing: 3));
    }
}
