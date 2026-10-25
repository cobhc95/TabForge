using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace TabForge.Avalonia.Platform;

// Owns: a stand-alone mini slider that looks and drags like the track list volume and pan sliders: it is drawn by the same painter (TrackRowCells.DrawSlider)
//   and maps the pointer with the same rules (TrackRowCells.SliderValueAt, SliderGrabOffset); a double-click returns it to its declared default.
// Does not own: what the value means or where its default is declared (the caller), the track list hit testing (TimelineInputController).
// Tests: TabForge.Avalonia.Tests.LiveStripSliderTests.
// Thread: UI thread. A value that does not change repaints nothing; a repaint never changes the size.
/// <summary>One mini slider over whole numbers. <see cref="ValueChanged"/> is raised for the user changes (drag, double-click reset); <see cref="Value"/> moves it silently.</summary>
public sealed class MiniSlider : Control
{
    private readonly AvaloniaDrawContext _draw = new();
    private TrackRowCells.CellPaint _paint;
    private double _grab;
    private bool _dragging;
    private int _value;

    public MiniSlider(int minimum, int maximum, int defaultValue, double width, double height)
    {
        Minimum = minimum; Maximum = maximum; Default = defaultValue; _value = defaultValue;
        Width = width; Height = height;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public int Minimum { get; }
    public int Maximum { get; }
    /// <summary>The value a double-click returns to.</summary>
    public int Default { get; }

    /// <summary>Raised when the user changes the value.</summary>
    public event Action<int>? ValueChanged;

    /// <summary>Raised when a drag ends (the pointer is released).</summary>
    public event Action? Released;

    /// <summary>True from the press to the release of a drag.</summary>
    public bool IsDragging => _dragging;

    /// <summary>The shown value; setting it raises nothing.</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value) return;
            _value = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>The user change: moves to <paramref name="value"/> and raises <see cref="ValueChanged"/> when it differs.</summary>
    public void SetByUser(int value)
    {
        var clamped = Math.Clamp(value, Minimum, Maximum);
        if (clamped == _value) return;
        _value = clamped;
        InvalidateVisual();
        ValueChanged?.Invoke(clamped);
    }

    /// <summary>What a double-click does.</summary>
    public void ResetToDefault() => SetByUser(Default);

    private Rect Box => new(0, 0, Bounds.Width, Bounds.Height);

    private int ValueAt(double x) => TrackRowCells.SliderValueAt(TrackRowCells.Cell.Volume, Box, x + _grab, (Minimum, Maximum));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2) { ResetToDefault(); return; }
        var x = e.GetPosition(this).X;
        _grab = TrackRowCells.SliderGrabOffset(Box, _value, (Minimum, Maximum), x);
        _dragging = true;
        e.Pointer.Capture(this);
        Paint(TrackRowCells.CellPaint.Pressed);
        SetByUser(ValueAt(x));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragging) SetByUser(ValueAt(e.GetPosition(this).X));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    // The capture can be taken away mid-drag (the window deactivates, a popup opens): the drag ends as a release does.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        Released?.Invoke();
        Paint(IsPointerOver ? TrackRowCells.CellPaint.Hover : TrackRowCells.CellPaint.Idle);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { if (!_dragging) Paint(TrackRowCells.CellPaint.Hover); }

    protected override void OnPointerExited(PointerEventArgs e) { if (!_dragging) Paint(TrackRowCells.CellPaint.Idle); }

    private void Paint(TrackRowCells.CellPaint paint)
    {
        if (paint == _paint) return;
        _paint = paint;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        TrackRowCells.DrawSlider(_draw.Use(context), AvaloniaPalette.InkOf(light), Box, Minimum, Maximum, _value, _paint);
    }
}
