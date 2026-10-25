using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Docking;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the draggable bar between the two halves of a dock split: its size, cursor, theme colours and the drag in DIPs.
// Does not own: what a drag changes (DockSplitPanel turns it into a ratio).
// Tests: TabForge.Avalonia.Tests.DockWorkspaceViewTests.
/// <summary>The bar between the halves of a dock split. It reports the distance dragged since the press, along the split axis.</summary>
public sealed class DockSplitter : Border
{
    /// <summary>The grab area thickness in DIPs; the visible line inside it is thinner (<see cref="RestLine"/>, <see cref="ActiveLine"/>).</summary>
    public const double BarThickness = 5;

    /// <summary>The visible line thickness at rest, and while hovered or dragged.</summary>
    private const double RestLine = 1;
    private const double ActiveLine = 2;

    private const string RestBrush = "BorderBrush";

    private readonly Border _line = new() { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };

    private Point? _pressAt;

    public DockSplitter(bool horizontalSplit)
    {
        Name = "DockSplitter";
        HorizontalSplit = horizontalSplit;
        Cursor = new Cursor(horizontalSplit ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
        Background = Brushes.Transparent;   // the whole grab area takes the pointer; only the centred line is drawn
        Child = _line;
        Paint(RestBrush);
        PointerEntered += (_, _) => Paint("Accent");
        PointerExited += (_, _) => { if (_pressAt is null) Paint(RestBrush); };
    }

    public bool HorizontalSplit { get; }

    /// <summary>Raised on press.</summary>
    public event Action? DragStarted;

    /// <summary>Raised while dragging with the distance from the press along the split axis.</summary>
    public event Action<double>? DragMoved;

    /// <summary>Raised on release after a press.</summary>
    public event Action? DragEnded;

    private void Paint(string paletteKey)
    {
        var thickness = paletteKey == RestBrush ? RestLine : ActiveLine;
        if (HorizontalSplit) { _line.Width = thickness; _line.Height = double.NaN; _line.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch; }
        else { _line.Height = thickness; _line.Width = double.NaN; _line.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch; }
        _line[!Border.BackgroundProperty] = new DynamicResourceExtension(paletteKey);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = HorizontalSplit ? new Size(BarThickness, 0) : new Size(0, BarThickness);
        _line.Measure(size);
        return size;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressAt = e.GetPosition((Visual?)Parent);
        e.Pointer.Capture(this);
        DragStarted?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_pressAt is not { } start) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { EndDrag(); return; }
        var now = e.GetPosition((Visual?)Parent);
        DragMoved?.Invoke(HorizontalSplit ? now.X - start.X : now.Y - start.Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pressAt is null) return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_pressAt is null) return;
        _pressAt = null;
        Paint(RestBrush);
        DragEnded?.Invoke();
    }
}
