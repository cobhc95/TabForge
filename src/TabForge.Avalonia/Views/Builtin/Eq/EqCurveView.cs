using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EqEffect = TabForge.Audio.Contracts.Builtin.EqParameters;
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
    private Pen? _gridPen, _curvePen, _mutedPen, _ringPen;
    private IBrush? _mutedBrush, _paperBrush, _accentBrush, _textBrush;
    private readonly Dictionary<double, FormattedText> _dbLabels = new();
    private readonly Dictionary<double, FormattedText> _hzLabels = new();
    internal int RebuildCount { get; private set; }

    public EqCurveView()
    {
        Name = "EqCurve";
        MinHeight = DialogMetrics.FxGraphMinHeight;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => { _gridPen = _curvePen = _mutedPen = _ringPen = null; _mutedBrush = _paperBrush = _accentBrush = _textBrush = null; _dbLabels.Clear(); _hzLabels.Clear(); InvalidateVisual(); };
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
        RebuildCount++;
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

    /// <summary>One pointer move: frequency, gain (and enable on first touch) applied with a single repaint (see audit-performance).</summary>
    internal void DragBand(int band, double hz, double db)
    {
        var onIndex = EqEffect.Index(band, EqEffect.OnField);
        var freqIndex = EqEffect.Index(band, EqEffect.FreqField);
        var gainIndex = EqEffect.Index(band, EqEffect.GainField);
        hz = EqEffect.Descriptors[freqIndex].Clamp(Math.Round(hz, 1));
        db = EqEffect.Descriptors[gainIndex].Clamp(Math.Round(db, 1));
        var dirty = false;
        if (_values[onIndex] != 1) { _values[onIndex] = 1; Changed?.Invoke(EqEffect.Descriptors[onIndex].Id, 1); dirty = true; }
        if (_values[freqIndex] != hz) { _values[freqIndex] = hz; Changed?.Invoke(EqEffect.Descriptors[freqIndex].Id, hz); dirty = true; }
        if (_values[gainIndex] != db) { _values[gainIndex] = db; Changed?.Invoke(EqEffect.Descriptors[gainIndex].Id, db); dirty = true; }
        if (dirty) Rebuild();
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
        DragBand(_dragging, HzOf(at.X), DbOf(at.Y));
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
        _gridPen ??= new Pen(BuiltinPaint.Theme(this, "BorderSoftBrush"), DialogMetrics.FxGridStroke);
        _mutedBrush ??= BuiltinPaint.Theme(this, "TextMuted");
        _paperBrush ??= BuiltinPaint.Theme(this, "WindowBackground");
        _mutedPen ??= new Pen(_mutedBrush, DialogMetrics.FxGridStroke);
        _curvePen ??= new Pen(BuiltinPaint.Theme(this, "AccentBrush"), DialogMetrics.FxCurveStroke);
        _accentBrush ??= BuiltinPaint.Theme(this, "AccentBrush");
        _textBrush ??= BuiltinPaint.Theme(this, "TextPrimary");
        var grid = _gridPen;
        var muted = _mutedBrush;
        var plot = Plot;
        context.FillRectangle(_paperBrush, plot);
        for (var db = -CurveSpanDb; db <= CurveSpanDb; db += GridStepDb)
        {
            context.DrawLine(grid, new Point(plot.Left, YOf(db)), new Point(plot.Right, YOf(db)));
            if (!_dbLabels.TryGetValue(db, out var label))
                _dbLabels[db] = label = BuiltinPaint.Label(db > 0 ? $"+{db:0}" : $"{db:0}", muted);
            context.DrawText(label, new Point(DialogMetrics.FxLabelInset, YOf(db) - DialogMetrics.FontSize / 2));
        }
        foreach (var hz in GridHz)
        {
            context.DrawLine(grid, new Point(XOf(hz), plot.Top), new Point(XOf(hz), plot.Bottom));
            if (!_hzLabels.TryGetValue(hz, out var label))
                _hzLabels[hz] = label = BuiltinPaint.Label(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", muted);
            context.DrawText(label, new Point(XOf(hz) + DialogMetrics.FxLabelInset, plot.Bottom + DialogMetrics.FxLabelInset / 2));
        }
        context.DrawRectangle(grid, plot);
        context.DrawLine(_mutedPen, new Point(plot.Left, YOf(0)), new Point(plot.Right, YOf(0)));
        if (_built != size) BuildCurve(size);
        if (_curve is not null) context.DrawGeometry(null, _curvePen, _curve);
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
        _paperBrush ??= BuiltinPaint.Theme(this, "WindowBackground");
        _ringPen ??= new Pen(BuiltinPaint.Theme(this, "TextPrimary"), DialogMetrics.FxGridStroke);
        context.DrawEllipse(on ? colour : _paperBrush, new Pen(colour, DialogMetrics.FxCurveStroke), centre, radius, radius);
        if (band == _selected)
        {
            var ring = radius + DialogMetrics.FxGridStroke * 2;
            context.DrawEllipse(null, _ringPen, centre, ring, ring);
        }
        _textBrush ??= BuiltinPaint.Theme(this, "TextPrimary");
        context.DrawText(BuiltinPaint.Label((band + 1).ToString(), on ? _paperBrush : _textBrush), new Point(centre.X - DialogMetrics.FxLabelInset, centre.Y - DialogMetrics.FontSize / 2));
    }
}
