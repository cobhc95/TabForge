using System.Windows;
using System.Windows.Controls;

namespace TabForge.Views;

// Owns: the track list toolbar's panel: one row (plain DockPanel layout) while the controls column is wide enough, two rows
//   when it is narrower (row 1: the left-docked children, the live input button and the transport capsule; row 2: the
//   right-docked children in their visual order, left-aligned), and the widths each form needs.
// Does not own: the buttons (ArrangementPanel.BuildControlsHeader), the column width and its minimum, the header row's height
//   and the timeline's matching offset (ArrangementPanel.TrackColumns.cs), compact mode's own layout (CompactColumnLayout).
// Tests: TestToolbarWrap, TestCompactMode.
internal sealed class ToolbarWrapPanel : DockPanel
{
    /// <summary>The narrowest the track name column gets at the controls column's minimum width.</summary>
    public const double MinNameColumn = 60;
    private const double RowGap = 2;
    private static readonly Size Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>Two rows are shown.</summary>
    public bool Wrapped { get; private set; }

    /// <summary>Width (with the panel's margin) the left group, the right group need, and the right group's height.</summary>
    private (double left, double right, double rightHeight) Groups()
    {
        double left = 0, right = 0, height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(Unbounded);
            if (GetDock(child) == Dock.Right) { right += child.DesiredSize.Width; height = Math.Max(height, child.DesiredSize.Height); }
            else left += child.DesiredSize.Width;
        }
        return (left, right, height);
    }

    /// <summary>Column width at which every button fits in one row.</summary>
    public double OneRowWidth { get { var (left, right, _) = Groups(); return left + right + Margin.Left + Margin.Right; } }
    /// <summary>Column width at which every button fits in two rows.</summary>
    public double TwoRowWidth { get { var (left, right, _) = Groups(); return Math.Max(left, right) + Margin.Left + Margin.Right; } }
    /// <summary>Height the second row adds to the header (0 in one row).</summary>
    public double ExtraHeight => Wrapped ? Groups().rightHeight + RowGap : 0;

    /// <summary>Picks one or two rows from the column's width alone; true when the form changed.</summary>
    public bool SetColumnWidth(double width)
    {
        var wrapped = width < OneRowWidth;
        if (wrapped == Wrapped) return false;
        Wrapped = wrapped;
        InvalidateMeasure();
        return true;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!Wrapped) return base.MeasureOverride(constraint);
        var (left, right, rightHeight) = Groups();
        return new Size(Math.Max(left, right), LeftHeight() + RowGap + rightHeight);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (!Wrapped) return base.ArrangeOverride(arrangeSize);
        double top = LeftHeight(), x = 0, rightHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (GetDock(child) == Dock.Right) { rightHeight = Math.Max(rightHeight, child.DesiredSize.Height); continue; }
            child.Arrange(new Rect(x, (top - child.DesiredSize.Height) / 2, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width;
        }
        x = 0;
        // The right-docked children are added right-most first: the last one is the left-most.
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var child = InternalChildren[i];
            if (GetDock(child) != Dock.Right) continue;
            child.Arrange(new Rect(x, top + RowGap + (rightHeight - child.DesiredSize.Height) / 2, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width;
        }
        return arrangeSize;
    }

    private double LeftHeight()
    {
        double height = 0;
        foreach (UIElement child in InternalChildren)
            if (GetDock(child) != Dock.Right) height = Math.Max(height, child.DesiredSize.Height);
        return height;
    }
}
