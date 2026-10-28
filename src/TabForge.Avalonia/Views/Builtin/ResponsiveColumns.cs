using System;
using Avalonia;
using Avalonia.Controls;

namespace TabForge.Avalonia.Views.Fx;

// Owns: a panel that lays its children out in equal columns, as many as fit the width at MinColumnWidth (at most MaxColumns, and a count that divides the children evenly:
//   4 bands go 4, 2, then 1 per row), each row as tall as its tallest child. Used by the built-in effect editors so control groups wrap instead of being cut off.
// Does not own: the editors that use it (EqEditor, CompressorEditor), spacing (the children carry their own margins).
// Tests: BuiltinEditorResponsiveTests.
public sealed class ResponsiveColumns : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty = AvaloniaProperty.Register<ResponsiveColumns, double>(nameof(MinColumnWidth), 200);

    /// <summary>The narrowest a column may get before the panel drops to fewer columns.</summary>
    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    static ResponsiveColumns() => AffectsMeasure<ResponsiveColumns>(MinColumnWidthProperty);

    private int ColumnsFor(double width)
    {
        var count = Math.Max(1, Children.Count);
        var fit = double.IsInfinity(width) ? count : Math.Clamp((int)(width / Math.Max(1, MinColumnWidth)), 1, count);
        while (fit > 1 && count % fit != 0) fit--;
        return fit;
    }

    protected override Size MeasureOverride(Size available)
    {
        var columns = ColumnsFor(available.Width);
        var cell = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / columns;
        double width = 0, height = 0, rowHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(cell, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            width = Math.Max(width, Children[i].DesiredSize.Width * columns);
            if (i % columns == columns - 1 || i == Children.Count - 1) { height += rowHeight; rowHeight = 0; }
        }
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var cell = finalSize.Width / columns;
        double y = 0, rowHeight = 0;
        for (var start = 0; start < Children.Count; start += columns)
        {
            rowHeight = 0;
            for (var i = start; i < Math.Min(Children.Count, start + columns); i++) rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            for (var i = start; i < Math.Min(Children.Count, start + columns); i++) Children[i].Arrange(new Rect((i - start) * cell, y, cell, rowHeight));
            y += rowHeight;
        }
        return finalSize;
    }
}
