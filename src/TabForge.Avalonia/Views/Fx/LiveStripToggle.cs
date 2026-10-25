using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Fx;

// Owns: one drawn toggle of the live input strip: the M and S buttons (drawn by the track list toggle painter, so the same boxes, red mute and yellow solo)
//   and the small lit "Tuner" switch (the accent fill while on).
// Does not own: what a toggle does (the strip owner) or the colours (TrackHeaderColumn.ToggleColours, the Accent token).
// Tests: TabForge.Avalonia.Tests.LiveStripSliderTests.
// Thread: UI thread. Nothing here changes its size or text; a change only repaints.
/// <summary>A two-state button. Setting <see cref="IsChecked"/> (or a click) raises <see cref="Toggled"/>; <see cref="Display"/> moves it silently.</summary>
public sealed class LiveStripToggle : Control
{
    /// <summary>Which look: the track list Mute or Solo box, or the accent-lit text switch.</summary>
    public enum Look { Mute, Solo, Lit }

    private readonly AvaloniaDrawContext _draw = new();
    private readonly Look _look;
    private readonly string _label;
    private TrackRowCells.CellPaint _paint;
    private bool _checked;

    public LiveStripToggle(Look look, string label, double width, double height)
    {
        _look = look; _label = label;
        Width = width; Height = height;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Raised when the state changes by a click or by setting <see cref="IsChecked"/>.</summary>
    public event Action<bool>? Toggled;

    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (value == _checked) return;
            _checked = value;
            InvalidateVisual();
            Toggled?.Invoke(value);
        }
    }

    /// <summary>Shows a state without raising <see cref="Toggled"/>.</summary>
    public void Display(bool on)
    {
        if (on == _checked) return;
        _checked = on;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        Paint(TrackRowCells.CellPaint.Pressed);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_paint != TrackRowCells.CellPaint.Pressed) return;
        e.Pointer.Capture(null);
        var inside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        Paint(inside ? TrackRowCells.CellPaint.Hover : TrackRowCells.CellPaint.Idle);
        if (inside) IsChecked = !_checked;
    }

    protected override void OnPointerEntered(PointerEventArgs e) { if (_paint == TrackRowCells.CellPaint.Idle) Paint(TrackRowCells.CellPaint.Hover); }

    protected override void OnPointerExited(PointerEventArgs e) { if (_paint == TrackRowCells.CellPaint.Hover) Paint(TrackRowCells.CellPaint.Idle); }

    private void Paint(TrackRowCells.CellPaint paint)
    {
        if (paint == _paint) return;
        _paint = paint;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var d = _draw.Use(context);
        var ink = AvaloniaPalette.InkOf(light);
        var box = new Rect(Bounds.Size);
        if (_look != Look.Lit)
        {
            TrackHeaderColumn.DrawToggle(d, ink, box, _look == Look.Mute ? TrackRowCells.Cell.Mute : TrackRowCells.Cell.Solo, _checked, _paint);
            return;
        }
        var fill = _checked ? ink("Accent") : ink("InstrumentBackground");
        var edge = _checked ? ink("Accent") : ink("InstrumentBoardEdge");
        d.Rect(fill, new UiStroke(edge, 1), box.X, box.Y, box.Width, box.Height, 4);
        TrackRowCells.DrawCellState(d, ink, box, fill, edge, _paint);
        var text = d.Text(_label, ChromeMetrics.UiFontFamily, ChromeMetrics.LiveStripFontSize, _checked ? ThemeColours.InkOn(fill) : ink("TextPrimary"), d.PixelsPerDip, weight: 700);
        d.DrawText(text, box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2);
    }
}
