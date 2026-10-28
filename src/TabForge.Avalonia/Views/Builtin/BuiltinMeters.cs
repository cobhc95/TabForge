using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the two small drawn indicators of the built-in effect windows: a horizontal level bar (gain reduction from the right, a level from the left, with an
//   optional marker such as the gate threshold) and a round on / off light (the gate open light). They repaint only when the shown value really changed.
// Does not own: where the value comes from (the effect readouts, polled by BuiltinEffectPanel), the dB to bar mapping (the windows).
// Tests: BuiltinEditorWindowTests.
public sealed class MeterBar : Control
{
    private const double MinStep = 0.002, ReleaseStep = 0.05;   // a level rises at once and falls by at most ReleaseStep per update: no flicker
    private readonly bool _fromRight;
    private double _fraction, _marker = double.NaN;
    private string _tone;

    public MeterBar(string name, string tone, bool fromRight = false)
    {
        (Name, _tone, _fromRight) = (name, tone, fromRight);
        Height = DialogMetrics.FxMeterHeight;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double Fraction => _fraction;

    /// <summary>Shows a level from 0 to 1 and the palette key of the colour it is drawn in; repaints only when either changed.</summary>
    public void Set(double fraction, string? tone = null)
    {
        fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        if (fraction < _fraction) fraction = Math.Max(fraction, _fraction - ReleaseStep);
        var toneChanged = tone is not null && tone != _tone;
        if (Math.Abs(fraction - _fraction) < MinStep && !toneChanged && !(fraction == 0 && _fraction != 0)) return;
        (_fraction, _tone) = (fraction, tone ?? _tone);
        InvalidateVisual();
    }

    /// <summary>A vertical mark at <paramref name="fraction"/> of the width (NaN: none).</summary>
    public void SetMarker(double fraction)
    {
        if (_marker.Equals(fraction)) return;
        _marker = fraction;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 4 || size.Height < 2) return;
        var radius = DialogMetrics.FxMeterRadius;
        context.DrawRectangle(BuiltinPaint.Theme(this, "WindowBackground"), new Pen(BuiltinPaint.Theme(this, "BorderSoftBrush"), DialogMetrics.FxGridStroke), new Rect(size), radius, radius);
        if (_fraction > 0)
        {
            var width = Math.Max(radius * 2, size.Width * _fraction);
            context.DrawRectangle(BuiltinPaint.Palette(this, _tone), null, new Rect(_fromRight ? size.Width - width : 0, 0, width, size.Height), radius, radius);
        }
        if (!double.IsNaN(_marker))
        {
            var x = size.Width * Math.Clamp(_marker, 0, 1);
            var mark = new Pen(BuiltinPaint.Theme(this, "TextPrimary"), DialogMetrics.FxMarkerStroke);
            context.DrawLine(mark, new Point(x, 0), new Point(x, size.Height));
        }
    }
}

public sealed class StateLight : Control
{
    private bool _on;

    public StateLight(string name)
    {
        Name = name;
        Width = Height = DialogMetrics.FxLightSize;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public bool On => _on;

    public void Set(bool on)
    {
        if (on == _on) return;
        _on = on;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var half = DialogMetrics.FxLightSize / 2;
        var fill = _on ? BuiltinPaint.Palette(this, "Success") : BuiltinPaint.Theme(this, "WindowBackground");
        context.DrawEllipse(fill, new Pen(BuiltinPaint.Theme(this, "BorderSoftBrush"), DialogMetrics.FxGridStroke), new Point(half, half), half - DialogMetrics.FxGridStroke, half - DialogMetrics.FxGridStroke);
    }
}
