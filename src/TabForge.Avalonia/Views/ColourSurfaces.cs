using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the two drawn surfaces of the colour picker popup: a position surface that reports where it was pressed, dragged or nudged by the arrow
//   keys (0 to 1 each way), the saturation and brightness square at one hue, and the vertical hue strip.
// Does not own: what the position means (ColourPickerPresenter) or the popup layout (ColourPickerPopup). The white, black and hue colours are the
//   colour model's own, not theme colours.
// Tests: TabForge.Avalonia.Tests.ColourPickerPopupTests.
public abstract class ColourSurface : Control
{
    private const double KeyStep = 0.02;

    private static readonly ImmutableSolidColorBrush Light = new ImmutableSolidColorBrush(Model(new Rgba(255, 255, 255))), Dark = new ImmutableSolidColorBrush(Model(new Rgba(0, 0, 0)));
    private static readonly IPen LightRing = new ImmutablePen(Light, DialogMetrics.MarkerLine), DarkRing = new ImmutablePen(Dark, DialogMetrics.MarkerLine / 2);

    protected ColourSurface() => Focusable = true;

    /// <summary>Raised when the position changes by the pointer or the keys; x and y are 0 to 1.</summary>
    public event Action<double, double>? Moved;

    /// <summary>The marker's place across, 0 to 1.</summary>
    public double X { get; private set; }

    /// <summary>The marker's place down, 0 to 1.</summary>
    public double Y { get; private set; }

    /// <summary>Moves the marker without raising <see cref="Moved"/>.</summary>
    public void Place(double x, double y)
    {
        X = Math.Clamp(x, 0, 1);
        Y = Math.Clamp(y, 0, 1);
        InvalidateVisual();
    }

    /// <summary>Moves the marker and raises <see cref="Moved"/>, as a press or drag does.</summary>
    public void Pick(double x, double y)
    {
        Place(x, y);
        Moved?.Invoke(X, Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        e.Pointer.Capture(this);
        PickAt(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.Pointer.Captured == this) PickAt(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var (dx, dy) = e.Key switch { Key.Left => (-KeyStep, 0), Key.Right => (KeyStep, 0), Key.Up => (0, -KeyStep), Key.Down => (0, KeyStep), _ => (0.0, 0.0) };
        if (dx == 0 && dy == 0) return;
        Pick(X + dx, Y + dy);
        e.Handled = true;
    }

    private void PickAt(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        Pick(point.X / Math.Max(1, Bounds.Width), point.Y / Math.Max(1, Bounds.Height));
    }

    /// <summary>Draws the marker: a dark ring inside a light one, readable on any colour.</summary>
    protected void DrawMarker(DrawingContext context)
    {
        var centre = new Point(X * Bounds.Width, Y * Bounds.Height);
        context.DrawEllipse(null, LightRing, centre, DialogMetrics.MarkerRadius, DialogMetrics.MarkerRadius);
        context.DrawEllipse(null, DarkRing, centre, DialogMetrics.MarkerRadius + DialogMetrics.MarkerLine, DialogMetrics.MarkerRadius + DialogMetrics.MarkerLine);
    }

    protected static Color Model(Rgba colour) => AvaloniaPalette.ColorOf(colour);
}

/// <summary>The saturation (across) and brightness (down, full at the top) square at one hue.</summary>
public sealed class ColourSquare : ColourSurface
{
    private static readonly IBrush WhiteFade = Fade(new Rgba(255, 255, 255), new Rgba(255, 255, 255, 0), horizontal: true), BlackFade = Fade(new Rgba(0, 0, 0, 0), new Rgba(0, 0, 0), horizontal: false);

    private double _hue;
    private IBrush? _hueBrush;

    /// <summary>The hue the square shows, 0 to 359.</summary>
    public void ShowHue(double hue)
    {
        _hue = hue;
        _hueBrush = new ImmutableSolidColorBrush(Model(ColourChooserPresenter.ColourFromSliders(hue, 100, 100)));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        if (_hueBrush is not null) context.FillRectangle(_hueBrush, area);
        context.FillRectangle(WhiteFade, area);
        context.FillRectangle(BlackFade, area);
        DrawMarker(context);
    }

    private static IBrush Fade(Rgba from, Rgba to, bool horizontal) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = horizontal ? new RelativePoint(1, 0, RelativeUnit.Relative) : new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Model(from), 0), new GradientStop(Model(to), 1) },
    }.ToImmutable();
}

/// <summary>The vertical hue strip: red at the top through every hue back to red.</summary>
public sealed class HueStrip : ColourSurface
{
    private const int Stops = 6;

    private static readonly IBrush Spectrum = BuildSpectrum();

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Spectrum, new Rect(Bounds.Size));
        DrawMarker(context);
    }

    private static IBrush BuildSpectrum()
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        for (var i = 0; i <= Stops; i++)
            brush.GradientStops.Add(new GradientStop(Model(ColourChooserPresenter.ColourFromSliders(i * 60.0, 100, 100)), i / (double)Stops));
        return brush.ToImmutable();
    }
}
