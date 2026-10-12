using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace TabForge.Views.Fx;

/// <summary>A row of the list as a drag sees it: where it is, how deep it sits, whether it is a container that may take the dragged rows, whether it is dragged.</summary>
internal readonly record struct DropRow(double Top, double Height, int Depth, bool Container, bool Moving);

/// <summary>Where a drop goes: in front of row <see cref="Insert"/> (the row count: the end), into the container row <see cref="Into"/> (-1: none),
/// with the insertion line in front of row <see cref="Insert"/> at the indent of <see cref="LineDepth"/> (-1: no line).</summary>
internal readonly record struct DropTarget(int Insert, int Into, int LineDepth);

// Owns: where a drag in the FX chain list would drop for a pointer position (the zones of a container row, the end of an open
//   container chosen by the pointer's horizontal position), and the two marks of it: the insertion line, indented for a position
//   inside a container and full width outside, and the "Drop inside" badge on the container row.
// Does not own: the drag itself and the rows' look (FxChainList), what a drop does to the chain (FxChainContainers).
// Tests: TestFxChainContainerRows.
internal sealed class FxChainDropTarget
{
    public const double Indent = 16;
    private readonly Rectangle _line = new() { Height = 2, RadiusX = 1, RadiusY = 1, Visibility = Visibility.Collapsed };
    private readonly TextBlock _text = new() { FontSize = Services.ThemeService.MinFontSize, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _badge;

    public FxChainDropTarget(Canvas layer)
    {
        _line.SetResourceReference(Shape.FillProperty, "AccentBrush");
        _badge = new Border { Child = _text, Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
        _badge.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        _badge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextStrongBrush");
        layer.Children.Add(_line);
        layer.Children.Add(_badge);
    }

    /// <summary>The badge's text while it shows ("" otherwise).</summary>
    public string BadgeText => _badge.Visibility == Visibility.Visible ? _text.Text : "";
    /// <summary>The left edge of the insertion line while it shows (-1 otherwise).</summary>
    public double LineLeft => _line.Visibility == Visibility.Visible ? Canvas.GetLeft(_line) : -1;

    /// <summary>The drop for a pointer at (<paramref name="x"/>, <paramref name="y"/>), in the coordinates of the rows' tops.
    /// The middle 60% of a container row drops into it; its top and bottom 20% drop in front of or behind it. Where an open container
    /// ends, a pointer right of its plug-ins' indent drops inside at the end, a pointer left of it behind the container.</summary>
    public static DropTarget At(IReadOnlyList<DropRow> rows, double x, double y)
    {
        var (previous, insert) = (-1, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Moving) continue;
            if (r.Container && y > r.Top + r.Height * 0.2 && y < r.Top + r.Height * 0.8) return new DropTarget(i, i, -1);
            if (y < r.Top + r.Height / 2) { insert = i; break; }
            previous = i;
        }
        var depth = insert < rows.Count ? rows[insert].Depth : 0;
        // ponytail: where several nested containers end together the choice is the innermost or the row below; one level per indent step if that is ever needed.
        if (previous >= 0 && rows[previous].Depth > depth && x >= rows[previous].Depth * Indent + 8)
        {
            var inner = rows[previous].Depth;
            var parent = previous;
            while (parent >= 0 && rows[parent].Depth >= inner) parent--;
            if (parent >= 0 && rows[parent].Container) return new DropTarget(insert, parent, inner);
        }
        return new DropTarget(insert, -1, depth);
    }

    /// <summary>Shows the insertion line at a height of the list (a negative depth hides it): from the indent of that depth to the right edge.</summary>
    public void Line(double y, int depth, double width)
    {
        _line.Visibility = depth < 0 ? Visibility.Collapsed : Visibility.Visible;
        if (depth < 0) return;
        var left = 4 + depth * Indent;
        Canvas.SetLeft(_line, left); Canvas.SetTop(_line, y - 1);
        _line.Width = Math.Max(8, width - left - 22);
    }

    /// <summary>Shows "Drop inside" right-aligned in a container row, never wider than the room behind the row's name, which ends at
    /// <paramref name="nameRight"/> (a negative value hides it).</summary>
    public void Badge(double nameRight, double rowTop, double rowHeight, double width)
    {
        var room = width - 24 - nameRight - 6;
        _badge.Visibility = nameRight < 0 || room < 24 ? Visibility.Collapsed : Visibility.Visible;
        if (_badge.Visibility != Visibility.Visible) return;
        _text.Text = "Drop inside";
        _badge.MaxWidth = room;
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_badge, width - _badge.DesiredSize.Width - 24);
        Canvas.SetTop(_badge, rowTop + Math.Max(0, (rowHeight - _badge.DesiredSize.Height) / 2));
    }
}
