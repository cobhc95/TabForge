using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TabForge.Avalonia.Platform;

// Owns: the strip along the bottom edge of a Band card's fretboard, or of the card itself, that the user drags to resize it: it reports the height the pointer sets (measured from the
//   top of the control it measures), and shows a faint line in the board edge colour while the pointer is over it or dragging; the card's own strip shades with the theme's hover shade and shows the resize cursor.
// Does not own: the size itself or its limits (BandTrackPanel, BandFretboardSize), saving it (BandView).
// Tests: FretboardSizeTests.
internal sealed class BandFretboardGrip : Border
{
    /// <summary>The strip's height: wide enough to catch, thin enough to stay off the strings.</summary>
    public const double GripHeight = 8;

    /// <summary>The shade of the line while the pointer is over the strip or dragging.</summary>
    private const double LineOpacity = 0.7;
    private const double LineThickness = 2;
    private const string LineKey = "InstrumentBoardEdge";

    private readonly Control _board;
    private bool _dragging;
    private bool _over;
    private readonly bool _cardEdge;
    private readonly IBrush? _restFill;

    /// <param name="board">The fretboard whose top the height is measured from.</param>
    /// <param name="cardEdge">True for the card's own edge: it shades with the hover shade and shows the resize cursor.</param>
    public BandFretboardGrip(Control board, bool cardEdge = false)
    {
        _board = board;
        _cardEdge = cardEdge;
        if (cardEdge) Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        Height = GripHeight;
        VerticalAlignment = VerticalAlignment.Bottom;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = Brushes.Transparent;
        _restFill = Background;
        BorderThickness = new Thickness(0, 0, 0, LineThickness);
        BorderBrush = Brushes.Transparent;
        this.FollowTheme(ApplyLook);
    }

    /// <summary>Raised with the height the pointer asks for while it drags.</summary>
    public event Action<double>? Dragged;

    /// <summary>Raised when the drag ends.</summary>
    public event Action? Released;

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _over = true;
        ApplyLook();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _over = false;
        ApplyLook();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        ApplyLook();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { EndDrag(); return; }
        Dragged?.Invoke(e.GetPosition(_board).Y);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        e.Pointer.Capture(null);
        e.Handled = true;
        EndDrag();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        ApplyLook();
        Released?.Invoke();
    }

    private void ApplyLook()
    {
        if (_cardEdge) Background = _over || _dragging ? HoverShade.Brush(this) : _restFill;
        var brush = BandBrush.Of(this, LineKey);
        brush.Opacity = LineOpacity;
        BorderBrush = _over || _dragging ? brush : Brushes.Transparent;
    }
}
