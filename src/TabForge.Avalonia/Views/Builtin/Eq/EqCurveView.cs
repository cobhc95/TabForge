using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TabForge.AudioEngine.Builtin.Eq;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the EQ frequency curve (20 Hz to 20 kHz, plus and minus 18 dB) computed from the filter coefficients (EqDesign, the same as the sound), and the four band
//   points on it: drag = frequency and gain, mouse wheel = Q, double-click = reset the band; a coloured dot per band (hollow when the band is off), the last
//   touched band larger. The geometry is rebuilt only when a value or the size changed, never per frame.
// Does not own: sending the values (the Changed event; EqEditor sends them), the sliders under the curve, the filter maths (EqDesign).
// Tests: BuiltinEditorWindowTests.
public sealed class EqCurveView : Control
{
    private const double DisplayRate = 48000, CurveSpanDb = EqEffect.MaxGainDb, GridStepDb = 6, FreqRatioPerPoint = 1.025;
    private static readonly IReadOnlyList<string> BandColours = ["Accent", "Success", "Warning", "Danger"];
    private static readonly IReadOnlyList<double> GridHz = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];

    private readonly double[] _values = EqEffect.Descriptors.Select(p => p.Default).ToArray();
    private StreamGeometry? _curve;
    private Size _built;
    private int _selected, _dragging = -1;

    public EqCurveView()
    {
        Name = "EqCurve";
        MinHeight = DialogMetrics.FxGraphMinHeight;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        DoubleTapped += ResetBandAt;
    }

    /// <summary>A band value changed by the person: the parameter id and the new value (the window sends it to the engine).</summary>
    public event Action<uint, double>? Changed;

    /// <summary>The band touched last (0..3).</summary>
    public int Selected => _selected;

    /// <summary>The value at <paramref name="index"/> of <see cref="EqEffect.Descriptors"/> as drawn now.</summary>
    public double ValueAt(int index) => _values[index];

    /// <summary>Takes the engine values (indexed like <see cref="EqEffect.Descriptors"/>); ignored while a point is being dragged.</summary>
    public void SetValues(ReadOnlySpan<double> values)
    {
        if (_dragging >= 0) return;
        var changed = false;
        for (var i = 0; i < _values.Length && i < values.Length; i++)
        {
            if (_values[i] == values[i]) continue;
            _values[i] = values[i];
            changed = true;
        }
        if (changed) Rebuild();
    }

    private void Rebuild()
    {
        _built = default;
        InvalidateVisual();
    }

    private Rect Plot => new(DialogMetrics.FxPlotLeft, DialogMetrics.FxPlotTop,
        Math.Max(1, Bounds.Width - DialogMetrics.FxPlotLeft - DialogMetrics.FxPlotRight), Math.Max(1, Bounds.Height - DialogMetrics.FxPlotTop - DialogMetrics.FxPlotBottom));

    private double XOf(double hz) => Plot.X + Plot.Width * Math.Log(hz / EqEffect.MinHz) / Math.Log(EqEffect.MaxHz / EqEffect.MinHz);

    private double YOf(double db) => Plot.Y + Plot.Height * (CurveSpanDb - db) / (2 * CurveSpanDb);

    private double HzOf(double x) => EqEffect.MinHz * Math.Pow(EqEffect.MaxHz / EqEffect.MinHz, Math.Clamp((x - Plot.X) / Plot.Width, 0, 1));

    private double DbOf(double y) => CurveSpanDb - Math.Clamp((y - Plot.Y) / Plot.Height, 0, 1) * 2 * CurveSpanDb;

    /// <summary>Where the dot of a band is drawn (its frequency and gain).</summary>
    public Point DotPosition(int band) => new(XOf(_values[EqEffect.Index(band, EqEffect.FreqField)]), YOf(_values[EqEffect.Index(band, EqEffect.GainField)]));

    private int BandAt(Point p, double reach)
    {
        var best = -1;
        var bestDistance = reach;
        for (var b = 0; b < EqEffect.Bands; b++)
        {
            var dot = DotPosition(b);
            var d = Math.Sqrt((dot.X - p.X) * (dot.X - p.X) + (dot.Y - p.Y) * (dot.Y - p.Y));
            if (d <= bestDistance) { best = b; bestDistance = d; }
        }
        return best;
    }

    private void Set(int band, int field, double value)
    {
        var index = EqEffect.Index(band, field);
        var parameter = EqEffect.Descriptors[index];
        value = parameter.Clamp(value);
        if (_values[index] == value) return;
        _values[index] = value;
        Changed?.Invoke(parameter.Id, value);
        Rebuild();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var band = BandAt(e.GetPosition(this), DialogMetrics.FxDotHit);
        if (band < 0) return;
        (_selected, _dragging) = (band, band);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging < 0) return;
        var at = e.GetPosition(this);
        Set(_dragging, EqEffect.OnField, 1);
        Set(_dragging, EqEffect.FreqField, Math.Round(HzOf(at.X), 1));
        Set(_dragging, EqEffect.GainField, Math.Round(DbOf(at.Y), 1));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging < 0) return;
        _dragging = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var band = BandAt(e.GetPosition(this), DialogMetrics.FxDotHit * 2);
        if (band < 0) band = _selected;
        _selected = band;
        var q = _values[EqEffect.Index(band, EqEffect.QField)];
        Set(band, EqEffect.QField, Math.Round(q * Math.Pow(DialogMetrics.FxQStep, Math.Sign(e.Delta.Y)), 2));
        e.Handled = true;
        InvalidateVisual();
    }

    private void ResetBandAt(object? sender, TappedEventArgs e)
    {
        var band = BandAt(e.GetPosition(this), DialogMetrics.FxDotHit);
        if (band < 0) return;
        ResetBand(band);
        e.Handled = true;
    }

    /// <summary>Puts a band back to its default frequency, no gain and its default Q, and turns it on (the double-click on its point).</summary>
    public void ResetBand(int band)
    {
        _selected = band;
        Set(band, EqEffect.OnField, 1);
        Set(band, EqEffect.FreqField, EqEffect.Descriptors[EqEffect.Index(band, EqEffect.FreqField)].Default);
        Set(band, EqEffect.GainField, 0);
        Set(band, EqEffect.QField, EqEffect.Descriptors[EqEffect.Index(band, EqEffect.QField)].Default);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 40 || size.Height < 40) return;
        var grid = new Pen(BuiltinPaint.Theme(this, "BorderSoftBrush"), DialogMetrics.FxGridStroke);
        var muted = BuiltinPaint.Theme(this, "TextMuted");
        var plot = Plot;
        context.FillRectangle(BuiltinPaint.Theme(this, "WindowBackground"), plot);
        for (var db = -CurveSpanDb; db <= CurveSpanDb; db += GridStepDb)
        {
            context.DrawLine(grid, new Point(plot.Left, YOf(db)), new Point(plot.Right, YOf(db)));
            context.DrawText(BuiltinPaint.Label(db > 0 ? $"+{db:0}" : $"{db:0}", muted), new Point(DialogMetrics.FxLabelInset, YOf(db) - DialogMetrics.FontSize / 2));
        }
        foreach (var hz in GridHz)
        {
            context.DrawLine(grid, new Point(XOf(hz), plot.Top), new Point(XOf(hz), plot.Bottom));
            context.DrawText(BuiltinPaint.Label(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", muted), new Point(XOf(hz) + DialogMetrics.FxLabelInset, plot.Bottom + DialogMetrics.FxLabelInset / 2));
        }
        context.DrawRectangle(grid, plot);
        context.DrawLine(new Pen(muted, DialogMetrics.FxGridStroke), new Point(plot.Left, YOf(0)), new Point(plot.Right, YOf(0)));
        if (_built != size) BuildCurve(size);
        if (_curve is not null) context.DrawGeometry(null, new Pen(BuiltinPaint.Theme(this, "AccentBrush"), DialogMetrics.FxCurveStroke), _curve);
        for (var b = 0; b < EqEffect.Bands; b++) DrawDot(context, b);
    }

    private void BuildCurve(Size size)
    {
        _built = size;
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            var first = true;
            for (var hz = EqEffect.MinHz; ; hz = Math.Min(EqEffect.MaxHz, hz * FreqRatioPerPoint))
            {
                var db = Math.Clamp(EqDesign.ResponseDb(_values, hz, DisplayRate), -CurveSpanDb, CurveSpanDb);
                var point = new Point(XOf(hz), YOf(db));
                if (first) { g.BeginFigure(point, false); first = false; } else g.LineTo(point);
                if (hz >= EqEffect.MaxHz) break;
            }
            g.EndFigure(false);
        }
        _curve = curve;
    }

    private void DrawDot(DrawingContext context, int band)
    {
        var centre = DotPosition(band);
        var colour = BuiltinPaint.Palette(this, BandColours[band]);
        var on = _values[EqEffect.Index(band, EqEffect.OnField)] >= 0.5;
        var radius = band == _selected ? DialogMetrics.FxDotSelectedRadius : DialogMetrics.FxDotRadius;
        context.DrawEllipse(on ? colour : BuiltinPaint.Theme(this, "WindowBackground"), new Pen(colour, DialogMetrics.FxCurveStroke), centre, radius, radius);
        if (band == _selected)
        {
            var ring = radius + DialogMetrics.FxGridStroke * 2;
            context.DrawEllipse(null, new Pen(BuiltinPaint.Theme(this, "TextPrimary"), DialogMetrics.FxGridStroke), centre, ring, ring);
        }
        context.DrawText(BuiltinPaint.Label((band + 1).ToString(), BuiltinPaint.Theme(this, on ? "WindowBackground" : "TextPrimary")), new Point(centre.X - DialogMetrics.FxLabelInset, centre.Y - DialogMetrics.FontSize / 2));
    }
}
